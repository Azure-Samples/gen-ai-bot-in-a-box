using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.BotBuilderSamples;
using Microsoft.Extensions.Configuration;
using Services;

namespace GenAIBot.Bots
{
    public class PhiBot<T> : StateManagementBot<T> where T : Dialog
    {
        private readonly Phi _phiClient;
        private readonly string _instructions;
        public PhiBot(IConfiguration config, ConversationState conversationState, UserState userState, Phi phiClient, T dialog)
            : base(config, conversationState, userState, dialog)
        {
            _phiClient = phiClient;
            _instructions = config["LLM_INSTRUCTIONS"];
        }

        // Modify onMembersAdded as needed
        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            foreach (var member in membersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text("Hello and welcome to the Phi Bot Dotnet!"), cancellationToken);
                }
            }
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
            
            // Add user message to history
            conversationData.AddTurn("user", turnContext.Activity.Text);

            // Run logic to obtain response here
            var completion = await _phiClient.CreateCompletion(messages: conversationData.toMessages());
            var response = completion["choices"][0]["message"]["content"].ToString();

            // Add assistant message to history
            conversationData.AddTurn("assistant", response);
            
            // Respond back to user
            await turnContext.SendActivityAsync(MessageFactory.Text(response), cancellationToken);

            return true;
        }
    }
}