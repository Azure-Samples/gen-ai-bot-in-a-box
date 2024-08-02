// Sample code from: https://github.com/microsoft/BotFramework-WebChat

using System;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using OpenAI.Files;

namespace TokenSampleApi.Controllers
{
    [ApiController]
    public class AOAIFileController : ControllerBase
    {
        private readonly FileClient _assistantClient;


        public AOAIFileController(IConfiguration configuration, AzureOpenAIClient aoaiClient = null)
        {
            _assistantClient = aoaiClient.GetFileClient();
        }

        // Endpoint for generating a Direct Line token bound to a random user ID
        [HttpGet]
        [Route("/openai/files/{fileId}/content")]
        public async Task<IActionResult> Get(string fileId)
        {
            try
            {
                var fileResponse = await _assistantClient.DownloadFileAsync(fileId);
                return Ok(fileResponse.Value.ToStream());
            }
            catch (InvalidOperationException invalidOpException)
            {
                return BadRequest(new { message = invalidOpException.Message });
            }

        }
    }
}