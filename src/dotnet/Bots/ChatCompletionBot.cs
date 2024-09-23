// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.IO;
using System.Text.Json.Nodes;
using Plugins;

namespace GenAIBot.Bots
{
    public class ChatCompletionBot<T> : StateManagementBot<T> where T : Dialog
    {
        private readonly ChatClient _chatClient;
        private readonly string _instructions;
        private readonly string _welcomeMessage;
        private readonly AzureSearchChatDataSource _chatDataSource;
        private readonly HttpClient _httpClient;
        private readonly bool _streaming;
        private readonly ChatCompletionOptions _chatCompletionOptions;

        public ChatCompletionBot(IConfiguration config, ConversationState conversationState, UserState userState, AzureOpenAIClient aoaiClient, HttpClient httpClient, T dialog, AzureSearchChatDataSource chatDataSource = null)
            : base(config, conversationState, userState, dialog)
        {
            _chatClient = aoaiClient.GetChatClient(config["AZURE_OPENAI_DEPLOYMENT_NAME"]);
            _instructions = config["LLM_INSTRUCTIONS"];
            _welcomeMessage = config.GetValue("LLM_WELCOME_MESSAGE", "Hello and welcome to the Chat Completions Bot Dotnet!");
            _chatDataSource = chatDataSource;
            _httpClient = httpClient;

            _chatCompletionOptions = new();

            // Use either OYD or function calling, not both. Enabling search will disable function calling.
            if (_chatDataSource != null)
            {
                _chatCompletionOptions.AddDataSource(_chatDataSource);
            }
            else
            {
                foreach (var plugin in new List<string>() { "./Plugins/WikipediaQuery.json", "./Plugins/WikipediaGetArticle.json" })
                {
                    var dict = JsonSerializer.Deserialize<JsonNode>(File.ReadAllText(plugin))["function"];
                    _chatCompletionOptions.Tools.Add(ChatTool.CreateFunctionTool((string)dict["name"], (string)dict["description"], BinaryData.FromString(dict["parameters"].ToJsonString())));
                }
            }
        }

        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            foreach (var member in membersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text(_welcomeMessage), cancellationToken);
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

            var messages = conversationData.toMessages();
            // Do not use OYD when images are provided
            var completionOptions = conversationData.History.Exists(x => x.ImageData != null) ?
                new() : _chatCompletionOptions;
            var completion = _chatClient.CompleteChatStreamingAsync(messages: messages, options: completionOptions);
            await ProcessRunStreaming(completion, conversationData, turnContext, cancellationToken);
            // If OYD is enabled, remove image data from history after using it
            if (_chatCompletionOptions.GetDataSources().Count > 0)
            {
                conversationData.History.RemoveAll(x => x.ImageData != null);
            }

            return true;
        }

        protected async Task ProcessRunStreaming(AsyncCollectionResult<StreamingChatCompletionUpdate> run, ConversationData conversationData, ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            // Start streaming response
            var currentMessage = "";
            var currentToolName = "";
            var currentToolId = "";
            var currentToolArgs = "";
            IReadOnlyList<AzureChatCitation> citations = [];
            List<ToolOutput> toolOutputs = new();
            string activityId;
            int streamSequence = 1;
            activityId = await SendInterimMessage(turnContext, "Typing...", ++streamSequence, null, "typing");

            await foreach (StreamingChatCompletionUpdate evt in run)
            {
                if (evt.ToolCallUpdates.Count > 0)
                {
                    var update = evt.ToolCallUpdates[0];
                    if (update.Id != null)
                    {
                        if (currentToolId != "")
                        {
                            await FlushToolCalls(turnContext, conversationData, toolOutputs, currentToolId, currentToolName, currentToolArgs);
                        }
                        currentToolId = update.Id;
                        currentToolName = update.FunctionName;
                        currentToolArgs = "";
                    }
                    if (update.FunctionArgumentsUpdate != null)
                        currentToolArgs += update.FunctionArgumentsUpdate;
                }
                if (evt.FinishReason == ChatFinishReason.ToolCalls)
                {
                    await FlushToolCalls(turnContext, conversationData, toolOutputs, currentToolId, currentToolName, currentToolArgs);
                }
                if (evt.GetAzureMessageContext() != null)
                {
                    citations = evt.GetAzureMessageContext().Citations;
                }
                foreach (var messageContentUpdate in evt.ContentUpdate)
                {
                    if (messageContentUpdate.Text != null)
                    {
                        currentMessage += messageContentUpdate.Text;
                        SendInterimMessage(turnContext, currentMessage, ++streamSequence, activityId, "typing");
                    }
                    else if (messageContentUpdate.ImageUri != null)
                    {
                        currentMessage += $"![image]({messageContentUpdate.ImageUri})";
                    }
                }

            }

            // Recursively process the run with the tool outputs
            if (toolOutputs.Count > 0)
            {
                var messages = conversationData.toMessages();
                var newRun = _chatClient.CompleteChatStreamingAsync(messages: messages, options: _chatCompletionOptions);
                await ProcessRunStreaming(newRun, conversationData, turnContext, cancellationToken);
                return;
            }

            // Add assistant message to history
            conversationData.AddTurn("assistant", currentMessage);
            // Remove all tool calls from history
            conversationData.History.RemoveAll(t => t.Role == "tool" || t.ToolCalls != null);

            streamSequence++;
            await SendInterimMessage(turnContext, currentMessage, ++streamSequence, activityId, "message");

            if (citations.Count > 0)
            {
                var message = MessageFactory.Attachment(Utils.Utils.GetCitationsCard(citations));
                await turnContext.SendActivityAsync(message, cancellationToken);
            }

        }
        protected async Task FlushToolCalls(ITurnContext<IMessageActivity> turnContext, ConversationData conversationData, List<ToolOutput> toolOutputs, string currentToolId, string currentToolName, string currentToolArgs)
        {
            var arguments = JsonSerializer.Deserialize<JsonNode>(currentToolArgs, new JsonSerializerOptions());
            string response = "";
            if (currentToolName == "wikipedia_query")
            {
                response = await new WikipediaPlugin(conversationData, turnContext).QueryArticles(arguments["query"].ToString());
            }
            if (currentToolName == "wikipedia_get_article")
            {
                response = await new WikipediaPlugin(conversationData, turnContext).GetArticle(arguments["article_name"].ToString());
            }
            toolOutputs.Add(new ToolOutput(currentToolId, response));
            conversationData.AddTurn("assistant", toolCalls: new List<ChatToolCall>() { ChatToolCall.CreateFunctionToolCall(currentToolId, currentToolName, currentToolArgs) });
            conversationData.AddTurn("tool", toolCallId: currentToolId, message: response);
        }

        protected async Task<bool> HandleFileUploads(ITurnContext turnContext, ConversationData conversationData, CancellationToken cancellationToken)
        {
            var filesUploaded = false;
            // Check if incoming message has attached files
            if (turnContext.Activity.Attachments != null)
            {
                foreach (var attachment in turnContext.Activity.Attachments)
                {
                    if (attachment.ContentUrl == null)
                    {
                        continue;
                    }
                    filesUploaded = true;
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