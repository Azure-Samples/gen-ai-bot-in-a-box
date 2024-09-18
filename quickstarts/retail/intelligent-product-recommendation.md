# Retail Quickstart - Intelligent Product Recommendation

## Use Case

In the Retail & Consumer Goods Industry, highly relevant product discovery by its consumers is critical for any retailer (direct or indirect). This is the first step to drive sales (their top line), as product discovery converts into a sale through multiple channels, offers, promotions etc.

Every major retail store has an online presence in the e-retail space, through their own portals or through other e-retail collaborations, where their consumers can search for products, browse through catalogs, etc.  


## Solution Quickstart

This solution quickstart provides a boilerplate solution to accelerate the development and deployment of intelligent applications designed to assist in product discovery through virtual assistants.

Below is a representation of the solution architecture:


## Deployment Guide

### Infrastructure setup

To deploy this quickstart, start by deploying the Gen AI Bot in-a-Box template. Use one of the options below:

1. Deploy using local Azure Developer CLI

    ```sh
    azd init
    azd env set ALLOWED_IP_ADDRESSES $(dig +short myip.opendns.com @resolver1.opendns.com -4)
    azd up
    ```

2. Set up CI/CD (Recommended)

    > azd pipeline config --provider [azdo | github] --principal-name your_app_registration_name

When prompted, use the following configurations:

| Variable | Description |
| --- | --- |
| deploySearch | true
| implementation | "assistant"
| model | "gpt-4o,2024-05-13"
| modelCapacity | 50
| publicNetworkAccess | "Disabled"
| stack | "dotnetcore\|8.0"
| systemDatastoresAuthMode | "identity"

The following resources will be deployed:

| Service | Functionality |
| --- | --- |
| AI Services account | Hosts GPT-4o large language model |
| Cosmos DB noSQL account | Stores conversation history |
| Key Vault (Empty) | Stores application certificates and secrets |
| Storage Account (Empty) | Stores application data |
| Bot Service | Orchestrates communication with App Services |
| App Service Plan | Hosts application infrastructure |
| App Service (backend) | Hosts bot communication |
| App Service (frontend) | Hosts web chat interface |
| Managed Identity | Bot and app identity |
| Virtual Network | Host networking configurations |
| Private endpoints + Network Interface for all services | Enables private communication between apps and backend services |

Both applications will be automatically configured with the required environment variables. Below is the reference for the environment variables in each app.

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

### Quickstart configuration

Use the following steps to further configure the application for the e-retail scenario.

1. **System prompt**: Provide the necessary instructions to shape the behavior of the assistant depending on the use case. System messages can be provided in the App Service's (backend) Environment Variables, as shown below:

    ![alt text](image.png)

    Use the examples below and make adjustments as needed.

    > You are a helpful e-retail self-service virtual assistant. Your role is to help customers find the most relevant products based on their interactions. You have access to the product catalog and may issue searches to it as you interact with potential customers in order to recommend and provide details about available products. When referring to a product, always provide the link back to the online store, listed price, and display an image of the product when available.

2. Complete Entra ID Authentication Setup

Configure the web platform for the App Registration created as part of the deployment process. Make sure to add a callback URL in the format: `https://FRONTENT_APP_NAME.azurewebsites.net/.auth/login/aad/callback`

![alt text](image-3.png)

In the same resource, add the `openid` MS Graph permission to allow your application to sign users in.

![alt text](image-4.png)

3. Content Filtering

Configure content filters to properly safeguard the application from unintended use. 

- Navigate to the [Content Filter section of AI Studio](https://ai.azure.com/resource/contentfilters/contentFilter)
- Adjust each filter category accordingly.
![alt text](image-2.png)
> Note: when content filtering is triggered, the bot will refuse to respond. Use this to limit the bot's responses on some categories.

Keep in mind that:
- In the healthcare industry, even legitimate use of the tool may include references to violence, self-harm or sexual content. Carefully choose and test these configurations to ensure users get the help that they may need, without being exposed to harmful content.
- You may want to create custom behaviors for some content filter categories - e.g. provide emergency hotline numbers. For others, it may be enough to refuse to answer.

## User Guide

### Using the webchat

After deployment, you may reach the webchat by accessing the front-end application's default domain:

![Front-end app default domain](image-1.png)

You may interact with the assistant by typing into the chat, or by clicking the microphone icon and speaking. Start by specifying the role of the assistant in the conversation.

When using audio input, select the language before speaking. The assistant will output the language requested in both text and voice.

### Key functionalities

- **Text-babsed search**: Seaerch for products in the catalog by interacting with the bot
- **Image-based search**: Provide images of items similar or that provide further insight into products you are looking for.
- **Voice**: Chat using voice, with multiple supported input and output languages.
- **Document upload**: Upload exam results, prescriptions and other health-related documents across any languages and request further information.

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

### Customizing Web Chat

The front-end application, contained in the [./src/webchat](./src/webchat) is a NodeJS Restify app, with the following endpoints:

- **GET /api/directline/token**: Generates temporary Direct Line token, which allows users to start and interact with a conversation.
- **GET /api/speech/token**: Generates temporary Speech token, which allows users to leverage speech capabilities.
- **GET /api/files/:fileId**: Fetches files from Azure OpenAI.
- **GET /***: Serves static files (HTML, JS)

By default, this application will have Entra ID authentication turned on, meaning only users in your tenant are able to use it. You may change this behavior by updating the Authentication configuration of this App Service instance.

Refer to the [Bot Framework Web Chat project](https://github.com/microsoft/BotFramework-WebChat) for information on customizing this application.

## Troubleshooting

Please refer to the [Main README](../../README.md) for troubleshooting steps.

## References

1. [Implications of Language Barriers for Healthcare: A Systematic Review](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC7201401/)