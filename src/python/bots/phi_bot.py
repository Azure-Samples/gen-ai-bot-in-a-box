# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

import os
from botbuilder.core import ConversationState, TurnContext, UserState
from botbuilder.schema import ChannelAccount

from services import Phi
from data_models import ConversationData, ConversationTurn
from .state_management_bot import StateManagementBot

class PhiBot(StateManagementBot):

    def __init__(self, conversation_state: ConversationState, user_state: UserState, phi_client: Phi):
        super().__init__(conversation_state, user_state)
        self._phi_client = phi_client

    async def on_members_added_activity(
        self, members_added: list[ChannelAccount], turn_context: TurnContext
    ):
        for member in members_added:
            if member.id != turn_context.activity.recipient.id:
                await turn_context.send_activity("Hello and welcome to the Phi Bot!")

    async def on_message_activity(self, turn_context: TurnContext):
        # Get the state properties from the turn context.
        conversation_data = await self.conversation_data_accessor.get(
            turn_context, ConversationData([ConversationTurn(role="system", content=os.getenv("LLM_INSTRUCTIONS"))])
        )
        
        conversation_data.add_turn("user", turn_context.activity.text)

        completion = self._phi_client.create_completion(
            messages=conversation_data.toMessages()
        )

        response = completion["choices"][0]["message"]["content"]

        conversation_data.add_turn("assistant", response)

        return await turn_context.send_activity(response)
