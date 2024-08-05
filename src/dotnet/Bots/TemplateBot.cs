using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.BotBuilderSamples;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;

namespace GenAIBot.Bots
{
    public class TemplateBot : StateManagementBot
    {
        private readonly string _instructions;
        public TemplateBot(IConfiguration config, ConversationState conversationState, UserState userState)
            : base(conversationState, userState)
        {
            _instructions = config["LLM_INSTRUCTIONS"];
            // Inject dependencies here
        }

        // Modify onMembersAdded as needed
        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            foreach (var member in membersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text("Hello and welcome to the Template Bot Dotnet!"), cancellationToken);
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

            // Catch any special messages here
            
            // Add user message to history
            conversationData.AddTurn("user", turnContext.Activity.Text);

            // Run logic to obtain response here
            var response = "Hello, world!";

            // Add assistant message to history
            conversationData.AddTurn("assistant", response);
            
            // Respond back to user
            await turnContext.SendActivityAsync(MessageFactory.Text(response), cancellationToken);
        }
    }
}