// Sample code from: https://github.com/microsoft/BotFramework-WebChat
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;

namespace Services
{
    public class FileService
    {
        private readonly BlobContainerClient _blobContainerClient;
        private readonly HttpClient _httpClient;
        public FileService(IConfiguration configuration, BlobContainerClient blobContainerClient, HttpClient httpClient)
        {
            _blobContainerClient = blobContainerClient;
            _httpClient = httpClient;

            try
            {
                _blobContainerClient
                    .GetParentBlobServiceClient()
                    .CreateBlobContainer(configuration.GetValue<string>("AZURE_STORAGE_BLOB_CONTAINER"));
            }
            catch
            {
                Console.WriteLine("Failed to create blob container. It may already exist.");
            }
        }

        // Uploads a file to the blob storage and returns a SAS URI
        public async Task<string> UploadTempFile(string name, Stream content, string appUrl)
        {
            // Upload file to storage
            await _blobContainerClient.UploadBlobAsync(name, content);
            BlobClient blobClient = _blobContainerClient.GetBlobClient(name);
            // Set expiration to 1 day
            blobClient.SetMetadata(metadata: new Dictionary<string, string>(){{"RetentionDays", "1"}});
            // Create a User-Delegation SAS Token (also valid for 1 day)
            UserDelegationKey userDelegationKey =
            await _blobContainerClient
                .GetParentBlobServiceClient()
                .GetUserDelegationKeyAsync(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));

            // Build a SAS URI
            BlobSasBuilder sasBuilder = new BlobSasBuilder()
            {
                BlobContainerName = blobClient.BlobContainerName,
                BlobName = blobClient.Name,
                Resource = "b",
                StartsOn = DateTimeOffset.UtcNow,
                ExpiresOn = DateTimeOffset.UtcNow.AddDays(1)
            };

            // Specify the necessary permissions
            sasBuilder.SetPermissions(BlobSasPermissions.Read | BlobSasPermissions.Write);

            // Add the SAS token to the blob URI
            BlobUriBuilder uriBuilder = new BlobUriBuilder(new Uri($"{appUrl}/api/files/{name}"))
            {
                // Specify the user delegation key
                Sas = sasBuilder.ToSasQueryParameters(
                    userDelegationKey,
                    _blobContainerClient.AccountName
                )
            };

            return uriBuilder.ToUri().ToString();
        }

        public async Task<Stream> DownloadFileAsync(string fileId, string sasToken)
        {
            try
            {
                _blobContainerClient.GetBlobClient(fileId);
                var foo = $"{_blobContainerClient.Uri}/{fileId}{sasToken}";
                var response = await _httpClient.GetAsync(
                    $"{_blobContainerClient.Uri}/{fileId}{sasToken}"
                );

                response.EnsureSuccessStatusCode(); // Ensure the response is successful

                return response.Content.ReadAsStream();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to download file: {ex.Message}");
                return null; // or handle the error in an appropriate way
            }
        }

    }
}