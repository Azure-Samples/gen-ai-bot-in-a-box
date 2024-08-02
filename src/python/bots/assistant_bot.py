# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

import os
from botbuilder.core import ConversationState, TurnContext, UserState
from botbuilder.schema import ChannelAccount
from openai import AzureOpenAI
from openai.types.beta.assistant_stream_event import ThreadMessageDelta
from openai.types.beta.threads import TextDeltaBlock, ImageFileDeltaBlock

from data_models import ConversationData
from .state_management_bot import StateManagementBot

class AssistantBot(StateManagementBot):

    def __init__(self, conversation_state: ConversationState, user_state: UserState, aoai_client: AzureOpenAI):
        super().__init__(conversation_state, user_state)
        self._aoai_client = aoai_client

    async def on_members_added_activity(
        self, members_added: list[ChannelAccount], turn_context: TurnContext
    ):
        for member in members_added:
            if member.id != turn_context.activity.recipient.id:
                await turn_context.send_activity("Hello and welcome to the Assistant Bot!")

    async def on_message_activity(self, turn_context: TurnContext):
        conversation_data = await self.conversation_data_accessor.get(turn_context, ConversationData([]))

        conversation_data.add_turn("user", turn_context.activity.text)

        if conversation_data.thread_id is None:
            thread = self._aoai_client.beta.threads.create()
            conversation_data.thread_id = thread.id

        self._aoai_client.beta.threads.messages.create(thread_id=conversation_data.thread_id, role="user", content=turn_context.activity.text)
        
        run = self._aoai_client.beta.threads.runs.create(
            thread_id=conversation_data.thread_id,
            assistant_id=os.getenv("AZURE_OPENAI_ASSISTANT_ID"),
            instructions=os.getenv("LLM_INSTRUCTIONS"),
            stream=True
        )

        current_message = ""

        for event in run:
            if type(event) == ThreadMessageDelta:
                deltaBlock = event.data.delta.content[0]
                if type(deltaBlock) == TextDeltaBlock:
                    current_message += deltaBlock.text.value
                elif type(deltaBlock) == ImageFileDeltaBlock: 
                    current_message += f"![{deltaBlock.image_file.file_id}](/api/files/{deltaBlock.image_file.file_id})"
            # else:
            #     print(event)

        conversation_data.add_turn("assistant", current_message)

        return await turn_context.send_activity(current_message)
