// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

const path = require('path');
const crypto = require('crypto');
const dotenv = require('dotenv');
const restify = require('restify');
const { DefaultAzureCredential } = require('@azure/identity')
const { AzureOpenAI } = require('openai')

const ENV_FILE = path.join(__dirname, '.env');
dotenv.config({ path: ENV_FILE });
require('dotenv').config()


// Create HTTP server
const server = restify.createServer();
server.use(restify.plugins.bodyParser());

server.listen(process.env.port || process.env.PORT || 3000, () => {
    console.log(`\n${server.name} listening to ${server.url}`);
});

// Set up service authentication
const credential = new DefaultAzureCredential({
    managedIdentityClientId: process.env.MicrosoftAppId,
})

// Azure AI Services
const aoaiClient = new AzureOpenAI({
    baseURL: process.env.AZURE_OPENAI_API_ENDPOINT,
    azureADTokenProvider: () => credential.getToken('https://cognitiveservices.azure.com/.default').then(result => result.token),
    apiVersion: process.env.AZURE_OPENAI_API_VERSION,
});

// In NodeJS:
server.get('/api/files/:fileId', async (req, res) => {
    try {
        const fileId = req.params.fileId;
        // Your code here to handle the fileId parameter
        const response = await aoaiClient.files.content(req.body.message, req.body.context);
        res.send(200, response.data);
    } catch (error) {
        res.send(500, error);
    }
});
server.get('/api/directline/token', async (req, res) => {
    try {
        // In dotnet we do this to generate an id
        const userId = `dl_${crypto.randomBytes(16).toString('hex')}`;
        let tokenResponse = await fetch("https://directline.botframework.com/v3/directline/tokens/generate", {
            method: "POST",
            headers: {
                "Authorization": `Bearer ${process.env.DIRECT_LINE_SECRET}`,
                "Content-type": "application/json"
            },
            body: {
                User: { Id: userId }
            },
        })
        res.send(tokenResponse.status, await tokenResponse.json());
    } catch (error) {
        console.log(error)
        res.send(500, error);
    }
});
server.get('/api/speech/token', async (req, res) => {
    try {
        let tokenResponse = await credential.getToken('https://cognitiveservices.azure.com/.default')
            .then(result => ({
                token: `aad#${process.env.AZURE_SPEECH_RESOURCE_ID}#${result.token}`,
                region: process.env.AZURE_SPEECH_REGION
            }))
        res.send(200, tokenResponse);
    } catch (error) {
        console.log(error)
        res.send(500, error);
    }

});

// Home page
server.get('/', restify.plugins.serveStatic({
    directory: path.join(__dirname, 'public'),
    file: 'index.html'
}));