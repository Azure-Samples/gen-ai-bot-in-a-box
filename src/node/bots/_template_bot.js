// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

const { ConversationData, ConversationTurn } = require('../data_models/conversation_data');
const { StateManagementBot } = require('./state_management_bot');

class TemplateBot extends StateManagementBot {

    constructor(conversationState, userState) {
        super(conversationState, userState);
        this._instructions = process.env("LLM_INSTRUCTIONS")
        this._welcomeMessage = process.env("LLM_WELCOME_MESSAGE") || "Hello and welcome to the Template Bot NodeJS!"
        // Inject dependencies here

        // Modify onMembersAdded as needed
        this.onMembersAdded(async (context, next) => {
            const membersAdded = context.activity.membersAdded;
            for (let member of membersAdded) {
                if (member.id !== context.activity.recipient.id) {
                    await context.sendActivity(this._welcomeMessage);
                }
            }
            await next();
        });

        this.onMessage(async (context, next) => {
            // Load conversation state
            let conversationData = await this.conversationDataAccessor.get(context, new ConversationData([
                new ConversationTurn('system', this._instructions)
            ]));

            // Catch any special messages here

            // Add user message to history
            conversationData.history.push(new ConversationTurn('user', context.activity.text));
            if (conversationData.history.length >= conversationData.max_turns) {
                conversationData.history.splice(1, 1);
            }

            // Run logic to obtain response here
            let response = "Hello, world!";

            // Add assistant message to history
            conversationData.history.push(new ConversationTurn('assistant', response));
            if (conversationData.history.length >= conversationData.max_turns) {
                conversationData.history.splice(1, 1);
            }

            // Respond back to user
            await context.sendActivity(currentMessage);
            next();
        });
    }

}

module.exports.TemplateBot = TemplateBot;