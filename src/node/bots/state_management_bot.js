// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

const { ActivityHandler } = require('botbuilder');

class StateManagementBot extends ActivityHandler {
    constructor(conversationState, userState, dialog) {
        super()

        this.conversationState = conversationState;
        this.userState = userState;
        this.conversationDataAccessor = this.conversationState.createProperty("ConversationData");
        this.userProfileAccessor = this.userState.createProperty("UserProfile");
        this.dialog = dialog;
        this.ssoEnabled = process.env.SSO_ENABLED || false
        this.ssoConfigName = process.env.SSO_CONFIG_NAME || "default"
    }

    async run(context) {
        await super.run(context);
        // Save any state changes. The load happened during the execution of the Dialog.
        await this.conversationState.saveChanges(context, false);
        await this.userState.saveChanges(context, false);
    }

    async handleLogin(turnContext) {
        if (!this._ssoEnabled) {
            return true;
        }

        const userProfileAccessor = this._userState.createProperty('UserProfile');
        const userProfile = await userProfileAccessor.get(turnContext, () => ({}));

        const userTokenClient = turnContext.turnState.get(turnContext.adapter.UserTokenClientKey);

        try {
            const userToken = await userTokenClient.getUserToken(turnContext.activity.from.id, this._ssoConfigName, turnContext.activity.channelId);
            const decodedToken = jwt.decode(userToken.token);
            userProfile.name = decodedToken.name;
            return true;
        } catch (error) {
            await this._dialog.run(turnContext, this._conversationState.createProperty('DialogState'));
            return false;
        }
    }

    async handleLogout(turnContext) {
        const userTokenClient = turnContext.turnState.get(turnContext.adapter.UserTokenClientKey);
        await userTokenClient.signOutUser(turnContext.activity.from.id, this._ssoConfigName, turnContext.activity.channelId);
        await turnContext.sendActivityAsync("Signed out");
    }
}

module.exports.StateManagementBot = StateManagementBot;