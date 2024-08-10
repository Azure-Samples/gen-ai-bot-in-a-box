using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.BotBuilderSamples;
using Microsoft.Extensions.Configuration;
using OpenAI.Assistants;
using OpenAI.Chat;
using OpenAI.Files;

namespace GenAIBot.Bots
{
    public class AssistantBot<T> : StateManagementBot<T> where T : Dialog
    {
        private readonly string _instructions;
        private readonly string _welcomeMessage;
        private readonly string _appUrl;
        private readonly AssistantClient _assistantClient;
        private readonly ChatClient _chatClient;
        private readonly FileClient _fileClient;
        private readonly HttpClient _httpClient;
        private readonly string _assistantId;
        
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
            _appUrl = config.GetValue("APP_HOSTNAME", "http://localhost:3978");
            if (!_appUrl.StartsWith("http"))
            {
                _appUrl = $"https://{_appUrl}";
            }
        }

        // Modify onMembersAdded as needed
        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            foreach (var member in membersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text(_welcomeMessage), cancellationToken);
                }
            }
            await HandleLogin(turnContext, cancellationToken);
        }

        protected override async Task<bool> OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
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

            // Enforce login
            var loggedIn = await HandleLogin(turnContext, cancellationToken);
            if (!loggedIn)
            {
                return false;
            }

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
                return false;
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
                var msg = await _assistantClient.CreateMessageAsync(thread, [$"File uploaded: {attachment.Name}"], options);
                // Send feedback to user
                await turnContext.SendActivityAsync(MessageFactory.Text($"File added to {tool} successfully!"), cancellationToken);
                return true;
            }
            // Add user message to history
            conversationData.AddTurn("user", turnContext.Activity.Text);

            // Send user message to thread
            _assistantClient.CreateMessage(thread, new List<MessageContent>() { MessageContent.FromText(turnContext.Activity.Text) });

            // Run thread
            // var run = _assistantClient.CreateRunStreamingAsync(
            //     conversationData.ThreadId,
            //     _assistantId,
            //     new RunCreationOptions()
            //     {
            //         InstructionsOverride = _instructions
            //     }
            // );
            var run = (await _assistantClient.CreateRunAsync(
                conversationData.ThreadId,
                _assistantId,
                new RunCreationOptions()
                {
                    InstructionsOverride = _instructions
                }
            )).Value;

            // Start streaming response
            // if type(event) == ThreadMessageDelta:
            //     deltaBlock = event.data.delta.content[0]
            //     if type(deltaBlock) == TextDeltaBlock:
            //         current_message += deltaBlock.text.value
            //     elif type(deltaBlock) == ImageFileDeltaBlock: 
            //         current_message += f"![{deltaBlock.image_file.file_id}](/api/files/{deltaBlock.image_file.file_id})"

            // await foreach (StreamingUpdate evt in run)
            // {
            //     if (evt is MessageContentUpdate)
            //     {
            //         Console.WriteLine(JsonSerializer.Serialize((RunUpdate)evt));
            //     }
            //     else
            //     {
            //         Console.WriteLine(evt.GetType());
            //     }
            // }

            while (
                run.Status != RunStatus.Completed &&
                run.Status != RunStatus.Failed)
            {
                run = _assistantClient.GetRun(conversationData.ThreadId, run.Id).Value;
                if (run.Status == RunStatus.RequiresAction)
                {
                    List<ToolOutput> toolOutputs = new();
                    foreach (var toolCall in run.RequiredActions)
                    {
                        var argumentsString = toolCall.FunctionArguments;
                        var arguments = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsString, new JsonSerializerOptions());
                        if (toolCall.FunctionName == "image_query")
                        {
                            var response = await ImageQuery(conversationData, arguments["query"], arguments["image_name"]);
                            toolOutputs.Add(new ToolOutput(toolCall.ToolCallId, response));
                        }
                    }
                    await _assistantClient.SubmitToolOutputsToRunAsync(conversationData.ThreadId, run.Id, toolOutputs, cancellationToken);
                }
                Thread.Sleep(1000);
            }

            var messages = _assistantClient.GetMessages(conversationData.ThreadId);

            foreach (var message in messages)
            {
                // Stop on the last user message
                if (message.Role == MessageRole.User)
                    break;
                foreach (MessageContent item in message.Content)
                {
                    if (!string.IsNullOrEmpty(item.Text))
                    {
                        var response = item.Text;
                        // Add assistant message to history
                        conversationData.AddTurn("assistant", response);

                        // Respond back to user
                        await turnContext.SendActivityAsync(MessageFactory.Text(response), cancellationToken);
                    }
                    else if (!string.IsNullOrEmpty(item.ImageFileId))
                    {
                        var response = $"![{item.ImageFileId}]({_appUrl}/api/files/{item.ImageFileId})";
                        // Add assistant message to history
                        conversationData.AddTurn("assistant", response);
                        // Respond back to user
                        await turnContext.SendActivityAsync(MessageFactory.Text(response), cancellationToken);
                    }
                }
            }
            return true;

        }

        protected async Task<bool> HandleFileUploads(ITurnContext<IMessageActivity> turnContext, AssistantThread thread, ConversationData conversationData, CancellationToken cancellationToken)
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

                    // Add file upload notice to conversation history, frontend, and assistant
                    conversationData.AddTurn("user", $"File uploaded: {attachment.Name}");
                    await turnContext.SendActivityAsync(MessageFactory.Text($"File uploaded: {attachment.Name}"), cancellationToken);
                    await _assistantClient.CreateMessageAsync(thread, [$"File uploaded: {attachment.Name}"]);
                    // Ask whether to add file to a tool
                    await turnContext.SendActivityAsync(MessageFactory.SuggestedActions(
                        cardActions: new CardAction[]
                        {
                            new CardAction(title: "Code Interpreter", type: ActionTypes.ImBack, value: "#UPLOAD_FILE#code_interpreter#" + attachment.Name),
                            new CardAction( title: "File Search", type: ActionTypes.ImBack, value: "#UPLOAD_FILE#file_search#" + attachment.Name),
                            new CardAction( title: "Both", type: ActionTypes.ImBack, value: "#UPLOAD_FILE#code_interpreter,file_search#" + attachment.Name),
                        }, 
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

        private static List<string> ImageContentTypes = new() { "image/png", "image/jpeg", "image/jpg", "image/gif", "image/webp" };
        private static List<string> CodeInterpreterContentTypes = new() { "image/png", "image/jpeg", "image/jpg", "image/gif", "text/x-c", "text/x-csharp", "text/x-c++", "application/msword", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "text/html", "text/x-java", "application/json", "text/markdown", "application/pdf", "text/x-php", "application/vnd.openxmlformats-officedocument.presentationml.presentation", "text/x-python", "text/x-script.python", "text/x-ruby", "text/x-tex", "text/plain", "text/css", "text/javascript", "application/x-sh", "application/typescript", "application/csv", "application/x-tar", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/xml", "text/xml", "application/zip" };
        private static List<string> FileSearchContentTypes = new() { "text/x-c", "text/x-csharp", "text/x-c++", "application/msword", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "text/html", "text/x-java", "application/json", "text/markdown", "application/pdf", "text/x-php", "application/vnd.openxmlformats-officedocument.presentationml.presentation", "text/x-python", "text/x-script.python", "text/x-ruby", "text/x-tex", "text/plain", "text/css", "text/javascript", "application/x-sh", "application/typescript" };
    }
}