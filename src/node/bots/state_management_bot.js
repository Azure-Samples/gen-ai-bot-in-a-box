const { ActivityHandler } = require('botbuilder');

class StateManagementBot extends ActivityHandler {
    constructor(conversationState, userState) {
        super();

        if (conversationState === null) {
            throw new TypeError(
                "[StateManagementBot]: Missing parameter. conversationState is required but null was given"
            );
        }
        if (userState === null) {
            throw new TypeError(
                "[StateManagementBot]: Missing parameter. userState is required but null was given"
            );
        }

        this.conversationState = conversationState;
        this.userState = userState;
        
        this.conversationDataAccessor = this.conversationState.createProperty("ConversationData");
        this.userProfileAccessor = this.userState.createProperty("UserProfile");
    }

    async run(context) {
        await super.run(context);
        // Save any state changes. The load happened during the execution of the Dialog.
        await this.conversationState.saveChanges(context, false);
        await this.userState.saveChanges(context, false);
    }
}

module.exports.StateManagementBot = StateManagementBot;