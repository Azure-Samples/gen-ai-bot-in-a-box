// Generated with EchoBot .NET Template version v4.22.0

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.BotBuilderSamples;
using Microsoft.Extensions.Configuration;
using OpenAI.Assistants;

namespace GenAIBot.Bots
{
    public class AssistantBot : StateManagementBot
    {
        private readonly AssistantClient _assistantClient;
        private readonly string _assistantId;
        private readonly string _instructions;

        public AssistantBot(IConfiguration config, ConversationState conversationState, UserState userState, AzureOpenAIClient aoaiClient)
            : base(conversationState, userState)
        {
            _assistantClient = aoaiClient.GetAssistantClient();
            _assistantId = config["AZURE_OPENAI_ASSISTANT_ID"];
            _instructions = config["LLM_INSTRUCTIONS"];
        }

        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
            var conversationData = await conversationStateAccessors.GetAsync(turnContext, () => new ConversationData());

            var userStateAccessors = _userState.CreateProperty<UserProfile>(nameof(UserProfile));
            var userProfile = await userStateAccessors.GetAsync(turnContext, () => new UserProfile());

            conversationData.AddTurn("user", turnContext.Activity.Text);

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

            _assistantClient.CreateMessage(thread, new List<MessageContent>() { MessageContent.FromText(turnContext.Activity.Text) });

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
                        conversationData.AddTurn("assistant", item.Text);
                        await turnContext.SendActivityAsync(MessageFactory.Text(item.Text), cancellationToken);
                    }
                    else if (!string.IsNullOrEmpty(item.ImageFileId))
                    {
                        Console.WriteLine(item.ImageFileId);
                    }
                }
            }

        }

        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            var welcomeText = "Hello and welcome to the Assistant Bot!";
            foreach (var member in membersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text(welcomeText, welcomeText), cancellationToken);
                }
            }
        }
    }
}
