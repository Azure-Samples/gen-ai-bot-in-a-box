using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.BotBuilderSamples;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using System.Linq;
using AdaptiveCards;
using OpenAI.Chat;
using Azure.AI.OpenAI.Chat;
using System.Text.Json;

namespace GenAIBot.Bots
{


    public class PhiBot : StateManagementBot
    {
        private readonly Phi _phiClient;
        private readonly string _instructions;

        public PhiBot(IConfiguration config, ConversationState conversationState, UserState userState, Phi phiClient)
            : base(conversationState, userState)
        {
            _phiClient = phiClient;
            _instructions = config["LLM_INSTRUCTIONS"];
        }

        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            foreach (var member in membersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text("Hello and welcome to the Phi Bot!"), cancellationToken);
                }
            }
        }

        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
            var conversationData = await conversationStateAccessors.GetAsync(
                turnContext, () => new ConversationData()
                {
                    History = new List<ConversationTurn>() {
                        new() { Role = "system", Message = _instructions }
                    }
                });

            var userStateAccessors = _userState.CreateProperty<UserProfile>(nameof(UserProfile));
            var userProfile = await userStateAccessors.GetAsync(turnContext, () => new UserProfile());

            conversationData.AddTurn("user", turnContext.Activity.Text);

            var completion = await _phiClient.CreateCompletion(messages: conversationData.toMessages());

            var response = completion["choices"][0]["message"]["content"].ToString();

            conversationData.AddTurn("assistant", response);
            await turnContext.SendActivityAsync(MessageFactory.Text(response), cancellationToken);

        }
    }
}