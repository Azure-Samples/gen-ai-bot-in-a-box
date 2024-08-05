using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.BotBuilderSamples;
using Microsoft.Extensions.Configuration;
using OpenAI.Assistants;
using OpenAI.Files;

namespace GenAIBot.Bots
{
    public class AssistantBot : StateManagementBot
    {
        private readonly string _instructions;
        private readonly string _appUrl;
        private readonly AssistantClient _assistantClient;
        private readonly FileClient _fileClient;
        private readonly string _assistantId;

        public AssistantBot(IConfiguration config, ConversationState conversationState, UserState userState, AzureOpenAIClient aoaiClient)
            : base(conversationState, userState)
        {
            _assistantClient = aoaiClient.GetAssistantClient();
            _fileClient = aoaiClient.GetFileClient();
            _assistantId = config["AZURE_OPENAI_ASSISTANT_ID"];
            _instructions = config["LLM_INSTRUCTIONS"];
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
                    await turnContext.SendActivityAsync(MessageFactory.Text("Hello and welcome to the Assistant Bot Dotnet!"), cancellationToken);
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
            var filesUploaded = await HandleFileUploads(turnContext, thread, cancellationToken);

            // Return early if it is
            if (filesUploaded)
            {
                return;
            }

            // Add user message to history
            conversationData.AddTurn("user", turnContext.Activity.Text);

            // Send user message to thread
            _assistantClient.CreateMessage(thread, new List<MessageContent>() { MessageContent.FromText(turnContext.Activity.Text) });

            // Run thread
            var run = _assistantClient.CreateRun(
                conversationData.ThreadId,
                _assistantId,
                new RunCreationOptions()
                {
                    InstructionsOverride = _instructions
                }
            ).Value;

            // Stream mode is not supported in C# yet. Poll untill the run is completed.
            while (run.Status != RunStatus.Completed && run.Status != RunStatus.Failed)
            {
                run = _assistantClient.GetRun(conversationData.ThreadId, run.Id).Value;
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

        }
        protected async Task<bool> HandleFileUploads(ITurnContext<IMessageActivity> turnContext, AssistantThread thread, CancellationToken cancellationToken)
        {
            // Check if incoming message has attached files
            if (turnContext.Activity.Attachments != null)
            {
                foreach (var attachment in turnContext.Activity.Attachments)
                {
                    // Check if the attachment is an image
                    if (attachment.ContentType.Contains("image"))
                    {
                        // Not supported yet
                        await turnContext.SendActivityAsync(MessageFactory.Text("Image uploads are not supported yet."), cancellationToken);
                    }

                    // Get file contents as stream
                    // Upload file to Files API
                    using var client = new HttpClient();
                    using var response = await client.GetAsync(attachment.ContentUrl);
                    using var stream = response.Content.ReadAsStream();
                    var fileResponse = await _fileClient.UploadFileAsync(stream, attachment.Name, "assistants");

                    // Send the file to the assistant
                    var options = new MessageCreationOptions();
                    options.Attachments.Add(new MessageCreationAttachment(tools: [CodeInterpreterToolDefinition.CreateCodeInterpreter()], fileId: fileResponse.Value.Id));

                    var msg = await _assistantClient.CreateMessageAsync(
                        thread,
                        ["[attachment.Name]"],
                        options
                    );

                    // Send confirmation of file upload
                    await turnContext.SendActivityAsync(MessageFactory.Text("File uploaded successfully."), cancellationToken);

                }
            }
            return turnContext.Activity.Attachments != null;
        }
    }
}