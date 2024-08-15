# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

import os
from botbuilder.core import ConversationState, TurnContext, UserState
from botbuilder.schema import ChannelAccount

from data_models import ConversationData
from ..data_models.conversation_data import ConversationTurn
from .state_management_bot import StateManagementBot

class TemplateBot(StateManagementBot):

    def __init__(self, conversation_state: ConversationState, user_state: UserState):
        super().__init__(conversation_state, user_state)
        self._instructions = os.getenv("LLM_INSTRUCTIONS")
        self._welcomeMessage = os.getenv("LLM_WELCOME_MESSAGE", "Hello and welcome to the Template Bot Python!")
        # Inject dependencies here

    # Modify onMembersAdded as needed
    async def on_members_added_activity(self, members_added: list[ChannelAccount], turn_context: TurnContext):
        for member in members_added:
            if member.id != turn_context.activity.recipient.id:
                await turn_context.send_activity(self._welcomeMessage)

    async def on_message_activity(self, turn_context: TurnContext):
        # Load conversation state
        conversation_data = await self.conversation_data_accessor.get(turn_context, ConversationData([
            ConversationTurn("system", self._instructions)
        ]))

        # Catch any special messages here

        # Add user message to history
        conversation_data.add_turn("user", turn_context.activity.text)
        
        # Run logic to obtain response here
        response = "Hello, world!"

        # Add assistant message to history
        conversation_data.add_turn("assistant", response)

        # Respond back to user
        return await turn_context.send_activity(response)
