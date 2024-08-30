// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace GenAIBot.Bots
{
    public class ChatCompletionBot<T> : StateManagementBot<T> where T : Dialog
    {
        private readonly ChatClient _chatClient;
        private readonly string _instructions;
        private readonly AzureSearchChatDataSource _chatDataSource;
        private readonly HttpClient _httpClient;
        private readonly bool _streaming;

        public ChatCompletionBot(IConfiguration config, ConversationState conversationState, UserState userState, AzureOpenAIClient aoaiClient, AzureSearchChatDataSource chatDataSource, HttpClient httpClient, T dialog)
            : base(config, conversationState, userState, dialog)
        {
            _chatClient = aoaiClient.GetChatClient(config["AZURE_OPENAI_DEPLOYMENT_NAME"]);
            _instructions = config["LLM_INSTRUCTIONS"];
            _chatDataSource = chatDataSource;
            _httpClient = httpClient;
            _streaming = config.GetValue("AZURE_OPENAI_STREAMING", false);
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

            // Check if this is a file upload and process it
            var filesUploaded = await HandleFileUploads(turnContext, conversationData, cancellationToken);

            // Return early if there is no text in the message
            if (turnContext.Activity.Text == null)
            {
                return true;
            }

            // Add user message to history
            conversationData.AddTurn("user", turnContext.Activity.Text);

            ChatCompletionOptions options = new();

            if (_chatDataSource != null)
            {
                options.AddDataSource(_chatDataSource);
            }

            var completion = _chatClient.CompleteChatStreamingAsync(messages: conversationData.toMessages(), options: options);
            await ProcessRunStreaming(completion, conversationData, turnContext, cancellationToken);

            return true;
        }

        protected async Task ProcessRunStreaming(AsyncCollectionResult<StreamingChatCompletionUpdate> run, ConversationData conversationData, ITurnContext turnContext, CancellationToken cancellationToken)
        {
            // Start streaming response
            var currentMessage = "";
            IReadOnlyList<AzureChatCitation> citations = [];
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
                // Get citations if they exist
                if (evt.GetAzureMessageContext() != null)
                {
                    citations = evt.GetAzureMessageContext().Citations;
                }
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
                        if (_streaming) {
                            turnContext.SendActivityAsync(msg);
                        }
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
            
            if (citations.Count > 0)
            {
                var message = MessageFactory.Attachment(Utils.Utils.GetCitationsCard(citations));
                await turnContext.SendActivityAsync(message, cancellationToken);
            }
        }
        protected async Task<bool> HandleFileUploads(ITurnContext turnContext, ConversationData conversationData, CancellationToken cancellationToken)
        {
            var filesUploaded = false;
            // Check if incoming message has attached files
            if (turnContext.Activity.Attachments != null)
            {
                foreach (var attachment in turnContext.Activity.Attachments)
                {
                    filesUploaded = true;
                    // Get file contents as stream
                    using var client = new HttpClient();
                    using var response = await client.GetAsync(attachment.ContentUrl);
                    using var stream = response.Content.ReadAsStream();

                    // Add file to attachments in case we need to reference it in Function Calling
                    conversationData.Attachments.Add(new()
                    {
                        Name = attachment.Name,
                        ContentType = attachment.ContentType,
                        Url = attachment.ContentUrl
                    });

                    // If attachment is an image, add it to the conversation history as base64
                    if (attachment.ContentType.Contains("image"))
                    {
                        var byteArray = await _httpClient.GetByteArrayAsync(attachment.ContentUrl);
                        conversationData.AddTurn("user", $"File uploaded: {attachment.Name}", attachment.ContentType, Convert.ToBase64String(byteArray));
                    }

                    await turnContext.SendActivityAsync(MessageFactory.Text($"File uploaded: {attachment.Name}"), cancellationToken);
                }
            }
            return filesUploaded;
        }
    }
}