# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

import os
from botbuilder.dialogs import (
    WaterfallDialog,
    WaterfallStepContext,
    DialogTurnResult,
)
from botbuilder.dialogs.prompts import OAuthPrompt, OAuthPromptSettings, ConfirmPrompt

from dialogs import LogoutDialog


class LoginDialog(LogoutDialog):
    def __init__(self, connection_name: str):
        super(LoginDialog, self).__init__(LoginDialog.__name__, connection_name)
        self.login_success_message = os.getenv("SSO_MESSAGE_SUCCESS", "Login success")
        self.login_failed_message = os.getenv("SSO_MESSAGE_FAILED", "Login failed")

        self.add_dialog(
            OAuthPrompt(
                OAuthPrompt.__name__,
                OAuthPromptSettings(
                    connection_name=connection_name,
                    text=os.getenv("SSO_MESSAGE_TITLE"),
                    title=os.getenv("SSO_MESSAGE_PROMPT"),
                    timeout=300000,
                ),
            )
        )

        self.add_dialog(
            WaterfallDialog(
                "WaterfallDialog",
                [
                    self.prompt_step,
                    self.login_step
                ],
            )
        )

        self.initial_dialog_id = "WaterfallDialog"

    async def prompt_step(self, step_context: WaterfallStepContext) -> DialogTurnResult:
        return await step_context.begin_dialog(OAuthPrompt.__name__)

    async def login_step(self, step_context: WaterfallStepContext) -> DialogTurnResult:
        # Get the token from the previous step. Note that we could also have gotten the
        # token directly from the prompt itself. There is an example of this in the next method.
        token_response = step_context.result
        if token_response:
            await step_context.context.send_activity(self.login_success_message)
            return await step_context.endDialog()

        await step_context.context.send_activity(self.login_success_message)
        return await step_context.end_dialog()