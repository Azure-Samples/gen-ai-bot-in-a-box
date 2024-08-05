// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

const path = require('path');
const dotenv = require('dotenv');
const restify = require('restify');
const { DefaultAzureCredential } = require('@azure/identity')
const {
    CloudAdapter,
    ConfigurationBotFrameworkAuthentication,
    MemoryStorage,
    ConversationState,
    UserState
} = require('botbuilder');

const { AzureOpenAI } = require('openai')
const { Phi } = require('./services/phi')

const ENV_FILE = path.join(__dirname, '.env');
dotenv.config({ path: ENV_FILE });
require('dotenv').config()

const { AssistantBot } = require('./bots/assistant_bot');
const { ChatCompletionBot } = require('./bots/chat_completion_bot');
const { PhiBot } = require('./bots/phi_bot');
const { CosmosDbPartitionedStorage } = require('botbuilder-azure');


// Create HTTP server
const server = restify.createServer();
server.use(restify.plugins.bodyParser());

server.listen(process.env.port || process.env.PORT || 3978, () => {
    console.log(`\n${server.name} listening to ${server.url}`);
    console.log('\nGet Bot Framework Emulator: https://aka.ms/botframework-emulator');
    console.log('\nTo talk to your bot, open the emulator select "Open Bot"');
});

const botFrameworkAuthentication = new ConfigurationBotFrameworkAuthentication(process.env);

// Create adapter.
// See https://aka.ms/about-bot-adapter to learn more about how bots work.
const adapter = new CloudAdapter(botFrameworkAuthentication);

// Catch-all for errors.
const onTurnErrorHandler = async (context, error) => {
    // This check writes out errors to console log .vs. app insights.
    // NOTE: In production environment, you should consider logging this to Azure
    //       application insights. See https://aka.ms/bottelemetry for telemetry
    //       configuration instructions.
    console.error(`\n [onTurnError] unhandled error: ${error}`);


    // Send a message to the user
    await context.sendActivity('The bot encountered an error or bug.');
    await context.sendActivity('To continue to run this bot, please fix the bot source code.');
    await context.sendActivity(error);

};

// Set the error handler on the Adapter.
adapter.onTurnError = onTurnErrorHandler;

// Set up service authentication
const credential = new DefaultAzureCredential()

// Azure AI Services
const aoaiClient = new AzureOpenAI({
    baseURL: process.env.AZURE_OPENAI_API_ENDPOINT,
    azureADTokenProvider: () => credential.getToken('https://cognitiveservices.azure.com/.default').then(result => result.token),
    apiVersion: process.env.AZURE_OPENAI_API_VERSION,
});

// Conversation history storage
let storage
if (process.env.AZURE_COSMOSDB_ENDPOINT) {
    storage = new CosmosDbPartitionedStorage({
        cosmosDbEndpoint: process.env.AZURE_COSMOSDB_ENDPOINT,
        tokenCredential: credential,
        databaseId: process.env.AZURE_COSMOSDB_DATABASE_ID,
        containerId: process.env.AZURE_COSMOSDB_CONTAINER_ID,
    });
} else {
    storage = new MemoryStorage();
}

// Create conversation and user state
const conversationState = new ConversationState(storage);
const userState = new UserState(storage);

// Create the bot.
let bot
const engine = process.env.GEN_AI_IMPLEMENTATION
if (engine == "chat-completions") {
    bot = new ChatCompletionBot(conversationState, userState, aoaiClient)
}
else if (engine == "assistant") {
    bot = new AssistantBot(conversationState, userState, aoaiClient)
}
else if (engine == "semantic-kernel") {
    throw new Error("Semantic Kernel is not supported in NodeJS.")
}
else if (engine == "PHI") {
    phi_client = new Phi(process.env.AZURE_AI_PHI_DEPLOYMENT_ENDPOINT, process.env.AZURE_AI_PHI_DEPLOYMENT_KEY)
    bot = new PhiBot(conversationState, userState, phi_client)
}
else {
    throw "Invalid engine type"
}

// Listen for incoming requests on /api/messages.
server.post('/api/messages', async (req, res) => {
    // Route received a request to adapter for processing
    await adapter.process(req, res, (context) => bot.run(context));
});

// Listen for Upgrade requests for Streaming.
server.on('upgrade', async (req, socket, head) => {
    // Create an adapter scoped to this WebSocket connection to allow storing session data.
    const streamingAdapter = new CloudAdapter(botFrameworkAuthentication);
    // Set onTurnError for the CloudAdapter created for each connection.
    streamingAdapter.onTurnError = onTurnErrorHandler;

    await streamingAdapter.process(req, socket, head, (context) => bot.run(context));
});