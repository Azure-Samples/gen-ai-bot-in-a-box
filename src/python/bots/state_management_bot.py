# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.
import os
import jwt
from botbuilder.core import ActivityHandler, ConversationState, TurnContext, UserState
from botbuilder.dialogs import Dialog, DialogSet, DialogTurnStatus
from botframework.connector.auth.user_token_client import UserTokenClient


class StateManagementBot(ActivityHandler):
    def __init__(self, conversation_state: ConversationState, user_state: UserState, dialog: Dialog):
        self.conversation_state = conversation_state
        self.user_state = user_state
        self.conversation_data_accessor = self.conversation_state.create_property("ConversationData")
        self.user_profile_accessor = self.user_state.create_property("UserProfile")
        self.dialog = dialog
        self.sso_enabled = os.getenv("SSO_ENABLED", False)
        if (self.sso_enabled == "false"):
            self.sso_enabled = False
        self.sso_config_name = os.getenv("SSO_CONFIG_NAME", "default")


    async def on_turn(self, turn_context: TurnContext):
        await super().on_turn(turn_context)
        # Save any state changes. The load happened during the execution of the Dialog.
        await self.conversation_state.save_changes(turn_context)
        await self.user_state.save_changes(turn_context)
    
    async def handle_login(self, turn_context: TurnContext):
        if not self.sso_enabled:
            return True
        if turn_context.activity.text == 'logout':
            await self.handle_logout(turn_context)
            return False

        user_profile_accessor = self.user_state.create_property("UserProfile")
        user_profile = await user_profile_accessor.get(turn_context, lambda: {})

        user_token_client = turn_context.turn_state.get(UserTokenClient.__name__, None)

        try:
            user_token = await user_token_client.get_user_token(turn_context.activity.from_property.id, self.sso_config_name, turn_context.activity.channel_id, None)
            decoded_token = jwt.decode(user_token.token, options={"verify_signature": False})
            user_profile["name"] = decoded_token.get("name")
            return True
        except Exception as error:
            dialog_set = DialogSet(self.conversation_state.create_property("DialogState"))
            dialog_set.add(self.dialog)
            dialog_context = await dialog_set.create_context(turn_context)
            results = await dialog_context.continue_dialog()
            if results.status == DialogTurnStatus.Empty:
                await dialog_context.begin_dialog(self.dialog.id)
            return False

    async def handle_logout(self, turn_context):
        user_token_client = turn_context.turn_state.get(UserTokenClient.__name__, None)
        await user_token_client.sign_out_user(turn_context.activity.from_property.id, self.sso_config_name, turn_context.activity.channel_id)
        await turn_context.send_activity("Signed out")