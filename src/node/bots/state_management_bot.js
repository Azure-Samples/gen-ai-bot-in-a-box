// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

const { ActivityHandler } = require('botbuilder');
const { DialogSet, DialogTurnStatus } = require('botbuilder-dialogs');
const { jwtDecode } = require('jwt-decode')


class StateManagementBot extends ActivityHandler {
    constructor(conversationState, userState, dialog) {
        super()

        this.conversationState = conversationState;
        this.userState = userState;
        this.conversationDataAccessor = this.conversationState.createProperty("ConversationData");
        this.userProfileAccessor = this.userState.createProperty("UserProfile");
        this.dialog = dialog;
        this.ssoEnabled = process.env.SSO_ENABLED || false
        if (this.ssoEnabled === "false") {
            this.ssoEnabled = false
        }
        this.ssoConfigName = process.env.SSO_CONFIG_NAME || "default"
        this.streaming = process.env.AZURE_OPENAI_STREAMING || false
    }

    async run(context) {
        await super.run(context);
        // Save any state changes. The load happened during the execution of the Dialog.
        await this.conversationState.saveChanges(context, false);
        await this.userState.saveChanges(context, false);
    }

    async handleLogin(turnContext) {
        if (!this.ssoEnabled) {
            return true;
        }
        if (turnContext.activity.text == 'logout') {
            await this.handleLogout(turnContext);
            return false;
        }

        const userProfileAccessor = this.userState.createProperty('UserProfile');
        const userProfile = await userProfileAccessor.get(turnContext, {});

        const userTokenClient = turnContext.turnState.get(turnContext.adapter.UserTokenClientKey);

        try {
            const userToken = await userTokenClient.getUserToken(turnContext.activity.from.id, this.ssoConfigName, turnContext.activity.channelId);
            const decodedToken = jwtDecode(userToken.token);
            userProfile.name = decodedToken.name;
            userProfile.unique_name = decodedToken.unique_name;
            userProfile.oid = decodedToken.oid;
            return true;
        } catch (error) {
            const dialogSet = new DialogSet(this.conversationState.createProperty("DialogState"));
            dialogSet.add(this.dialog);
            const dialogContext = await dialogSet.createContext(turnContext);
            const results = await dialogContext.continueDialog();
            if (results.status === DialogTurnStatus.empty) {
                await dialogContext.beginDialog(this.dialog.id);
            }
            return false;
        }
    }

    async handleLogout(turnContext) {
        const userTokenClient = turnContext.turnState.get(turnContext.adapter.UserTokenClientKey);
        await userTokenClient.signOutUser(turnContext.activity.from.id, this.ssoConfigName, turnContext.activity.channelId);
        await turnContext.sendActivity("Signed out");
    }

    async sendInterimMessage(
        turnContext,
        interimMessage,
        streamSequence,
        streamId,
        streamType
    )
    {
        var streamSupported = this.streaming && turnContext.activity.channelId == "directline";
        var updateSupported = this.streaming && turnContext.activity.channelId == "msteams";
        // If we can neither stream or update, return null
        if (streamType == "typing" && !streamSupported && !updateSupported)
        {
            return null;
        }
        // If we can update messages, do so
        if (updateSupported)
        {
            if (streamId == null)
            {
                var createActivity = await turnContext.sendActivity(interimMessage);
                return createActivity.id;
            }
            else
            {
                var updateMessage = {
                    text: interimMessage,
                    type: "message",
                    id: streamId
                };
                var updateActivity = await turnContext.updateActivity(updateMessage);
                return updateActivity.id;
            }
        }
        // If we can stream messages, do so
        var channelData = {
            "streamId": streamId,
            "streamSequence": streamSequence,
            "streamType": streamType == "typing" ? "streaming" : "final"
        };
        var message = {
            channel_data: streamSupported ? channelData : null,
            text: interimMessage,
            type: streamType
        };
        var activity = await turnContext.sendActivity(message);
        return activity.Id;
    }
}

module.exports.StateManagementBot = StateManagementBot;