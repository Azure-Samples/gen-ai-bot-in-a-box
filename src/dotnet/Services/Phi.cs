using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenAI.Chat;

namespace Services
{
    public class Phi
    {
        private readonly string _deploymentEndpoint;
        private readonly string _deploymentKey;
        private static readonly HttpClient client = new HttpClient();

        public Phi(string deploymentEndpoint, string deploymentKey)
        {
            _deploymentEndpoint = deploymentEndpoint;
            _deploymentKey = deploymentKey;
        }

        public async Task<JObject> CreateCompletion(List<ChatMessage> messages)
        {
            try
            {
                var content = new Dictionary<string, object>() {
                { "messages", messages.Select(m => new {
                    role =
                        m is SystemChatMessage ? "system" :
                        m is AssistantChatMessage ? "assistant" :
                        "user",
                    content = m.Content[0].Text
                }).ToList() }
            };
                Console.WriteLine(JsonSerializer.Serialize(content));

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri(_deploymentEndpoint),
                    Headers =
                {
                    { "Authorization", $"Bearer {_deploymentKey}" },
                },
                    Content = new StringContent(JsonSerializer.Serialize(content), Encoding.UTF8, "application/json")
                };

                var response = await client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"HTTP error! status: {response.StatusCode}");
                }
                JObject json = JObject.Parse(await response.Content.ReadAsStringAsync());
                return json;

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CreateCompletion: {ex.Message}");
                throw;
            }
        }
    }
}