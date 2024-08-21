# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

import os
import requests

from botbuilder.core import ConversationState, TurnContext, UserState
from botbuilder.schema import ChannelAccount
from botbuilder.dialogs import Dialog

from openai import AzureOpenAI
from openai.types.beta.assistant_stream_event import ThreadMessageDelta
from openai.types.beta.threads import TextDeltaBlock, ImageFileDeltaBlock

from data_models import ConversationData
from .state_management_bot import StateManagementBot

class AssistantBot(StateManagementBot):

    def __init__(self, conversation_state: ConversationState, user_state: UserState, aoai_client: AzureOpenAI, dialog: Dialog):
        super().__init__(conversation_state, user_state, dialog)
        self.aoai_client = aoai_client

    async def on_members_added_activity(self, members_added: list[ChannelAccount], turn_context: TurnContext):
        for member in members_added:
            if member.id != turn_context.activity.recipient.id:
                await turn_context.send_activity("Hello and welcome to the Assistant Bot Python!")

    async def on_message_activity(self, turn_context: TurnContext):
        # Load conversation state
        conversation_data = await self.conversation_data_accessor.get(turn_context, ConversationData([]))

        # Create a new thread if one does not exist
        if conversation_data.thread_id is None:
            thread = self._aoai_client.beta.threads.create()
            conversation_data.thread_id = thread.id

        # # Check if this is a file upload and process it
        # filesUploaded = await self.handle_file_uploads(turn_context, conversation_data.thread_id);
        # # Return early if it is
        # if (filesUploaded):
        #     return

        # Add user message to history
        conversation_data.add_turn("user", turn_context.activity.text)
        
        # Run logic to obtain response
        # Send user message to thread
        self._aoai_client.beta.threads.messages.create(
            thread_id=conversation_data.thread_id, 
            role="user", 
            content=turn_context.activity.text
        )
        
        # Run thread
        run = self._aoai_client.beta.threads.runs.create(
            thread_id=conversation_data.thread_id,
            assistant_id=os.getenv("AZURE_OPENAI_ASSISTANT_ID"),
            instructions=os.getenv("LLM_INSTRUCTIONS"),
            stream=True
        )

        # Start streaming response
        current_message = ""
        for event in run:
            if type(event) == ThreadMessageDelta:
                deltaBlock = event.data.delta.content[0]
                if type(deltaBlock) == TextDeltaBlock:
                    current_message += deltaBlock.text.value
                elif type(deltaBlock) == ImageFileDeltaBlock: 
                    current_message += f"![{deltaBlock.image_file.file_id}](/api/files/{deltaBlock.image_file.file_id})"
        
        response = current_message

        # Add assistant message to history
        conversation_data.add_turn("assistant", response)

        # Respond back to user
        return await turn_context.send_activity(response)

    # Helper to handle file uploads from user
    async def handle_file_uploads(self, turn_context: TurnContext, thread_id: str):
        filesUploaded = False
        # Check if incoming message has attached files
        if turn_context.activity.attachments is not None:
            filesUploaded = True
            for attachment in turn_context.activity.attachments:
                # Images uploads are not supported yet
                if "image" in attachment.content_type:
                    await turn_context.send_activity("Image uploads are not supported yet.")
                    continue

                # Download file and upload to Azure OpenAI Files API
                file = requests.get(attachment.content_url).content
                file_response = self._aoai_client.files.create(file=file, purpose="assistants")

                # Send message to thread with file attachment
                self._aoai_client.beta.threads.messages.create(
                    thread_id=thread_id,
                    role="user",
                    content=f"[{attachment.name}]",
                    attachments=[{
                        "file_id": file_response.id,
                        "tools": [{
                            "type": "code_interpreter"
                        }]
                    }]
                )

                # Send confirmation of file upload
                await turn_context.send_activity(f"File {attachment.name} uploaded successfully.")

        # Return True if files were uploaded
        return filesUploaded

