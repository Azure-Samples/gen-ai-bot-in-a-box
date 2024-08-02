// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

const path = require('path');

const dotenv = require('dotenv');
// Import required bot configuration.
const ENV_FILE = path.join(__dirname, '.env');
dotenv.config({ path: ENV_FILE });

const restify = require('restify');


// Import required bot services.
// See https://aka.ms/bot-services to learn more about the different parts of a bot.
const {
    CloudAdapter,
    ConfigurationBotFrameworkAuthentication,
    MemoryStorage,
    ConversationState,
    UserState
} = require('botbuilder');

const { AzureOpenAI } = require('openai')
const { Phi } = require('./services/phi')


// This bot's main dialog.
const { AssistantBot } = require('./bots/assistant_bot');
const { ChatCompletionBot } = require('./bots/chat_completion_bot');
const { PhiBot } = require('./bots/phi_bot');
const { CosmosDbPartitionedStorage } = require('botbuilder-azure');

require('dotenv').config()

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

    // Send a more detailed message to the user in debug mode
    if (process.env.DEBUG === 'true') {
        await context.sendActivity(error);
    }

};

// Set the onTurnError for the singleton CloudAdapter.
adapter.onTurnError = onTurnErrorHandler;


// Dependency Injection
const aoaiClient = new AzureOpenAI({
    baseURL: process.env.AZURE_OPENAI_API_ENDPOINT,
    apiKey: process.env.AZURE_OPENAI_API_KEY,
    apiVersion: process.env.AZURE_OPENAI_API_VERSION,
});


// Define state store for your bot.

let storage
if (process.env.AZURE_COSMOSDB_ENDPOINT) {
    storage = new CosmosDbPartitionedStorage({
        cosmosDbEndpoint: process.env.AZURE_COSMOSDB_ENDPOINT,
        authKey: process.env.AZURE_COSMOSDB_KEY,
        databaseId: process.env.AZURE_COSMOSDB_DATABASE_ID,
        containerId: process.env.AZURE_COSMOSDB_CONTAINER_ID,
    });
} else {
    storage = new MemoryStorage();
}

// Create conversation and user state with in-memory storage provider.
const conversationState = new ConversationState(storage);
const userState = new UserState(storage);

// Create the bot.
let bot
const ENGINE = "ASSISTANT"
if (ENGINE == "CHAT_COMPLETIONS") {
    bot = new ChatCompletionBot(conversationState, userState, aoaiClient)
}
else if (ENGINE == "ASSISTANT") {
    bot = new AssistantBot(conversationState, userState, aoaiClient)
}
else if (ENGINE == "PHI") {
    phi_client = new Phi(process.env.AZURE_AI_PHI_DEPLOYMENT_ENDPOINT, process.env.AZURE_AI_PHI_DEPLOYMENT_KEY)
    bot = new PhiBot(conversationState, userState, phi_client)
}
else {
    throw "Invalid engine type"
}

// Listen for incoming requests.
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

// Error handler middleware
server.on('InternalServer', function(req, res, err, callback) {
    // before the response is sent, this listener will be invoked, allowing
    // opportunities to do metrics capturing or logging.
    myMetrics.capture(err);
    // invoke the callback to complete your work, and the server will send out
    // a response.
    return callback();
});