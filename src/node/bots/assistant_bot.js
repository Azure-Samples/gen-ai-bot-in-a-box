// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

const { MessageFactory, ActionTypes } = require('botbuilder');
const { ConversationData } = require('../data_models/conversation_data');
const { StateManagementBot } = require('./state_management_bot');
const { File } = require('node:buffer');
const { mimeType } = require('../data_models/mime_type');

class AssistantBot extends StateManagementBot {

    constructor(conversationState, userState, dialog, aoaiClient) {
        super(conversationState, userState, dialog);
        this.deployment = process.env.AZURE_OPENAI_DEPLOYMENT_NAME;
        this.aoaiClient = aoaiClient;
        this.welcomeMessage = process.env.LLM_WELCOME_MESSAGE || "Hello and welcome to the Chat Completions Bot NodeJS!";

        this.onMembersAdded(async (context, next) => {
            const membersAdded = context.activity.membersAdded;
            for (let member of membersAdded) {
                if (member.id !== context.activity.recipient.id) {
                    await context.sendActivity(this.welcomeMessage);
                }
            }
            await next();
        });

        this.onMessage(async (context, next) => {
            // Enforce login
            let loggedIn = await this.handleLogin(context)
            if (!loggedIn) {
                return false
            }
            if (context.activity.text === 'logout') {
                return await this.handleLogout(context)
            }

            // Load conversation state
            let conversationData = await this.conversationDataAccessor.get(context, new ConversationData([]));

            // Create a new thread if one does not exist
            let thread
            if (conversationData.thread_id === null) {
                thread = await this.aoaiClient.beta.threads.create();
                conversationData.thread_id = thread.id;
            } else {
                thread = await this.aoaiClient.beta.threads.retrieve(conversationData.thread_id);
            }

            // Delete thread if user asks
            if (context.activity.text === 'clear') {
                await this.aoaiClient.beta.threads.del(conversationData.thread_id);
                conversationData.thread_id = null;
                conversationData.attachments = [];
                conversationData.history = [];
                await context.sendActivity('Conversation cleared!');
                return true;
            }

            // Check if this is a file upload and process it
            let fileUploaded = await this.handleFileUploads(context, thread.id, conversationData);

            // Return early if there is no text in the message
            if (context.activity.text == null) {
                return true;
            }

            // Check if this is a file upload follow up - user selects a file upload option
            if (context.activity.text.startsWith(":")) {
                // Get upload metadata
                let tool = context.activity.text.split(':').pop().trim()
                // Get file from attachments
                let attachment = conversationData.attachments[conversationData.attachments.length - 1]
                // Send the file to the assistant
                let file = await fetch(attachment.url)
                let fileResponse = await this.aoaiClient.files.create({
                    file: new File([await file.blob()], attachment.name),
                    purpose: 'assistants'
                })
                // Add file upload to relevant tool
                let tools = []
                if (tool == "Code Interpreter")
                    tools.push({
                        "type": "code_interpreter"
                    })
                if (tool == "File Search")
                    tools.push({
                        "type": "file_search"
                    })
                await this.aoaiClient.beta.threads.messages.create(
                    conversationData.thread_id,
                    {
                        role: 'user',
                        content: `File uploaded: ${attachment.name}`,
                        attachments: [{
                            "file_id": fileResponse.id,
                            "tools": tools
                        }]
                    })
                // Send feedback to user
                await context.sendActivity(`File added to ${tool} successfully!`);
                return true
            }

            // Add user message to history
            ConversationData.addTurn(conversationData, 'user', context.activity.text);

            // Send user message to thread
            await this.aoaiClient.beta.threads.messages.create(
                conversationData.thread_id,
                {
                    role: 'user',
                    content: context.activity.text
                }
            );

            // Run thread
            let run = await this.aoaiClient.beta.threads.runs.create(
                conversationData.thread_id,
                {
                    assistant_id: process.env.AZURE_OPENAI_ASSISTANT_ID,
                    instructions: process.env.LLM_INSTRUCTIONS,
                    stream: true
                }
            );

            // Process run streaming
            await this.processRunStreaming(run, conversationData, context)

            next();

            return true
        });
    }


    async processRunStreaming(run, conversationData, context, streamId = null) {
        // Start streaming response
        let currentMessage = ""
        let toolOutputs = []
        let currentRunId = ""
        let activityId = ""
        let streamSequence = 1

        activityId = await this.sendInterimMessage(context, "Typing...", streamSequence, streamId, "typing")

        for await (let event of run.iterator()) {
            if (event.event == "thread.run.created")
                currentRunId = event.data.id
            if (event.event == "thread.run.failed") {
                currentMessage = event.data.last_error.message
                break
            }
            if (event.event == "thread.run.requires_action") {
                let tool_calls = event.data.required_action.submit_tool_outputs.tool_calls
                for (let tool_call of tool_calls) {
                    let args = JSON.parse(tool_call.function.arguments)
                    if (tool_call.function.name == "image_query") {
                        let response = await this.imageQuery(conversationData, args["query"], args["image_name"])
                        toolOutputs.push({ "tool_call_id": tool_call.id, "output": response })
                    }
                }

            }
            if (event.event == "thread.message.delta") {
                let deltaBlock = event.data.delta.content[0]
                if (deltaBlock.type == "text") {
                    currentMessage += event.data.delta.content[0].text.value;
                    // Flush content every 50 messages
                    if (streamSequence % 50 == 0) {
                        await this.sendInterimMessage(context, currentMessage, ++streamSequence, activityId, "typing")
                    }
                } else if (deltaBlock.type == "image_file") {
                    currentMessage += `![${deltaBlock.image_file.file_id}](/api/files/${deltaBlock.image_file.file_id})`
                }
            }
        }

        // Recursively process the run with the tool outputs
        if (toolOutputs.length > 0) {
            let newRun = await this.aoaiClient.beta.threads.runs.submitToolOutputs(
                conversationData.thread_id,
                currentRunId,
                { tool_outputs: toolOutputs, stream: true }
            )
            await this.processRunStreaming(newRun, conversationData, context, activityId)
            return
        }

        // Add assistant message to history
        ConversationData.addTurn(conversationData, 'assistant', currentMessage)

        // Respond back to user
        await this.sendInterimMessage(context, currentMessage, ++streamSequence, activityId, "message")
    }

    // Helper to handle file uploads from user
    async handleFileUploads(context, threadId, conversationData) {
        let filesUploaded = false;
        // Check if incoming message has attached files
        if (context.activity.attachments != null) {
            for (const attachment of context.activity.attachments) {
                if (attachment.contentUrl == null) {
                    continue;
                }
                filesUploaded = true;
                let downloadUrl = attachment.contentUrl;
                if (attachment.content && attachment.content.downloadUrl)
                    downloadUrl = attachment.content.downloadUrl
                // Add file to attachments in case we need to reference it in Function Calling
                conversationData.attachments.push({
                    name: attachment.name,
                    contentType: mimeType(attachment.name),
                    url: downloadUrl
                })

                // Add file upload notice to conversation history, frontend, and assistant
                ConversationData.addTurn(conversationData, 'user', `File uploaded: ${attachment.name}`)
                await context.sendActivity(`File uploaded: ${attachment.name}`)
                await this.aoaiClient.beta.threads.messages.create(
                    threadId,
                    {
                        role: 'user',
                        content: `File uploaded: ${attachment.name}`
                    }
                )
                // Ask whether to add file to a tool
                await context.sendActivity(MessageFactory.suggestedActions(
                    [
                        {
                            title: ":Code Interpreter",
                            type: ActionTypes.ImBack,
                            value: ":Code Interpreter"
                        },
                        {
                            title: ":File Search",
                            type: ActionTypes.ImBack,
                            value: ":File Search"
                        }
                    ],
                    "Add to a tool? (ignore if not needed)",
                ))
            }
        }

        // Return true if files were uploaded
        return filesUploaded
    }

    async imageQuery(conversationData, query, imageName) {
        // Find image in attachments by name
        let image = conversationData.attachments.find(a => a.name == imageName.split("/")[imageName.split("/").length - 1])

        // Read image.url
        let data = await fetch(image.url)
        let bytes = Buffer.from(await data.arrayBuffer()).toString('base64')

        // Send image to assistant
        let response = await this.aoaiClient.chat.completions.create({
            model: this.deployment,
            messages: [
                {
                    "role": "user", "content": [
                        { "type": "text", "text": query },
                        {
                            "type": "image_url", "image_url": {
                                "url": `data:${image.contentType};base64,${bytes}`
                            }
                        }
                    ]
                }
            ]
        })
        return response.choices[0].message.content

    }

}

module.exports.AssistantBot = AssistantBot;