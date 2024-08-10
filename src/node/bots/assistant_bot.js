// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

const { ConversationData, ConversationTurn } = require('../data_models/conversation_data');
const { StateManagementBot } = require('./state_management_bot');

class AssistantBot extends StateManagementBot {

    constructor(conversationState, userState, aoaiClient) {
        super(conversationState, userState);
        this._aoaiClient = aoaiClient;

        this.onMembersAdded(async (context, next) => {
            const membersAdded = context.activity.membersAdded;
            for (let member of membersAdded) {
                if (member.id !== context.activity.recipient.id) {
                    await context.sendActivity("Hello and welcome to the Assistant Bot NodeJS!");
                }
            }
            await next();
        });

        this.onMessage(async (context, next) => {
            // Load conversation state
            let conversationData = await this.conversationDataAccessor.get(context, new ConversationData([]));

            // Create a new thread if one does not exist
            let thread
            if (conversationData.thread_id === null) {
                thread = await this._aoaiClient.beta.threads.create();
                conversationData.thread_id = thread.id;
            }

            // Check if this is a file upload and process it
            let fileUploaded = await this.handleFileUploads(context, thread.id);
            // Return early if it is
            if (fileUploaded) {
                return;
            }

            // Add user message to history
            conversationData.history.push(new ConversationTurn('user', context.activity.text));
            if (conversationData.history.length >= conversationData.max_turns) {
                conversationData.history.splice(1, 1);
            }

            // Send user message to thread
            this._aoaiClient.beta.threads.messages.create(
                conversationData.thread_id,
                { 
                    role: 'user', 
                    content: context.activity.text 
                }
            );

            // Run thread
            let run = await this._aoaiClient.beta.threads.runs.create(
                conversationData.thread_id,
                {
                    assistant_id: process.env.AZURE_OPENAI_ASSISTANT_ID,
                    instructions: process.env.LLM_INSTRUCTIONS,
                    stream: true
                }
            );

            // Start streaming response
            let currentMessage = "";
            for await (let event of run.iterator()) {
                if (event.event == "thread.message.delta") {
                    currentMessage += event.data.delta.content[0].text.value;
                    // TO DO: Handle image blocks
                }
            }
            response = currentMessage

            // Add assistant message to history
            conversationData.history.push(new ConversationTurn('assistant', response));
            if (conversationData.history.length >= conversationData.max_turns) {
                conversationData.history.splice(1, 1);
            }

            // Respond back to user
            await context.sendActivity(response);
            next();
        });
    }

    // Helper to handle file uploads from user
    async handleFileUploads(context, threadId) {
        let filesUploaded = false;
        // Check if incoming message has attached files
        if (context.activity.attachments != null) {
            for (const attachment of context.activity.attachments) {
                filesUploaded = true;
                // Image uploads are not supported yet
                if (attachment.contentType.includes('image')) {
                    await context.sendActivity('Image uploads are not supported yet.');
                }

                // Download file and upload to Azure OpenAI Files API
                file = await fetch(attachment.contentUrl)
                const fileResponse = await this._aoaiClient.files.create({
                    file: file,
                    purpose: 'assistants'
                });

                // Send message to thread with file attachment
                await this._aoaiClient.beta.threads.messages.create(
                    threadId,
                    {
                        role: 'user',
                        content: `[${attachment.Name}]`,
                        attachments: [{
                            file_id: fileResponse.id,
                            tools: [{
                                type: "code_interpreter"
                            }]
                        }]
                    }
                );

                // Send confirmation of file upload
                await context.sendActivity('File uploaded successfully.');
            }
        }
        
        // Return true if files were uploaded
        return filesUploaded
    }
}

module.exports.AssistantBot = AssistantBot;