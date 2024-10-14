// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Builder;
using Microsoft.Bot.Builder.Azure;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Extensions.DependencyInjection;
using Services;

namespace GenAIBot
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public async void ConfigureServices(IServiceCollection services)
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();
            services.AddSingleton(configuration);

            services.AddHttpClient().AddControllers().AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.MaxDepth = HttpHelper.BotMessageSerializerSettings.MaxDepth;
            });

            // Create the Bot Framework Authentication to be used with the Bot Adapter.
            services.AddSingleton<BotFrameworkAuthentication, ConfigurationBotFrameworkAuthentication>();

            // Create the Bot Adapter with error handling enabled.
            services.AddSingleton<IBotFrameworkHttpAdapter, AdapterWithErrorHandler>();

            string userAssignedClientId = configuration.GetValue<string>("MicrosoftAppId");
            var credential = new DefaultAzureCredential(
                new DefaultAzureCredentialOptions { ManagedIdentityClientId = userAssignedClientId }
            );

            AzureOpenAIClient aoaiClient;
            var aoaiApiKey = configuration.GetValue<string>("AZURE_OPENAI_API_KEY");
            if (string.IsNullOrEmpty(aoaiApiKey))
            {
                aoaiClient = new AzureOpenAIClient(
                    endpoint: new Uri(configuration.GetValue<string>("AZURE_OPENAI_API_ENDPOINT")),
                    credential: credential
                );
            }
            else
            {
                aoaiClient = new AzureOpenAIClient(
                    endpoint: new Uri(configuration.GetValue<string>("AZURE_OPENAI_API_ENDPOINT")),
                    credential: new ApiKeyCredential(aoaiApiKey)
                );
            }
            services.AddSingleton(aoaiClient);

            Phi phiClient = new Phi(
                configuration.GetValue<string>("AZURE_AI_PHI_DEPLOYMENT_ENDPOINT"),
                configuration.GetValue<string>("AZURE_AI_PHI_DEPLOYMENT_KEY")
            );

            services.AddSingleton(phiClient);

            IStorage storage;
            if (configuration.GetValue<string>("AZURE_COSMOSDB_ENDPOINT") != null)
            {
                CosmosDbPartitionedStorageOptions cosmosDbStorageOptions;
                if (string.IsNullOrEmpty(configuration.GetValue<string>("AZURE_COSMOSDB_AUTH_KEY")))
                {
                    cosmosDbStorageOptions = new CosmosDbPartitionedStorageOptions
                    {
                        CosmosDbEndpoint = configuration.GetValue<string>("AZURE_COSMOSDB_ENDPOINT"),
                        TokenCredential = credential,
                        DatabaseId = configuration.GetValue<string>("AZURE_COSMOSDB_DATABASE_ID"),
                        ContainerId = configuration.GetValue<string>("AZURE_COSMOSDB_CONTAINER_ID")
                    };
                }
                else
                {
                    cosmosDbStorageOptions = new CosmosDbPartitionedStorageOptions
                    {
                        CosmosDbEndpoint = configuration.GetValue<string>("AZURE_COSMOSDB_ENDPOINT"),
                        AuthKey = configuration.GetValue<string>("AZURE_COSMOSDB_AUTH_KEY"),
                        DatabaseId = configuration.GetValue<string>("AZURE_COSMOSDB_DATABASE_ID"),
                        ContainerId = configuration.GetValue<string>("AZURE_COSMOSDB_CONTAINER_ID")
                    };
                }
                storage = new CosmosDbPartitionedStorage(cosmosDbStorageOptions);
            }
            else
            {
                storage = new MemoryStorage();
            }

            // Create the User state passing in the storage layer.
            var userState = new UserState(storage);
            services.AddSingleton(userState);

            // Create the Conversation state passing in the storage layer.
            var conversationState = new ConversationState(storage);
            services.AddSingleton(conversationState);

            // Add the Login dialog
            services.AddSingleton<LoginDialog>();

            if (!string.IsNullOrEmpty(configuration.GetValue<string>("AZURE_SEARCH_API_ENDPOINT")))
            {
                var searchApiKey = configuration.GetValue<string>("AZURE_SEARCH_API_KEY");
                services.AddSingleton(new AzureSearchChatDataSource()
                {
                    Endpoint = new Uri(configuration.GetValue<string>("AZURE_SEARCH_API_ENDPOINT")),
                    IndexName = configuration.GetValue<string>("AZURE_SEARCH_INDEX"),
                    Authentication = string.IsNullOrEmpty(searchApiKey) ? DataSourceAuthentication.FromSystemManagedIdentity() : DataSourceAuthentication.FromApiKey(searchApiKey),
                });
            }

            // Create the bot as a transient. In this case the ASP Controller is expecting an IBot.
            switch (configuration.GetValue<string>("GEN_AI_IMPLEMENTATION"))
            {
                case "chat-completions":
                    services.AddSingleton<IBot, Bots.ChatCompletionBot<LoginDialog>>();
                    break;
                case "assistant":
                    services.AddSingleton<IBot, Bots.AssistantBot<LoginDialog>>();
                    break;
                case "semantic-kernel":
                    services.AddSingleton<IBot, Bots.SemanticKernelBot<LoginDialog>>();
                    break;
                case "langchain":
                    throw new Exception("Langchain is not supported in this version.");
                case "phi":
                    services.AddSingleton<IBot, Bots.PhiBot<LoginDialog>>();
                    break;
                default:
                    throw new Exception("Invalid engine type");
            }
            services.AddHttpClient();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseDefaultFiles()
                .UseStaticFiles()
                .UseWebSockets()
                .UseRouting()
                .UseAuthorization()
                .UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });

            // app.UseHttpsRedirection();
        }
    }
}
