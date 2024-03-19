using Microsoft.Extensions.Configuration;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace SymProxyCloud
{
    // Some features of HttpListener such as MinSendBytesPerSecond aren't
    // available on all platforms.
    [SupportedOSPlatform("windows")]
    class Program
    {
        private static string symbolServerUri { get; set; }
        private static string localPort { get; set; }
        private static string aadClientId { get; set; }
        private static string aadClientSecret { get; set; }
        private static string tokenAudience { get; set; }
        private static string tenantId { get; set; }
        private static string blobConnectionString { get; set; }
        private static string blobContainerName { get; set; }
        private static long symDownloadRetryCount { get; set; }
        private static bool isNoisy { get; set; }

        private static HttpClient httpClient { get; set; }
        private static ProxyHandler proxyHandler = new ProxyHandler();

        static async Task<int> Main(string[] args)
        {
            // Add Configuration 
            IConfiguration configuration = new ConfigurationBuilder()
                .AddXmlFile("AppSettings.xml", optional: false, reloadOnChange: true)
                .Build();

            SetupEnvironment(configuration);

            var handler = new SocketsHttpHandler()
            {
                //https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-net-http-httpclient
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                MaxResponseDrainSize = int.MaxValue,
                KeepAlivePingTimeout = TimeSpan.FromMinutes(5),
                Expect100ContinueTimeout = TimeSpan.FromMinutes(5),
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5)
            };

            // Create the HTTP client
            httpClient = new HttpClient(handler) { BaseAddress = new Uri(symbolServerUri), };

            // Set auth header for Identity
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                await proxyHandler.GetAccessToken(tenantId, clientId: aadClientId, clientSecret: aadClientSecret, audience: tokenAudience, noisy: isNoisy));

            // Start the proxy server
            using (HttpListener listener = new HttpListener())
            {
                const string hostUriBaseString = @"http://localhost:"; // using HTTPS requires configuring a certificate
                string localHostString = $"{hostUriBaseString}{localPort}/";

                listener.TimeoutManager.MinSendBytesPerSecond = UInt32.MaxValue;
                listener.Prefixes.Add(localHostString); // Replace with your desired proxy server URL
                listener.Start();

                if (isNoisy == true)
                {
                    Console.WriteLine($"Local Symbol Proxy started at {localHostString}");
                }

                // Handle incoming requests
                while (true)
                {
                    HttpListenerContext context = await listener.GetContextAsync();

                    // A request shouldn't block the main thread from accepting
                    // other requests
                    ThreadPool.QueueUserWorkItem(async (_) =>
                    {
                        // If it's not a GET, ignore it.
                        if (context.Request.HttpMethod.ToUpperInvariant() == "GET")
                        {
                            var requestHandleSuccess = await proxyHandler.HandleRequestAsync(context, symbolServerUri, blobConnectionString, blobContainerName, httpClient, retryCount: symDownloadRetryCount, noisy: isNoisy);
                            if (isNoisy == true)
                            {
                                if (requestHandleSuccess == false)
                                {
                                    Console.WriteLine($"Symbol unavailable: {context.Request.Url.PathAndQuery}");
                                }
                                else
                                {
                                    Console.WriteLine($"Success: {context.Request.Url.PathAndQuery}");
                                }
                            }
                        }
                    });
                }
            }
        }

        static void SetupEnvironment(IConfiguration configuration)
        {
            try
            {
                // Grab Mandatory Settings
                symbolServerUri = configuration["section:MandatorySettings:key:SymbolServerURI"];
                if (!string.IsNullOrEmpty(symbolServerUri))
                {
                    if (!symbolServerUri.EndsWith('/'))
                    {
                        symbolServerUri += '/';
                    }
                }
                localPort = configuration["section:MandatorySettings:key:LocalPort"];
                if (long.TryParse(localPort, out var port) == false || port < 0)
                {
                    Console.WriteLine("Invalid Local Port. Defaulting to 5000");
                    localPort = "5000";
                }

                if (string.IsNullOrEmpty(symbolServerUri) || string.IsNullOrEmpty(localPort))
                {
                    Console.WriteLine("Required AppSettings empty. Symbol Server URI & Local Port are required values.");
                    //https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--0-499-
                    Environment.Exit(87);
                }

                // Optional Settings
                blobConnectionString = configuration["section:OptionalSettings:key:BlobConnectionString"];
                blobContainerName = configuration["section:OptionalSettings:key:BlobContainerName"];
                aadClientId = configuration["section:OptionalSettings:key:ClientId"];
                aadClientSecret = configuration["section:OptionalSettings:key:ClientSecret"];
                tokenAudience = configuration["section:OptionalSettings:key:TokenAudience"];
                tenantId = configuration["section:OptionalSettings:key:TenantId"];
                if (!string.IsNullOrEmpty(configuration["section:OptionalSettings:key:IsNoisy"]) &&
                    configuration["section:OptionalSettings:key:IsNoisy"].ToLowerInvariant() == "true")
                {
                    isNoisy = true;
                }
                if (!string.IsNullOrEmpty(configuration["section:OptionalSettings:key:SymDownloadRetryCount"]) &&
                                    long.TryParse(configuration["section:OptionalSettings:key:SymDownloadRetryCount"], out long retryCount) && retryCount > 0)
                {
                    symDownloadRetryCount = retryCount;
                }
                else
                {
                    Console.WriteLine("Invalid SymDownloadRetryCount value. Defaulting to 2.");
                    symDownloadRetryCount = 2;
                }
            }
            catch (Exception e)
            {
                // Unreadable parameter. Log it, and run as default.
                Console.WriteLine("Encountered an error when parsing AppSettings.");
                if (isNoisy)
                {
                    Console.WriteLine(e.ToString());
                }
            }

            if (string.IsNullOrEmpty(symbolServerUri) || string.IsNullOrEmpty(localPort))
            {
                Console.WriteLine("Required AppSettings parameters empty.");
                //https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--0-499-
                Environment.Exit(87);
            }
        }
    }
}
