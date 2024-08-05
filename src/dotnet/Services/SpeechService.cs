// Sample code from: https://github.com/microsoft/BotFramework-WebChat
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Models;
using System.Text.Json;

namespace Services
{
    public class SpeechService
    {
        private readonly string _region;
        private readonly TokenCredential _credential;

        public SpeechService(HttpClient httpClient, string uriBase, string region, TokenCredential credential)
        {
            httpClient.BaseAddress = new Uri(uriBase);
            _region = region;
            _credential = credential;
        }

        // Generates a new Direct Line token given the secret.
        // Provides user ID in the request body to bind the user ID to the token.
        public async Task<SpeechTokenDetails> GetTokenAsync(CancellationToken cancellationToken = default)
        {
            var token = await _credential.GetTokenAsync(new TokenRequestContext(["https://cognitiveservices.azure.com"]), cancellationToken);
            Console.WriteLine(JsonSerializer.Serialize(token));
            return new SpeechTokenDetails() {
                Token = token.Token
            };
        }
    }
}