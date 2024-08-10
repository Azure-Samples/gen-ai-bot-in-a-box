// Sample code from: https://github.com/microsoft/BotFramework-WebChat

using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using OpenAI.Files;
using Services;

namespace TokenSampleApi.Controllers
{
    [ApiController]
    public class AOAIFileController : ControllerBase
    {
        private readonly FileClient _assistantClient;
        private readonly FileService _fileService;


        public AOAIFileController(IConfiguration configuration, AzureOpenAIClient aoaiClient, FileService fileService)
        {
            _assistantClient = aoaiClient.GetFileClient();
            _fileService = fileService;
        }

        [HttpGet]
        [Route("/api/files/{fileId}")]
        public async Task<IActionResult> GetFileContent(string fileId)
        {
            try
            {
                Stream fileStream = null;
                if (fileId.StartsWith("assistant"))
                {
                    var fileResponse = await _assistantClient.DownloadFileAsync(fileId);
                    fileStream = fileResponse.Value.ToStream();
                } else {
                    var queryString = Request.QueryString.ToString();
                    fileStream = await _fileService.DownloadFileAsync(fileId, queryString);
                }
                if (fileStream == null)
                {
                    return NotFound();
                }
                return Ok(fileStream);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}