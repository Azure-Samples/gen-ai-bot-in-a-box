# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

import os
import sys
import traceback
from http import HTTPStatus

from azure.identity import DefaultAzureCredential, get_bearer_token_provider

from aiohttp import web
from aiohttp.web import Request, Response, json_response
from botbuilder.azure import (
    CosmosDbPartitionedStorage,
    CosmosDbPartitionedConfig,
)
from botbuilder.core import (
    ConversationState,
    MemoryStorage,
    TurnContext,
    UserState,
)
from botbuilder.core.integration import aiohttp_error_middleware
from botbuilder.integration.aiohttp import CloudAdapter, ConfigurationBotFrameworkAuthentication
from botbuilder.schema import Activity, ActivityTypes

from openai import AzureOpenAI
from services import Phi
from dotenv import load_dotenv

from bots import AssistantBot, ChatCompletionBot, PhiBot, SemanticKernelBot
from config import DefaultConfig


load_dotenv()
CONFIG = DefaultConfig()

# Create adapter.
# See https://aka.ms/about-bot-adapter to learn more about how bots work.
ADAPTER = CloudAdapter(ConfigurationBotFrameworkAuthentication(CONFIG))

# Catch-all for errors.
async def on_error(context: TurnContext, error: Exception):
    # This check writes out errors to console log .vs. app insights.
    # NOTE: In production environment, you should consider logging this to Azure
    #       application insights.
    print(f"\n [on_turn_error] unhandled error: {error}", file=sys.stderr)
    traceback.print_exc()

    # Send a message to the user
    await context.send_activity("The bot encountered an error or bug.")
    await context.send_activity(
        "To continue to run this bot, please fix the bot source code."
    )
    # Send a trace activity if we're talking to the Bot Framework Emulator
    if CONFIG.DEBUG:
        # Create a trace activity that contains the error object
        await context.send_activity(str(error))
        # Send a trace activity, which will be displayed in Bot Framework Emulator

    # Clear out state
    await CONVERSATION_STATE.delete(context)


# Set the error handler on the Adapter.
# In this case, we want an unbound method, so MethodType is not needed.
ADAPTER.on_turn_error = on_error

# Create MemoryStorage and state

if CONFIG.COSMOSDB_ENDPOINT:
    MEMORY = CosmosDbPartitionedStorage(
        CosmosDbPartitionedConfig(
            cosmos_db_endpoint=CONFIG.COSMOSDB_ENDPOINT,
            # auth_key=CONFIG.COSMOSDB_KEY,
            database_id=CONFIG.COSMOSDB_DATABASE_ID,
            container_id=CONFIG.COSMOSDB_CONTAINER_ID,
        )
    )
else:
    MEMORY = MemoryStorage()
USER_STATE = UserState(MEMORY)
CONVERSATION_STATE = ConversationState(MEMORY)

# Dependency Injection
AOAI_CLIENT = AzureOpenAI(
    api_version=os.getenv("AZURE_OPENAI_API_VERSION"),
    azure_endpoint=os.getenv("AZURE_OPENAI_API_ENDPOINT"),
    azure_ad_token_provider=get_bearer_token_provider(
        DefaultAzureCredential(), 
        "https://cognitiveservices.azure.com/.default"
    )
)

# Create Bot
GEN_AI_IMPLEMENTATION = os.getenv("GEN_AI_IMPLEMENTATION")
if GEN_AI_IMPLEMENTATION == "chat-completions":
    BOT = ChatCompletionBot(CONVERSATION_STATE, USER_STATE, AOAI_CLIENT)
elif GEN_AI_IMPLEMENTATION == "assistant":
    BOT = AssistantBot(CONVERSATION_STATE, USER_STATE, AOAI_CLIENT)
elif GEN_AI_IMPLEMENTATION == "semantic-kernel":
    BOT = SemanticKernelBot(CONVERSATION_STATE, USER_STATE, AOAI_CLIENT)
elif GEN_AI_IMPLEMENTATION == "langchain":
    raise ValueError("Langchain is not supported in this version.")
elif GEN_AI_IMPLEMENTATION == "phi":
    phi_client = Phi(deployment_endpoint=os.getenv("AZURE_AI_PHI_DEPLOYMENT_ENDPOINT"), deployment_key=os.getenv("AZURE_AI_PHI_DEPLOYMENT_KEY"))
    BOT = PhiBot(CONVERSATION_STATE, USER_STATE, phi_client)
else:
    raise ValueError("Invalid engine type")

# Listen for incoming requests on /api/messages.
async def messages(req: Request) -> Response:
    # Main bot message handler.
    if "application/json" in req.headers["Content-Type"]:
        body = await req.json()
    else:
        return Response(status=HTTPStatus.UNSUPPORTED_MEDIA_TYPE)

    activity = Activity().deserialize(body)
    auth_header = req.headers["Authorization"] if "Authorization" in req.headers else ""

    response = await ADAPTER.process_activity(auth_header, activity, BOT.on_turn)
    if response:
        return json_response(data=response.body, status=response.status)
    return Response(status=HTTPStatus.OK)


app = web.Application(middlewares=[aiohttp_error_middleware])
app.router.add_post("/api/messages", messages)

if __name__ == "__main__":
    try:
        web.run_app(app, port=CONFIG.PORT)
    except Exception as error:
        raise error