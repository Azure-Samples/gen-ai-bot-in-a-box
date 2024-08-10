using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.BotBuilderSamples;
using Microsoft.Extensions.Configuration;

namespace GenAIBot.Bots
{
    public class TemplateBot<T> : StateManagementBot<T> where T : Dialog
    {
        private readonly string _instructions;
        private readonly string _welcomeMessage;
        public TemplateBot(IConfiguration config, ConversationState conversationState, UserState userState, T dialog)
            : base(config, conversationState, userState, dialog)
        {
            _instructions = config["LLM_INSTRUCTIONS"];
            _welcomeMessage = config.GetValue("LLM_WELCOME_MESSAGE", "Hello and welcome to the Template Bot Dotnet!");
            // Inject dependencies here
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

            // Catch any special messages here

            // Enforce login
            var loggedIn = await HandleLogin(turnContext, cancellationToken);
            if (!loggedIn)
            {
                return false;
            }
            
            // Add user message to history
            conversationData.AddTurn("user", turnContext.Activity.Text);

            // Run logic to obtain response here
            var response = "Hello, world!";

            // Add assistant message to history
            conversationData.AddTurn("assistant", response);
            
            // Respond back to user
            await turnContext.SendActivityAsync(MessageFactory.Text(response), cancellationToken);

            // Return true if bot should run next handlers
            return true;
        }
    }
}