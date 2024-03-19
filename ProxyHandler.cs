using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Identity.Client;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace SymProxyCloud
{
    public class ProxyHandler
    {
        ProxyTokenCache AadTokenCache = new ProxyTokenCache();

        public async Task<bool> HandleRequestAsync(HttpListenerContext context, string symbolServerUrl, string blobConnectionString, string containerName, HttpClient httpClient, long? retryCount = null, bool? noisy = null)
        {
            string symbolPath = context.Request.Url.AbsolutePath.TrimStart('/');

            // Avoid searching for non-real symbols.
            if (symbolPath != "swagger.json" && !string.IsNullOrEmpty(symbolPath) && Path.GetFileName(symbolPath) != ("index2.txt") && Path.GetFileName(symbolPath) != "pingme.txt")
            {
                #region BlobSymbolServing
                if (!string.IsNullOrEmpty(blobConnectionString))
                {
                    BlobServiceClient blobServiceClient = new BlobServiceClient(blobConnectionString);
                    BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                    BlobClient blobClient = containerClient.GetBlobClient(symbolPath);

                    try
                    {
                        BlobProperties blobProperties = await blobClient.GetPropertiesAsync();

                        if (blobProperties.ContentLength > 0)
                        {
                            context.Response.ContentType = "application/octet-stream";
                            context.Response.ContentLength64 = blobProperties.ContentLength;
                            var response = await blobClient.DownloadStreamingAsync();
                            await response.Value.Content.CopyToAsync(context.Response.OutputStream);
                            await context.Response.OutputStream.DisposeAsync();

                            return true;
                        }
                    }
                    catch (RequestFailedException)
                    {
                        // Blob not found in Azure Blob Storage cache, continue to fetch from the symbol server
                    }
                }
                #endregion

                #region HttpSymbolServer
                try
                {
                    string symbolServerFilePath = symbolServerUrl + symbolPath;
                    HttpRequestMessage hrm = new HttpRequestMessage(HttpMethod.Get, symbolServerFilePath);

                    for (int i = 0; i < retryCount; i++)
                    {
                        using (var response = await httpClient.SendAsync(hrm, HttpCompletionOption.ResponseHeadersRead))
                        {
                            if (response != null && response.IsSuccessStatusCode)
                            {

                                long fileSizeBytes = response.Content.Headers.ContentLength ?? 0;

                                if (fileSizeBytes > 0)
                                {

                                    context.Response.ContentLength64 = fileSizeBytes;
                                    context.Response.ContentType = response.Content.Headers.ContentType.MediaType;
                                    context.Response.SendChunked = response.Headers.TransferEncodingChunked ?? false;

                                    using (var respStream = await response.Content.ReadAsStreamAsync())
                                    {
                                        if (respStream != null)
                                        {

                                            if (fileSizeBytes <= 2000000000)
                                            {
                                                using (var tempStream = new MemoryStream())
                                                {
                                                    await respStream.CopyToAsync(tempStream);
                                                    await respStream.FlushAsync();

                                                    tempStream.Position = 0;
                                                    await tempStream.CopyToAsync(context.Response.OutputStream);
                                                    await tempStream.FlushAsync();

                                                    if (!string.IsNullOrEmpty(blobConnectionString))
                                                    {
                                                        BlobServiceClient blobServiceClient = new BlobServiceClient(blobConnectionString);
                                                        BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                                                        BlobClient blobClient = containerClient.GetBlobClient(symbolPath);

                                                        if (await blobClient.ExistsAsync() == false)
                                                        {

                                                            tempStream.Position = 0;
                                                            if (noisy == true)
                                                            {
                                                                Console.WriteLine($"Cloud Caching {symbolPath}");
                                                            }

                                                            // Cache the symbol in blob storage
                                                            await blobClient.UploadAsync(tempStream);

                                                        }

                                                        return true;
                                                    }

                                                }
                                            }

                                            // MemoryStream Max Size == UInt32 max (2GB)
                                            // Don't cloud-cache if > 2GB, instead serve the symbol directly.
                                            await respStream.CopyToAsync(context.Response.OutputStream);
                                            context.Response.StatusCode = (int)response.StatusCode;
                                            await context.Response.OutputStream.DisposeAsync();

                                            return true;
                                        }
                                    }

                                    context.Response.StatusCode = (int)HttpStatusCode.InsufficientStorage;
                                    await context.Response.OutputStream.DisposeAsync();

                                    return false;
                                }

                                context.Response.StatusCode = (int)HttpStatusCode.LengthRequired;
                                await context.Response.OutputStream.DisposeAsync();

                                return false;
                            }

                        }
                    }
                }
                catch (Exception ex)
                {
                    if (noisy == true)
                    {
                        Console.WriteLine($"Error: {ex.Message}");
                    }
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

                    return false;
                }
                #endregion
            }

            return false;
        }

        /// <summary>
        /// Retrieves an access token as a Managed Identity if a ClientId & Audience are given, otherwise uses interactive credentials
        /// </summary>
        /// <param name="audience">Optional parameter for automation; Otherwise known as 'Token Audience'</param>
        /// <param name="clientId">Optional parameter for Managed Identity Authentication</param>
        /// <param name="authority">Optional parameter for the AAD authority you're authenticating with; Otherwise known as 'Instance'</param>
        /// <returns>A JWT Token</returns>
        public async Task<string> GetAccessToken(string tenantId, bool noisy, string audience = "", string clientId = "", string clientSecret = @"", string authority = @"https://login.microsoftonline.com/")
        {
            authority = $"https://login.microsoftonline.com/{tenantId}";

            if (AadTokenCache.GetTokenCount() > 0 && !string.IsNullOrEmpty(AadTokenCache.GetTokenFromStoreIfExists()))
            {
                return AadTokenCache.GetTokenFromStoreIfExists();
            }
            else
            {
                if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(audience) && !string.IsNullOrEmpty(clientSecret))
                {
                    IConfidentialClientApplication app = ConfidentialClientApplicationBuilder
                        .Create(clientId)
                        .WithClientSecret(clientSecret)
                        .WithTenantId(tenantId)
                        .WithAuthority(authority)
                        .Build();

                    try
                    {
                        string[] scopes = new string[] { audience + "/.default" };
                        AuthenticationResult result = await app.AcquireTokenForClient(scopes).ExecuteAsync();

                        string accessToken = result.AccessToken;

                        if (noisy == true)
                        {
                            Console.WriteLine("Access token acquired successfully.");
                        }

                        AadTokenCache.AddToken(result.AccessToken, result.ExpiresOn);

                        return result.AccessToken;
                    }
                    catch (Exception ex)
                    {
                        if (noisy == true)
                        {
                            Console.WriteLine("Error acquiring access token: " + ex.Message);
                        }
                    }
                }
                else
                {
                    var credential = new DefaultAzureCredential();
                    var tokenRequestContext = new TokenRequestContext(new[] { audience });
                    var token = await credential.GetTokenAsync(tokenRequestContext);

                    AadTokenCache.AddToken(token.Token, token.ExpiresOn);

                    if (noisy == true)
                    {
                        Console.WriteLine("Access token acquired successfully.");
                    }

                    return token.Token;
                }

                return string.Empty;
            }
        }

    }
}
