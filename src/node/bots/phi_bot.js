const { ConversationData, ConversationTurn } = require('../data_models/conversation_data');
const { StateManagementBot } = require('./state_management_bot');

class PhiBot extends StateManagementBot {
    constructor(conversationState, userState, phiClient) {
        super(conversationState, userState);
        this._phiClient = phiClient;
        // See https://aka.ms/about-bot-activity-message to learn more about the message and other activity types.
        this.onMessage(async (context, next) => {
            try {
                let conversationData = await this.conversationDataAccessor.get(
                    context, new ConversationData([new ConversationTurn("system", process.env.LLM_INSTRUCTIONS)])
                );

                conversationData.history.push(new ConversationTurn('user', context.activity.text));
                if (conversationData.history.length >= conversationData.max_turns) {
                    conversationData.history.splice(1, 1);
                }

                let completion = await this._phiClient.createCompletion(conversationData.history);

                let response = completion.choices[0].message.content;

                conversationData.history.push(new ConversationTurn('assistant', response));
                if (conversationData.history.length >= conversationData.max_turns) {
                    conversationData.history.splice(1, 1);
                }

                await context.sendActivity(response);

                await next();
            } catch (error) {
                console.error(error);
                await context.sendActivity('The bot encountered an error or bug.');
                await context.sendActivity('To continue to run this bot, please fix the bot source code.');
                next({ "error": true })
            }
        });

        this.onMembersAdded(async (context, next) => {
            const membersAdded = context.activity.membersAdded;
            for (let cnt = 0; cnt < membersAdded.length; ++cnt) {
                if (membersAdded[cnt].id !== context.activity.recipient.id) {
                    await context.sendActivity('Hello and welcome to the Phi Bot!');
                }
            }
            await next();
        });
    }
}

module.exports.PhiBot = PhiBot;