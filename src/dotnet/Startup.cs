// Generated with EchoBot .NET Template version v4.22.0

using System;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Azure;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        public void ConfigureServices(IServiceCollection services)
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

            AzureOpenAIClient aoaiClient = new AzureOpenAIClient(
                endpoint: new Uri(configuration.GetValue<string>("AZURE_OPENAI_API_ENDPOINT")),
                credential: credential
            );
            services.AddSingleton(aoaiClient);
            
            Phi phiClient = new Phi(
                configuration.GetValue<string>("AZURE_AI_PHI_DEPLOYMENT_ENDPOINT"),
                configuration.GetValue<string>("AZURE_AI_PHI_DEPLOYMENT_KEY")
            );

            services.AddSingleton(phiClient);

            IStorage storage;
            if (configuration.GetValue<string>("AZURE_COSMOSDB_ENDPOINT") != null)
            {
                var cosmosDbStorageOptions = new CosmosDbPartitionedStorageOptions()
                {
                    CosmosDbEndpoint = configuration.GetValue<string>("AZURE_COSMOSDB_ENDPOINT"),
                    TokenCredential = credential,
                    DatabaseId = configuration.GetValue<string>("AZURE_COSMOSDB_DATABASE_ID"),
                    ContainerId = configuration.GetValue<string>("AZURE_COSMOSDB_CONTAINER_ID")
                };
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

            // Create the bot as a transient. In this case the ASP Controller is expecting an IBot.

            // In Dotnet:
            switch (configuration.GetValue<string>("GEN_AI_IMPLEMENTATION"))
            {
                case "chat-completions":
                    services.AddTransient<IBot, Bots.ChatCompletionBot>();
                    break;
                case "assistant":
                    services.AddTransient<IBot, Bots.AssistantBot>();
                    break;
                case "semantic-kernel":
                    throw new Exception("Semantic Kernel is not supported in this version.");
                case "langchain":
                    throw new Exception("Langchain is not supported in this version.");
                case "phi":
                    services.AddTransient<IBot, Bots.PhiBot>();
                    break;
                default:
                    throw new Exception("Invalid engine type");
            }
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
