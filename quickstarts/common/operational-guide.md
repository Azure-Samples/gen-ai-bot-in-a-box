
## Operational Guide

### Project structure

| Directory | Contents |
| --- | --- |
| .azdo | Azure Devops CI/CD resources |
| .github | Github Actions CI/CD resources |
| assistants_tools | JSON files describing available Assistant Plugins |
| infra | Bicep templates for required Azure resources |
| media | Readme images and resource |
| quickstarts | Industry-specific quickstart instructions (including current document) |
| scripts | Post-deployment configuration scripts |
| src | Source code for applications |
| src/python | Python version of the application |
| src/node | NodeJS version of the application |
| src/dotnet | Dotnet version of the application |
| src/webchat | NodeJS application that hosts simple webchat app (frontend) |

### Customizing bot behavior

The bot application is provided in three programming languages for the convenience of development teams. The functionality is largely the same, with minor adaptations. Each of the implementations exposes the same API interface, defined by the Bot Framework SDK 4.0.

Refer to the [Bot Framework SDK documentation](https://learn.microsoft.com/en-us/azure/bot-service/index-bf-sdk?view=azure-bot-service-4.0) for details of how these applications work.

> Note: you will not be able to integrate with this application directly. It uses the Bot Framework authentication and as such will only accept connections coming from the deployed Azure Bot Service.

Bot backend environment variables:

| Variable | Description | Default |
| --- | --- | --- |
| AZURE_COSMOSDB_CONTAINER_ID | Cosmos DB container that will store conversation data | "Conversations" |
| AZURE_COSMOSDB_DATABASE_ID | Cosmos DB database | "GenAIBot" |
| AZURE_COSMOSDB_ENDPOINT | Cosmos DB endpoint | "https://cosmos-macarbot-i3m.documents.azure.com:443/" |
| AZURE_OPENAI_API_ENDPOINT | Azure Open AI endpoint | "https://cog-macarbot-i3m.cognitiveservices.azure.com/" |
| AZURE_OPENAI_API_VERSION | Azure Open AI API version | "2024-05-01-preview" |
| AZURE_OPENAI_ASSISTANT_ID | Azure Open AI Assistant ID | Automatically generated |
| AZURE_OPENAI_DEPLOYMENT_NAME | Azure Open AI model deployment name | "gpt-4o" |
| AZURE_OPENAI_STREAMING | Whether to enable streaming responses on the web chat | "true" |
| DEBUG | Debug mode. If true, chat will display error messages on the UI | "true" |
| GEN_AI_IMPLEMENTATION | Generative AI engine. Should be left as "assistant" | "assistant" |
| LLM_INSTRUCTIONS | System message for the AI Assistant. | "Answer the questions as accurately as possible using the provided functions." |
| LLM_WELCOME_MESSAGE | First message that is displayed when user joins the chat | "Hello and welcome!" |
| MAX_TURNS | Number of conversation turns persisted | "10" |
| MicrosoftAppId | Client ID od the Bot Service (only one allowed to communicate with this app) | Same as the created Managed Identity's ID |
| MicrosoftAppTenantId | Tenant ID of the Bot Service | Same as current tenant ID |
| MicrosoftAppType | App type of the Bot Service | "UserAssignedMSI" |


### Customizing Web Chat

The front-end application, contained in the [src/webchat](../..//src/webchat) is a NodeJS Restify app, with the following endpoints:

- **GET /api/directline/token**: Generates temporary Direct Line token, which allows users to start and interact with a conversation.
- **GET /api/speech/token**: Generates temporary Speech token, which allows users to leverage speech capabilities.
- **GET /api/files/:fileId**: Fetches files from Azure OpenAI.
- **GET /***: Serves static files (HTML, JS)

By default, this application will have Entra ID authentication turned on, meaning only users in your tenant are able to use it. You may change this behavior by updating the Authentication configuration of this App Service instance.

Refer to the [Bot Framework Web Chat project](https://github.com/microsoft/BotFramework-WebChat) for information on customizing this application.
