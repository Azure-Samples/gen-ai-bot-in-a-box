#pragma warning disable AOAI001

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.BotBuilderSamples;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using System.Linq;
using AdaptiveCards;
using OpenAI.Chat;
using Azure.AI.OpenAI.Chat;
using System.Text.Json;

namespace GenAIBot.Bots
{


    public class ChatCompletionBot : StateManagementBot
    {
        private readonly ChatClient _chatClient;
        private readonly string _instructions;
        private readonly string _searchEndpoint;
        private readonly string _searchKey;
        private readonly string _searchIndex;

        public ChatCompletionBot(IConfiguration config, ConversationState conversationState, UserState userState, AzureOpenAIClient aoaiClient)
            : base(conversationState, userState)
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
                    await turnContext.SendActivityAsync(MessageFactory.Text("Hello and welcome to the Chat Completion Bot!"), cancellationToken);
                }
            }
        }

        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
            var conversationData = await conversationStateAccessors.GetAsync(
                turnContext, () => new ConversationData()
                {
                    History = new List<ConversationTurn>() {
                        new() { Role = "system", Message = _instructions }
                    }
                });
            var userStateAccessors = _userState.CreateProperty<UserProfile>(nameof(UserProfile));
            var userProfile = await userStateAccessors.GetAsync(turnContext, () => new UserProfile());

            conversationData.AddTurn("user", turnContext.Activity.Text);

            ChatCompletionOptions options = new();

            if (!string.IsNullOrEmpty(_searchEndpoint))
            {
                options.AddDataSource(new AzureSearchChatDataSource()
                {
                    Endpoint = new Uri(_searchEndpoint),
                    IndexName = _searchIndex,
                    Authentication = DataSourceAuthentication.FromSystemManagedIdentity()
                });
            }

            var completion = _chatClient.CompleteChat(messages: conversationData.toMessages(), options: options);

            var response = completion.Value.Content[0].Text;

            conversationData.AddTurn("assistant", response);
            await turnContext.SendActivityAsync(MessageFactory.Text(response), cancellationToken);

            if (!string.IsNullOrEmpty(_searchEndpoint))
            {
                var citations = completion.Value.GetAzureMessageContext().Citations;
                if (citations.Count > 0)
                {
                    var message = MessageFactory.Attachment(GetCitationsCard(citations));
                    await turnContext.SendActivityAsync(message, cancellationToken);
                }
            }
        }

        private Microsoft.Bot.Schema.Attachment GetCitationsCard(IReadOnlyList<AzureChatCitation> citations)
        {
            var citationBlocks = citations.Select(getCitationBlock).ToList();
            return new Microsoft.Bot.Schema.Attachment()
            {
                ContentType = AdaptiveCard.ContentType,
                Content = new AdaptiveCard("1.2")
                {
                    Version = "1.2",
                    Body = citationBlocks
                }
            };
        }

        private AdaptiveElement getCitationBlock(AzureChatCitation citation, int index)
        {

            return new AdaptiveContainer()
            {
                Items = [
                    new AdaptiveColumnSet() {
                        Columns = [
                            new AdaptiveColumn() {
                                Items = [
                                    new AdaptiveTextBlock() {
                                        Text = $"[{citation.Title}]({citation.Url})",
                                        Wrap = true,
                                        Size = AdaptiveTextSize.Medium
                                    }
                                ],
                                Width = "stretch"
                            },
                            new AdaptiveColumn() {
                                Id = $"chevronDown{index}",
                                Spacing = AdaptiveSpacing.Small,
                                VerticalContentAlignment = AdaptiveVerticalContentAlignment.Center,
                                Items = [
                                    new AdaptiveImage() {
                                        Url = new Uri("https://adaptivecards.io/content/down.png"),
                                        PixelWidth = 20,
                                        AltText = "collapsed"
                                    }
                                ],
                                Width = "auto",
                                IsVisible = false
                            },
                            new AdaptiveColumn() {
                                Id = $"chevronUp{index}",
                                Spacing = AdaptiveSpacing.Small,
                                VerticalContentAlignment = AdaptiveVerticalContentAlignment.Center,
                                Items = [
                                    new AdaptiveImage() {
                                        Url = new Uri("https://adaptivecards.io/content/up.png"),
                                        PixelWidth = 20,
                                        AltText = "expanded"
                                    }
                                ],
                                Width = "auto"
                            }
                        ],
                        SelectAction = new AdaptiveToggleVisibilityAction() {
                            TargetElements = [
                                $"cardContent{index}",
                                $"chevronUp{index}",
                                $"chevronDown{index}"
                            ]
                        }
                    },
                    new AdaptiveContainer() {
                        Id = $"cardContent{index}",
                        Items = [
                            new AdaptiveContainer() {
                                Items = [
                                    new AdaptiveTextBlock() {
                                        Text = citation.Content,
                                        IsSubtle = true,
                                        Wrap = true
                                    }
                                ]
                            }
                        ],
                        IsVisible = false
                    }
                ],
                Separator = true,
                Spacing = AdaptiveSpacing.Medium
            };
        }
    }
}