#pragma warning disable AOAI001

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Azure.AI.OpenAI.Chat;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.BotBuilderSamples;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;
using Utils;

namespace GenAIBot.Bots
{
    public class ChatCompletionBot : StateManagementBot
    {
        private readonly ChatClient _chatClient;
        private readonly string _instructions;
        private readonly string _searchEndpoint;
        private readonly string _searchIndex;

        public ChatCompletionBot(IConfiguration config, ConversationState conversationState, UserState userState, AzureOpenAIClient aoaiClient)
            : base(conversationState, userState)
        {
            _chatClient = aoaiClient.GetChatClient(config["AZURE_OPENAI_DEPLOYMENT_NAME"]);
            _instructions = config["LLM_INSTRUCTIONS"];

            _searchEndpoint = config["AZURE_SEARCH_API_ENDPOINT"];
            _searchIndex = config["AZURE_SEARCH_INDEX"];
        }

        // Modify onMembersAdded as needed
        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            foreach (var member in membersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text("Hello and welcome to the Chat Completion Bot Dotnet!"), cancellationToken);
                }
            }
        }

        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            // Load conversation state
            var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
            var conversationData = await conversationStateAccessors.GetAsync(
                turnContext, () => new ConversationData()
                {
                    History = new List<ConversationTurn>() {
                        new() { Role = "system", Message = _instructions }
                    }
                });

            // Catch any special messages here
            
            // Add user message to history
            conversationData.AddTurn("user", turnContext.Activity.Text);

            // Run logic to obtain response here
            ChatCompletionOptions options = new();

            if (!string.IsNullOrEmpty(_searchEndpoint))
            {
                options.AddDataSource(new AzureSearchChatDataSource()
                {
                    Endpoint = new System.Uri(_searchEndpoint),
                    IndexName = _searchIndex,
                    Authentication = DataSourceAuthentication.FromSystemManagedIdentity()
                });
            }

            var completion = _chatClient.CompleteChat(messages: conversationData.toMessages(), options: options);
            var response = completion.Value.Content[0].Text;

            // Add assistant message to history
            conversationData.AddTurn("assistant", response);
            
            // Respond back to user
            await turnContext.SendActivityAsync(MessageFactory.Text(response), cancellationToken);

            // Send citations if they exist
            if (!string.IsNullOrEmpty(_searchEndpoint))
            {
                var citations = completion.Value.GetAzureMessageContext().Citations;
                if (citations.Count > 0)
                {
                    var message = MessageFactory.Attachment(Utils.Utils.GetCitationsCard(citations));
                    await turnContext.SendActivityAsync(message, cancellationToken);
                }
            }
        }
    }
}