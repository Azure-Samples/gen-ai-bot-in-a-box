// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Plugins;

namespace GenAIBot.Bots
{
    public class SemanticKernelBot<T> : StateManagementBot<T> where T : Dialog
    {
        private readonly string _instructions;
        private readonly string _welcomeMessage;
        private readonly string _endpoint;
        private readonly string _deployment;
        private readonly AzureSearchChatDataSource _chatDataSource;
        private readonly TokenCredential _credential;

        public SemanticKernelBot(IConfiguration config, ConversationState conversationState, UserState userState, AzureOpenAIClient aoaiClient, AzureSearchChatDataSource chatDataSource, T dialog)
            : base(config, conversationState, userState, dialog)
        {
            _instructions = config["LLM_INSTRUCTIONS"];
            _welcomeMessage = config.GetValue("LLM_WELCOME_MESSAGE", "Hello and welcome to the Semantic Kernel Bot Dotnet!");
            _endpoint = config["AZURE_OPENAI_API_ENDPOINT"];
            _deployment = config["AZURE_OPENAI_DEPLOYMENT_NAME"];
            _credential = new DefaultAzureCredential();
            _chatDataSource = chatDataSource;
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
            // Log in at the start of the conversation
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

            // Add user message to history
            conversationData.AddTurn("user", turnContext.Activity.Text);

            var kernel = Kernel.CreateBuilder()
                .AddAzureOpenAIChatCompletion(_deployment, _endpoint, _credential)
                .Build();

            // Add custom plugins
            kernel.Plugins.Add(kernel.CreatePluginFromObject(new WikipediaPlugin(conversationData, turnContext)));

            var promptExecutionSettings = new AzureOpenAIPromptExecutionSettings()
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
            };

            var history = new ChatHistory();
            foreach (var turn in conversationData.History)
            {
                if (turn.Role == "user")
                {
                    history.AddUserMessage(turn.Message);
                }
                else
                {
                    history.AddAssistantMessage(turn.Message);
                }
            }

            var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

            var answer = await chatCompletionService.GetChatMessageContentAsync(history, promptExecutionSettings, kernel);

            var response = answer.ToString();

            // Add assistant message to history
            conversationData.AddTurn("assistant", response);

            // Respond back to user
            await turnContext.SendActivityAsync(MessageFactory.Text(response), cancellationToken);

            // Return true if bot should run next handlers
            return true;
        }
    }
}