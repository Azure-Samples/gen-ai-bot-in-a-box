# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.
import os
import jwt
from botbuilder.core import ActivityHandler, ConversationState, TurnContext, UserState, MessageFactory
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
        print(self.sso_enabled)
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

    async def send_interim_message(
        self,
        turn_context,
        interim_message,
        stream_sequence,
        stream_id,
        stream_type
    ):
        stream_supported = self.streaming and turn_context.activity.channel_id == "directline"
        update_supported = self.streaming and turn_context.activity.channel_id == "msteams"
        # If we can neither stream or update, return null
        if stream_type == "typing" and not stream_supported and not update_supported:
            return None
        # If we can update messages, do so
        if update_supported:
            if stream_id == None:
                create_activity = await turn_context.send_activity(interim_message)
                return create_activity.id
            else:
                update_message = MessageFactory.text(interim_message)
                update_message.id = stream_id
                update_message.type = "message"
                update_activity = await turn_context.update_activity(update_message)
                return update_activity.id
        # If we can stream messages, do so
        channel_data = {
            "streamId": stream_id,
            "streamSequence": stream_sequence,
            "streamType": "streaming" if stream_type == "typing" else "final"
        }
        message = MessageFactory.text(interim_message)
        message.channel_data = channel_data if stream_supported else None
        message.type = stream_type
        activity = await turn_context.send_activity(message)
        return activity.id