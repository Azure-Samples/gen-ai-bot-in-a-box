const { ConversationData, ConversationTurn } = require('../data_models/conversation_data');
const { StateManagementBot } = require('./state_management_bot');

class AssistantBot extends StateManagementBot {

    constructor(conversationState, userState, aoaiClient) {
        super(conversationState, userState);
        this._aoaiClient = aoaiClient;
        // See https://aka.ms/about-bot-activity-message to learn more about the message and other activity types.
        this.onMessage(async (context, next) => {
            try {
                let conversationData = await this.conversationDataAccessor.get(context, new ConversationData([]));

                conversationData.history.push(new ConversationTurn('user', context.activity.text));
                if (conversationData.history.length >= conversationData.max_turns) {
                    conversationData.history.splice(1, 1);
                }

                if (conversationData.thread_id === null) {
                    let thread = await this._aoaiClient.beta.threads.create();
                    conversationData.thread_id = thread.id;
                }

                this._aoaiClient.beta.threads.messages.create(
                    conversationData.thread_id, 
                    { role: 'user', content: context.activity.text }
                );


                let run = await this._aoaiClient.beta.threads.runs.create(
                    conversationData.thread_id,
                    {
                        assistant_id: process.env.AZURE_OPENAI_ASSISTANT_ID,
                        instructions: process.env.LLM_INSTRUCTIONS,
                        stream: true
                    }
                );

                let currentMessage = '';

                for await (let event of run.iterator()) {
                    if (event.event == "thread.message.delta") {
                        currentMessage += event.data.delta.content[0].text.value;
                    }
                }

                conversationData.history.push(new ConversationTurn('assistant', currentMessage));
                if (conversationData.history.length >= conversationData.max_turns) {
                    conversationData.history.splice(1, 1);
                }

                await context.sendActivity(currentMessage);
                next();
            } catch (error) {
                console.error(error);
                await context.sendActivity('The bot encountered an error or bug.');
                await context.sendActivity('To continue to run this bot, please fix the bot source code.');
                next({ "error": true })
            }
        });

        this.onMembersAdded(async (context, next) => {
            const membersAdded = context.activity.membersAdded;
            const welcomeText = 'Hello and welcome to the Assistant Bot!';
            for (let cnt = 0; cnt < membersAdded.length; ++cnt) {
                if (membersAdded[cnt].id !== context.activity.recipient.id) {
                    await context.sendActivity(welcomeText);
                }
            }
            // By calling next() you ensure that the next BotHandler is run.
            await next();
        });
    }
}

module.exports.AssistantBot = AssistantBot;