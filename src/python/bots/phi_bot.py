# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

import os

from botbuilder.core import ConversationState, TurnContext, UserState
from botbuilder.schema import ChannelAccount
from botbuilder.dialogs import Dialog

from services import Phi
from data_models import ConversationData
from .state_management_bot import StateManagementBot

class PhiBot(StateManagementBot):

    def __init__(self, conversation_state: ConversationState, user_state: UserState, phi_client: Phi, dialog: Dialog):
        super().__init__(conversation_state, user_state, dialog)
        self._phi_client = phi_client
        self.welcome_message = os.getenv("LLM_WELCOME_MESSAGE", "Hello and welcome to the Phi Bot Python!")

    async def on_members_added_activity(self, members_added: list[ChannelAccount], turn_context: TurnContext):
        for member in members_added:
            if member.id != turn_context.activity.recipient.id:
                await turn_context.send_activity(self.welcome_message)

    async def on_message_activity(self, turn_context: TurnContext):
        # Load conversation state
        conversation_data = await self.conversation_data_accessor.get(turn_context, ConversationData([]))

        # Add user message to history
        conversation_data.add_turn("user", turn_context.activity.text)
        
        # Run logic to obtain response
        completion = self._phi_client.create_completion(
            messages=conversation_data.toMessages()
        )
        response = completion["choices"][0]["message"]["content"]

        # Add assistant message to history
        conversation_data.add_turn("assistant", response)

        # Respond back to user
        return await turn_context.send_activity(response)
