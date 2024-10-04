// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

const { ConversationData, ConversationTurn } = require('../data_models/conversation_data');
const { StateManagementBot } = require('./state_management_bot');

class ChatCompletionBot extends StateManagementBot {

    constructor(conversationState, userState, dialog, aoaiClient) {
        super(conversationState, userState, dialog);
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
            // Load conversation state
            let conversationData = await this.conversationDataAccessor.get(context, new ConversationData([]));

            // Add user message to history
            ConversationData.addTurn(conversationData, 'user', context.activity.text);

            let extraBody = {}
            if (process.env.AZURE_SEARCH_API_ENDPOINT)
                extraBody['data_sources'] = [
                    {
                        "type": "azure_search",
                        "parameters": {
                            "endpoint": process.env.AZURE_SEARCH_API_ENDPOINT,
                            "index_name": process.env.AZURE_SEARCH_INDEX,
                            "authentication": process.env.AZURE_SEARCH_API_ENDPOINT ? {
                                "type": "system_assigned_managed_identity",
                            } : {
                                "type": "api_key",
                                "value": process.env.AZURE_SEARCH_API_KEY
                            }
                        }
                    }
                ]
            let completion = await this.aoaiClient.chat.completions.create(
                {
                    model: process.env.AZURE_OPENAI_DEPLOYMENT_NAME,
                    messages: ConversationData.toMessages(conversationData.history),
                    data_sources: process.env.AZURE_SEARCH_API_ENDPOINT ? [
                        {
                            "type": "azure_search",
                            "parameters": {
                                "endpoint": process.env.AZURE_SEARCH_API_ENDPOINT,
                                "index_name": process.env.AZURE_SEARCH_INDEX,
                                "authentication": process.env.AZURE_SEARCH_API_KEY ? {
                                    "type": "api_key",
                                    "value": process.env.AZURE_SEARCH_API_KEY
                                } : {
                                    "type": "system_assigned_managed_identity",
                                }
                            }
                        }
                    ] : undefined,
                },
            );

            let response = completion.choices[0].message.content;

            // Add assistant message to history
            ConversationData.addTurn(conversationData, "assistant", response);
            if (conversationData.history.length >= conversationData.max_turns) {
                conversationData.history.splice(1, 1);
            }

            // Respond back to user
            await context.sendActivity(response);
            next();
        });
    }

    async handleFileUploads(context, conversationData) {
        let filesUploaded = false
        // Check if incoming message has attached files
        if (context.activity.attachments) {
            for (let attachment of context.activity.attachments) {
                if (attachment.contentUrl == null) {
                    continue;
                }
                filesUploaded = true
                // Add file to attachments in case we need to reference it in Function Calling
                conversationData.attachments.push({
                    name: attachment.name,
                    contentType: attachment.contentType,
                    url: attachment.contentUrl
                })
                // If attachment is an image, add it to the conversation history as base64
                if (attachment.contentType.includes("image")) {

                    let data = await fetch(attachment.contentUrl)
                    let bytes = Buffer.from(await data.arrayBuffer()).toString('base64')
                    ConversationData.addTurn(conversationData, "user", `File uploaded: ${attachment.Name}`, attachment.ContentType, bytes);
                }
            }
        }
    }
}

module.exports.ChatCompletionBot = ChatCompletionBot;