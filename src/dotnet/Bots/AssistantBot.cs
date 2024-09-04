// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace GenAIBot.Bots
{
    public class AssistantBot<T> : StateManagementBot<T> where T : Dialog
    {
        private readonly string _instructions;
        private readonly string _welcomeMessage;
        private readonly string _assistantId;
        private readonly AssistantClient _assistantClient;
        private readonly ChatClient _chatClient;
        private readonly FileClient _fileClient;
        private readonly HttpClient _httpClient;

        public AssistantBot(IConfiguration config, ConversationState conversationState, UserState userState, AzureOpenAIClient aoaiClient, HttpClient httpClient, T dialog)
            : base(config, conversationState, userState, dialog)
        {
            _assistantClient = aoaiClient.GetAssistantClient();
            _chatClient = aoaiClient.GetChatClient(config["AZURE_OPENAI_DEPLOYMENT_NAME"]);
            _fileClient = aoaiClient.GetFileClient();
            _httpClient = httpClient;
            
            _assistantId = config["AZURE_OPENAI_ASSISTANT_ID"];
            _instructions = config["LLM_INSTRUCTIONS"];
            _welcomeMessage = config.GetValue("LLM_WELCOME_MESSAGE", "Hello and welcome to the Assistant Bot Dotnet!");
        }

        // Modify onMembersAdded as needed
        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            foreach (var member in membersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text(_welcomeMessage, _welcomeMessage), cancellationToken);
                }
            }
            await HandleLogin(turnContext, cancellationToken);
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

            // Create a new thread if one does not exist
            AssistantThread thread;
            if (conversationData.ThreadId == null)
            {
                thread = _assistantClient.CreateThread().Value;
                conversationData.ThreadId = thread.Id;
            }
            else
            {
                thread = _assistantClient.GetThread(conversationData.ThreadId).Value;
            }

            // Check if this is a file upload and process it
            var filesUploaded = await HandleFileUploads(turnContext, thread, conversationData, cancellationToken);

            // Return early if there is no text in the message
            if (turnContext.Activity.Text == null)
            {
                return true;
            }

            // Check if this is a file upload follow up - user selects a file upload option
            if (turnContext.Activity.Text.StartsWith("#UPLOAD_FILE"))
            {
                // Get upload metadata
                var metadata = turnContext.Activity.Text.Split("#");
                var tool = metadata[2];
                var fileName = metadata[3];
                // Get file from attachments
                var attachment = conversationData.Attachments.Find(a => a.Name == fileName);
                // Add file upload to relevant tool
                var options = new MessageCreationOptions();
                var stream = await _httpClient.GetStreamAsync(attachment.Url);
                var fileResponse = await _fileClient.UploadFileAsync(stream, attachment.Name, "assistants");
                // Send the file to the assistant
                List<ToolDefinition> tools = new();
                if (tool.Contains("code_interpreter"))
                {
                    tools.Add(ToolDefinition.CreateCodeInterpreter());
                }
                if (tool.Contains("file_search"))
                {
                    tools.Add(ToolDefinition.CreateFileSearch());
                }
                options.Attachments.Add(new MessageCreationAttachment(tools: tools, fileId: fileResponse.Value.Id));
                var msg = await _assistantClient.CreateMessageAsync(thread, MessageRole.User, [$"File uploaded: {attachment.Name}"], options);
                // Send feedback to user
                await turnContext.SendActivityAsync(MessageFactory.Text($"File added to {tool} successfully!"), cancellationToken);
                return true;
            }
            // Add user message to history
            conversationData.AddTurn("user", turnContext.Activity.Text);

            // Send user message to thread
            _assistantClient.CreateMessage(thread, MessageRole.User, new List<MessageContent>() { MessageContent.FromText(turnContext.Activity.Text) });

            // Run thread
            var run = _assistantClient.CreateRunStreamingAsync(
                conversationData.ThreadId,
                _assistantId,
                new RunCreationOptions()
                {
                    InstructionsOverride = _instructions
                }
            );

            // Process run streaming
            await ProcessRunStreaming(run, conversationData, turnContext, cancellationToken);

            return true;

        }

        protected async Task ProcessRunStreaming(AsyncCollectionResult<StreamingUpdate> run, ConversationData conversationData, ITurnContext turnContext, CancellationToken cancellationToken)
        {
            // Start streaming response
            var currentMessage = "";
            List<ToolOutput> toolOutputs = new();
            ThreadRun currentRun = null;
            string activityId;
            int streamSequence = 1;
            activityId = await SendInterimMessage(turnContext, "Typing...", streamSequence, null, "typing");

            await foreach (StreamingUpdate evt in run)
            {
                if (evt is RunUpdate)
                {
                    var runUpdate = (RunUpdate)evt;
                    currentRun = runUpdate.Value;
                }
                else if (evt is RequiredActionUpdate)
                {
                    var requiredActionUpdate = (RequiredActionUpdate)evt;
                    var argumentsString = requiredActionUpdate.FunctionArguments;
                    var arguments = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsString, new JsonSerializerOptions());
                    if (requiredActionUpdate.FunctionName == "image_query")
                    {
                        var response = await ImageQuery(conversationData, arguments["query"], arguments["image_name"]);
                        toolOutputs.Add(new ToolOutput(requiredActionUpdate.ToolCallId, response));
                    }
                }
                else if (evt is MessageContentUpdate)
                {
                    var messageContentUpdate = (MessageContentUpdate)evt;
                    if (messageContentUpdate.Text != null)
                    {
                        currentMessage += messageContentUpdate.Text;
                        streamSequence++;
                        // Flush content every 50 messages
                        if (streamSequence % 50 == 0)
                        {
                            await SendInterimMessage(turnContext, currentMessage, streamSequence, activityId, "typing");
                        }
                    }
                    else if (messageContentUpdate.ImageFileId != null)
                    {
                        currentMessage += $"![{messageContentUpdate.ImageFileId}](/api/files/{messageContentUpdate.ImageFileId})";
                    }
                }
            }
            // Recursively process the run with the tool outputs
            if (toolOutputs.Count > 0)
            {
                var newRun = _assistantClient.SubmitToolOutputsToRunStreamingAsync(currentRun, toolOutputs);
                await ProcessRunStreaming(newRun, conversationData, turnContext, cancellationToken);
                return;
            }

            // Add assistant message to history
            conversationData.AddTurn("assistant", currentMessage);
            await SendInterimMessage(turnContext, currentMessage, ++streamSequence, activityId, "message");
        }

        protected async Task<bool> HandleFileUploads(ITurnContext turnContext, AssistantThread thread, ConversationData conversationData, CancellationToken cancellationToken)
        {
            var filesUploaded = false;
            // Check if incoming message has attached files
            if (turnContext.Activity.Attachments != null)
            {
                foreach (var attachment in turnContext.Activity.Attachments)
                {
                    if (attachment.ContentUrl == null) {
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

                    // Add file upload notice to conversation history, frontend, and assistant
                    conversationData.AddTurn("user", $"File uploaded: {attachment.Name}");
                    await turnContext.SendActivityAsync(MessageFactory.Text($"File uploaded: {attachment.Name}"), cancellationToken);
                    await _assistantClient.CreateMessageAsync(thread, MessageRole.User, [$"File uploaded: {attachment.Name}"]);
                    // Ask whether to add file to a tool
                    await turnContext.SendActivityAsync(MessageFactory.SuggestedActions(
                        cardActions:
                        [
                            new CardAction(title: "Code Interpreter", type: ActionTypes.ImBack, value: "#UPLOAD_FILE#code_interpreter#" + attachment.Name),
                            new CardAction( title: "File Search", type: ActionTypes.ImBack, value: "#UPLOAD_FILE#file_search#" + attachment.Name),
                            new CardAction( title: "Both", type: ActionTypes.ImBack, value: "#UPLOAD_FILE#code_interpreter,file_search#" + attachment.Name),
                        ],
                        "Add to a tool? (ignore if not needed)",
                        null,
                        null
                    ), cancellationToken);
                }
            }
            return filesUploaded;
        }

        private async Task<string> ImageQuery(ConversationData conversationData, string query, string image_name)
        {
            // Find image in attachments by name
            var image = conversationData.Attachments.Find(a => a.Name == image_name.Split('/').Last());
            // Handle image not found

            // Read image.Url into BinaryData
            var byteArray = await _httpClient.GetByteArrayAsync(image.Url);
            var binaryData = new BinaryData(byteArray);

            var messages = new List<ChatMessage>() {
                new UserChatMessage(new List<ChatMessageContentPart>(){
                    ChatMessageContentPart.CreateTextMessageContentPart(query),
                    ChatMessageContentPart.CreateImageMessageContentPart(binaryData, image.ContentType),
                })
            };
            var response = await _chatClient.CompleteChatAsync(messages);
            return response.Value.Content.First().Text;
        }
    }
}
