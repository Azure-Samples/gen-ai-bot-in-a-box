#pragma warning disable AOAI001

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Azure.AI.OpenAI.Chat;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.BotBuilderSamples;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;

namespace GenAIBot.Bots
{
    public class ChatCompletionBot<T> : StateManagementBot<T> where T : Dialog
    {
        private readonly ChatClient _chatClient;
        private readonly string _instructions;
        private readonly string _searchEndpoint;
        private readonly string _searchIndex;

        public ChatCompletionBot(IConfiguration config, ConversationState conversationState, UserState userState, AzureOpenAIClient aoaiClient, T dialog)
            : base(config, conversationState, userState, dialog)
        {
            _chatClient = aoaiClient.GetChatClient(config["AZURE_OPENAI_DEPLOYMENT_NAME"]);
            _instructions = config["LLM_INSTRUCTIONS"];

            _searchEndpoint = config["AZURE_SEARCH_API_ENDPOINT"];
            _searchIndex = config["AZURE_SEARCH_INDEX"];
        }

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

        protected override async Task<bool> OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            // Enforce login
            var loggedIn = await HandleLogin(turnContext, cancellationToken);
            if (!loggedIn)
            {
                return false;
            }

            // Load conversation state
            var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
            var conversationData = await conversationStateAccessors.GetAsync(
                turnContext, () => new ConversationData()
                {
                    History = new List<ConversationTurn>() {
                        new() { Role = "system", Message = _instructions }
                    }
                });

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

            var completion = _chatClient.CompleteChatStreamingAsync(messages: conversationData.toMessages(), options: options);
            await ProcessRunStreaming(completion, conversationData, turnContext, cancellationToken);

            // Send citations if they exist
            // if (!string.IsNullOrEmpty(_searchEndpoint))
            // {
            //     var citations = completion.Value.GetAzureMessageContext().Citations;
            //     if (citations.Count > 0)
            //     {
            //         var message = MessageFactory.Attachment(Utils.Utils.GetCitationsCard(citations));
            //         await turnContext.SendActivityAsync(message, cancellationToken);
            //     }
            // }
            return true;
        }

        protected async Task ProcessRunStreaming(AsyncResultCollection<StreamingChatCompletionUpdate> run, ConversationData conversationData, ITurnContext turnContext, CancellationToken cancellationToken)
        {
            // Start streaming response
            var currentMessage = "";
            List<ToolOutput> toolOutputs = new();
            string activityId;
            int streamSequence = 1;

            var firstMessage = new Activity
            {
                ChannelData = new Dictionary<string, object>() {
                {"streamSequence", streamSequence},
                {"streamType", "informative"}
            },
                Text = "Typing...",
                Type = "typing"
            };
            activityId = (await turnContext.SendActivityAsync(firstMessage)).Id;

            await foreach (StreamingChatCompletionUpdate evt in run)
            {
                foreach (var messageContentUpdate in evt.ContentUpdate)
                {
                    if (messageContentUpdate.Text != null)
                    {
                        currentMessage += messageContentUpdate.Text;
                        streamSequence++;
                        var msg = new Activity
                        {
                            ChannelData = new Dictionary<string, object>() {
                            {"streamId", activityId},
                            {"streamSequence", streamSequence},
                            {"streamType", "streaming"}
                        },
                            Id = activityId,
                            Type = "typing",
                            Text = currentMessage
                        };
                        turnContext.SendActivityAsync(msg);
                    }
                    else if (messageContentUpdate.ImageUri != null)
                    {
                        currentMessage += $"![image]({messageContentUpdate.ImageUri})";
                    }
                }

            }

            // Add assistant message to history
            conversationData.AddTurn("assistant", currentMessage);

            streamSequence++;
            var finalMsg = new Activity
            {
                ChannelData = new Dictionary<string, object>() {
                {"streamId", activityId},
                {"streamSequence", streamSequence},
                {"streamType", "final"}
            },
                Id = activityId,
                Text = currentMessage,
                Type = "message"
            };
            await turnContext.SendActivityAsync(finalMsg);
        }
    }
}