# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

import os
import io
import json
import base64
import urllib.request

from botbuilder.core import ConversationState, TurnContext, UserState, MessageFactory
from botbuilder.schema import ChannelAccount, CardAction, ActionTypes, Activity
from botbuilder.dialogs import Dialog

from openai import AzureOpenAI
from openai.types.beta.assistant_stream_event import ThreadMessageDelta, ThreadRunRequiresAction, ThreadRunCreated, ThreadRunFailed
from openai.types.beta.threads import TextDeltaBlock, ImageFileDeltaBlock

from data_models import ConversationData, Attachment, mime_type
from .state_management_bot import StateManagementBot

class AssistantBot(StateManagementBot):

    def __init__(self, conversation_state: ConversationState, user_state: UserState, aoai_client: AzureOpenAI, dialog: Dialog):
        super().__init__(conversation_state, user_state, dialog)
        self.aoai_client = aoai_client
        self.chat_client = aoai_client.chat
        self.file_client = aoai_client.files

        self.deployment = os.getenv("AZURE_OPENAI_DEPLOYMENT_NAME")
        self.instructions = os.getenv("LLM_INSTRUCTIONS")
        self.welcome_message = os.getenv("LLM_WELCOME_MESSAGE", "Hello and welcome to the Assistant Bot Python!")
        self.assistant_id = os.getenv("AZURE_OPENAI_ASSISTANT_ID")
        self.streaming = os.getenv("AZURE_OPENAI_STREAMING", False)

    async def on_members_added_activity(self, members_added: list[ChannelAccount], turn_context: TurnContext):
        for member in members_added:
            if member.id != turn_context.activity.recipient.id:
                await turn_context.send_activity(self.welcome_message)

    async def on_message_activity(self, turn_context: TurnContext):
        
        # Enforce login
        loggedIn = await self.handle_login(turn_context)
        if not loggedIn:
            return False
        
        # Load conversation state
        conversation_data = await self.conversation_data_accessor.get(turn_context, ConversationData([]))

        # Create a new thread if one does not exist
        if conversation_data.thread_id is None:
            thread = self.aoai_client.beta.threads.create()
            conversation_data.thread_id = thread.id

        # Delete thread if user asks
        if turn_context.activity.text == 'clear':
            self.aoai_client.beta.threads.delete(conversation_data.thread_id)
            conversation_data.thread_id = None
            conversation_data.attachments = []
            conversation_data.history = []
            await turn_context.send_activity('Conversation cleared!')
            return True
                
        # Check if this is a file upload and process it
        files_uploaded = await self.handle_file_uploads(turn_context, conversation_data.thread_id, conversation_data)
        
        #  Return early if there is no text in the message
        if (turn_context.activity.text == None):
            return
    
        # Check if this is a file upload follow up - user selects a file upload option
        if (turn_context.activity.text.startswith(":")):
            # Get upload metadata
            tool = turn_context.activity.text.split(':').pop().strip()
            # Get file from attachments
            attachment = conversation_data.attachments[-1]
            # Add file upload to relevant tool
            with urllib.request.urlopen(attachment.url) as f:
                bytes = io.BytesIO(f.read())
                bytes.name = attachment.name
            file_response = self.file_client.create(file=bytes, purpose="assistants")
            # Send the file to the assistant
            tools = []
            if tool == "Code Interpreter":
                tools.append({
                    "type": "code_interpreter"
                })
            if tool == "File Search":
                tools.append({
                    "type": "file_search"
                })
            self.aoai_client.beta.threads.messages.create(
                thread_id=conversation_data.thread_id,
                role="user",
                content=f"File uploaded: {attachment.name}",
                attachments=[{
                    "file_id": file_response.id,
                    "tools": tools
                }]
            )
            # Send feedback to user
            await turn_context.send_activity(f"File added to {tool} successfully!");
            return True

        # Add user message to history
        conversation_data.add_turn("user", turn_context.activity.text)
        
        # Send user message to thread
        self.aoai_client.beta.threads.messages.create(
            thread_id=conversation_data.thread_id, 
            role="user", 
            content=turn_context.activity.text
        )
        
        # Run thread
        run = self.aoai_client.beta.threads.runs.create(
            thread_id=conversation_data.thread_id,
            assistant_id=self.assistant_id,
            instructions=self.instructions,
            stream=True
        )

        # Process run streaming
        await self.process_run_streaming(run, conversation_data, turn_context)

        return True

    async def process_run_streaming(self, run, conversation_data, turn_context, stream_id = None):
        # Start streaming response
        current_message = ""
        tool_outputs = []
        current_run_id = ""
        activity_id = ""
        stream_sequence = 1
        activity_id = await self.send_interim_message(turn_context, "Typing...", stream_sequence, stream_id, "typing")

        for event in run:
            if type(event) == ThreadRunFailed:
                current_message = event.data.last_error.message
                break
            if type(event) == ThreadRunCreated:
                current_run_id = event.data.id
            if type(event) == ThreadRunRequiresAction:
                tool_calls = event.data.required_action.submit_tool_outputs.tool_calls
                for tool_call in tool_calls:
                    arguments = json.loads(tool_call.function.arguments)
                    if tool_call.function.name == "image_query":
                        response = await self.image_query(conversation_data, arguments["query"], arguments["image_name"])
                        tool_outputs.append({"tool_call_id": tool_call.id, "output": response})

            if type(event) == ThreadMessageDelta:
                deltaBlock = event.data.delta.content[0]
                if type(deltaBlock) == TextDeltaBlock:
                    current_message += deltaBlock.text.value
                    stream_sequence += 1
                    # Flush content every 50 messages
                    if (stream_sequence % 50 == 0):
                        await self.send_interim_message(turn_context, current_message, stream_sequence, activity_id, "typing")

                elif type(deltaBlock) == ImageFileDeltaBlock: 
                    current_message += f"![{deltaBlock.image_file.file_id}](/api/files/{deltaBlock.image_file.file_id})"
        
        # Recursively process the run with the tool outputs
        if len(tool_outputs) > 0:
            new_run = self.aoai_client.beta.threads.runs.submit_tool_outputs(thread_id=conversation_data.thread_id, run_id=current_run_id, tool_outputs=tool_outputs, stream=True)
            await self.process_run_streaming(new_run, conversation_data, turn_context, activity_id)
            return
        response = current_message

        # Add assistant message to history
        conversation_data.add_turn("assistant", response)

        # Respond back to user
        stream_sequence += 1
        await self.send_interim_message(turn_context, current_message, stream_sequence, activity_id, "message")
    

    # Helper to handle file uploads from user
    async def handle_file_uploads(self, turn_context: TurnContext, thread_id: str, conversation_data: ConversationData):
        files_uploaded = False
        # Check if incoming message has attached files
        if turn_context.activity.attachments is not None:
            for attachment in turn_context.activity.attachments:
                if attachment.content_url == None:
                    continue
                files_uploaded = True
                download_url = attachment.content_url
                if attachment.content and "downloadUrl" in attachment.content:
                    download_url = attachment.content["downloadUrl"]
                # Add file to attachments in case we need to reference it in Function Calling
                conversation_data.attachments.append(Attachment(
                    name = attachment.name,
                    content_type = mime_type(attachment.name),
                    url = download_url
                ))

                # Add file upload notice to conversation history, frontend, and assistant
                conversation_data.add_turn("user", f"File uploaded: {attachment.name}")
                await turn_context.send_activity(f"File uploaded: {attachment.name}")
                self.aoai_client.beta.threads.messages.create(thread_id=thread_id,role="user",content=f"File uploaded: {attachment.name}",)
                # Ask whether to add file to a tool
                await turn_context.send_activity(MessageFactory.suggested_actions(
                    [
                        CardAction(title= ":Code Interpreter", type= ActionTypes.im_back, value= ":Code Interpreter"),
                        CardAction( title= ":File Search", type= ActionTypes.im_back, value= ":File Search"),
                    ],
                    "Add to a tool? (ignore if not needed)",
                ))


        # Return True if files were uploaded
        return files_uploaded
    
    async def image_query(self, conversation_data: ConversationData, query: str, image_name: str):
        # Find image in attachments by name
        image = next(filter(lambda a: a.name == image_name.split("/")[-1], conversation_data.attachments))
        # Handle image not found

        # Read image.url
        with urllib.request.urlopen(image.url) as f:
            # get file as base64
            bytes = base64.b64encode(f.read()).decode()

        # Send image to assistant
        response = self.chat_client.completions.create(
            model=self.deployment,
            messages=[
                {"role": "user", "content": [
                    {"type": "text", "text": query},
                    {"type": "image_url", "image_url": {
                        "url": f"data:{image.content_type};base64,{bytes}"}
                    }
                ]}
            ]
        )
        return response.choices[0].message.content

