// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.BotBuilderSamples
{
    public class StateManagementBot<T> : TeamsActivityHandler where T : Dialog
    {
        public readonly BotState _conversationState;
        public readonly BotState _userState;
        protected readonly Dialog _dialog;
        public string _systemMessage;
        public bool _sso_enabled;
        public string _sso_config_name;

        public StateManagementBot(IConfiguration config, ConversationState conversationState, UserState userState, T dialog)
        {
            _conversationState = conversationState;
            _userState = userState;
            _dialog = dialog;
            _sso_enabled = config.GetValue("SSO_ENABLED", false);
            _sso_config_name = config.GetValue("SSO_CONFIG_NAME", "default");
        }

        public override async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken = default(CancellationToken))
        {
            await base.OnTurnAsync(turnContext, cancellationToken);
            // Save any state changes that might have occurred during the turn.
            await _conversationState.SaveChangesAsync(turnContext, false, cancellationToken);
            await _userState.SaveChangesAsync(turnContext, false, cancellationToken);
        }

        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            await turnContext.SendActivityAsync("Welcome to State Bot Sample. Type anything to get started.");
        }

        protected async Task<bool> HandleLogin(ITurnContext turnContext, CancellationToken cancellationToken)
        {
            if (!_sso_enabled)
            {
                return true;
            }
            if (turnContext.Activity.Text == "logout") {
                await HandleLogout(turnContext, cancellationToken);
                return false;
            }

            var userStateAccessors = _userState.CreateProperty<UserProfile>(nameof(UserProfile));
            var userProfile = await userStateAccessors.GetAsync(turnContext, () => new UserProfile());

            var userTokenClient = turnContext.TurnState.Get<UserTokenClient>();

            TokenResponse userToken;
            try
            {
                userToken = await userTokenClient.GetUserTokenAsync(turnContext.Activity.From.Id, _sso_config_name, turnContext.Activity.ChannelId, null, cancellationToken);
                var tokenHandler = new JwtSecurityTokenHandler();
                var securityToken = tokenHandler.ReadToken(userToken.Token) as JwtSecurityToken;
                securityToken.Payload.TryGetValue("name", out var userName);
                userProfile.Name = userName as string;
                return true;
            }
            catch
            {
                await _dialog.RunAsync(turnContext, _conversationState.CreateProperty<DialogState>(nameof(DialogState)), cancellationToken);
                return false;
            }
        }

        protected async Task HandleLogout(ITurnContext turnContext, CancellationToken cancellationToken)
        {
            var userTokenClient = turnContext.TurnState.Get<UserTokenClient>();
            await userTokenClient.SignOutUserAsync(turnContext.Activity.From.Id, _sso_config_name, turnContext.Activity.ChannelId, cancellationToken).ConfigureAwait(false);
            await turnContext.SendActivityAsync("Signed out");
        }


        protected override async Task OnTeamsSigninVerifyStateAsync(ITurnContext<IInvokeActivity> turnContext, CancellationToken cancellationToken)
        {
            await _dialog.RunAsync(turnContext, _conversationState.CreateProperty<DialogState>(nameof(DialogState)), cancellationToken);
        }
    }
}