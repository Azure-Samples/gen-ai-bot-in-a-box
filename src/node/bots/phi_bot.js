// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

const { ConversationData, ConversationTurn } = require('../data_models/conversation_data');
const { StateManagementBot } = require('./state_management_bot');

class PhiBot extends StateManagementBot {

    constructor(conversationState, userState, dialog, phiClient) {
        super(conversationState, userState, dialog);
        this.phiClient = phiClient;
        this.welcomeMessage = process.env.LLM_WELCOME_MESSAGE || "Hello and welcome to the Chat Completions Bot NodeJS!";

        // Modify onMembersAdded as needed
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
            conversationData.history.push(new ConversationTurn('user', context.activity.text));
            if (conversationData.history.length >= conversationData.max_turns) {
                conversationData.history.splice(1, 1);
            }

            let completion = await this.phiClient.createCompletion(conversationData.history);
            let response = completion.choices[0].message.content;

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

}

module.exports.PhiBot = PhiBot;