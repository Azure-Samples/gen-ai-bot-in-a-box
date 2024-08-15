// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

const { ComponentDialog } = require('botbuilder-dialogs');
const { OAuthPrompt, WaterfallDialog } = require('botbuilder-dialogs');

class LoginDialog extends ComponentDialog {
    constructor() {
        super("LoginDialog");
        this.loginSuccessMessage = process.env.SSO_MESSAGE_SUCCESS || "Login success";
        this.loginFailedMessage = process.env.SSO_MESSAGE_FAILED || "Login failed";
            
        this.addDialog(new OAuthPrompt(
            "OAuthPrompt",
            {
                connectionName: process.env.SSO_CONFIG_NAME,
                text: process.env.SSO_MESSAGE_TITLE,
                title: process.env.SSO_MESSAGE_PROMPT,
                timeout: 300000, // User has 5 minutes to login (1000 * 60 * 5)
            }));

        this.addDialog(new WaterfallDialog("WaterfallDialog", [
            this.promptStepAsync,
            this.loginStepAsync
        ]));

        this.initialDialogId = "WaterfallDialog";
    }

    async promptStepAsync(stepContext) {
        return await stepContext.beginDialog("OAuthPrompt");
    }

    async loginStepAsync(stepContext) {
        // Get the token from the previous step.
        var tokenResponse = stepContext.result;
        if (tokenResponse != null)
        {
            await stepContext.Context.SendActivityAsync(this.loginSuccessMessage);
            return await stepContext.endDialog();
        }

        await stepContext.Context.SendActivityAsync(this.loginFailedMessage);
        return await stepContext.endDialog();
    }
}

module.exports.LoginDialog = LoginDialog;