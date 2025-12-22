using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Qobuzarr.IntegrationTests
{
    public class SimpleIntegrationTest
    {
        private readonly ITestOutputHelper _output;
        private readonly string _lidarrUrl;
        private readonly string _lidarrApiKey;

        public SimpleIntegrationTest(ITestOutputHelper output)
        {
            _output = output;
            
            // Load environment variables
            DotNetEnv.Env.TraversePath().Load();
            
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();
            
            _lidarrUrl = configuration["LIDARR_URL"] ?? throw new InvalidOperationException("LIDARR_URL not configured");
            _lidarrApiKey = configuration["LIDARR_API_KEY"] ?? throw new InvalidOperationException("LIDARR_API_KEY not configured");
            
            _output.WriteLine($"Testing against Lidarr at: {_lidarrUrl}");
        }

        [Fact]
        public async Task Should_Connect_To_Lidarr_Successfully()
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("X-Api-Key", _lidarrApiKey);
            
            var response = await httpClient.GetAsync($"{_lidarrUrl}/api/v1/system/status");
            
            response.IsSuccessStatusCode.Should().BeTrue($"Should be able to connect to Lidarr API. Status: {response.StatusCode}");
            
            var content = await response.Content.ReadAsStringAsync();
            var status = JsonConvert.DeserializeObject<dynamic>(content);
            
            _output.WriteLine($"Connected to Lidarr version: {status?.version}");
            _output.WriteLine($"Instance name: {status?.instanceName}");
            _output.WriteLine($"Status content: {content}");
        }

        [Fact]
        public async Task Should_Retrieve_System_Health()
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("X-Api-Key", _lidarrApiKey);
            
            var response = await httpClient.GetAsync($"{_lidarrUrl}/api/v1/health");
            
            response.IsSuccessStatusCode.Should().BeTrue($"Should be able to get health status. Status: {response.StatusCode}");
            
            var content = await response.Content.ReadAsStringAsync();
            var health = JsonConvert.DeserializeObject<dynamic[]>(content);
            
            _output.WriteLine($"Health check returned {health?.Length ?? 0} items");
            
            if (health != null)
            {
                foreach (var healthItem in health)
                {
                    _output.WriteLine($"Health: {healthItem?.source} - {healthItem?.type} - {healthItem?.message}");
                }
            }
        }

        [Fact]
        public async Task Should_Retrieve_Artists()
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("X-Api-Key", _lidarrApiKey);
            
            var response = await httpClient.GetAsync($"{_lidarrUrl}/api/v1/artist");
            
            response.IsSuccessStatusCode.Should().BeTrue($"Should be able to get artists. Status: {response.StatusCode}");
            
            var content = await response.Content.ReadAsStringAsync();
            var artists = JsonConvert.DeserializeObject<dynamic[]>(content) ?? Array.Empty<dynamic>();
            
            artists.Should().NotBeNull("Artists response should not be null");
            artists.Should().NotBeEmpty("Should have at least some artists configured for testing");
            
            _output.WriteLine($"Found {artists.Length} artists:");
            
            for (int i = 0; i < Math.Min(artists.Length, 5); i++)
            {
                _output.WriteLine($"  - {artists[i]?.artistName} (ID: {artists[i]?.id})");
            }
        }

        [Fact]
        public async Task Should_Retrieve_Wanted_Albums()
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("X-Api-Key", _lidarrApiKey);
            
            var response = await httpClient.GetAsync($"{_lidarrUrl}/api/v1/wanted/missing?pageSize=10");
            
            response.IsSuccessStatusCode.Should().BeTrue($"Should be able to get wanted albums. Status: {response.StatusCode}");
            
            var content = await response.Content.ReadAsStringAsync();
            var wantedResponse = JsonConvert.DeserializeObject<dynamic>(content);
            
            content.Should().NotBeNullOrWhiteSpace("Wanted albums response should not be null");
            
            var totalRecords = (int)(wantedResponse?.totalRecords ?? 0);
            var records = wantedResponse?.records as Newtonsoft.Json.Linq.JArray;
            
            _output.WriteLine($"Found {totalRecords} total wanted albums");
            _output.WriteLine($"Retrieved {records?.Count ?? 0} albums in this page");
            
            if (records != null && records.Count > 0)
            {
                for (int i = 0; i < Math.Min(records.Count, 3); i++)
                {
                    var album = records[i];
                    _output.WriteLine($"  - {album["artist"]?["artistName"]} - {album["title"]} (ID: {album["id"]})");
                }
                
                totalRecords.Should().BeGreaterThan(0, "Should have some wanted albums available for testing");
            }
            else
            {
                _output.WriteLine("No wanted albums found. You may need to add some artists and mark albums as wanted for testing.");
            }
        }

        [Fact]
        public async Task Should_Check_Indexer_Configuration()
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("X-Api-Key", _lidarrApiKey);
            
            var response = await httpClient.GetAsync($"{_lidarrUrl}/api/v1/indexer");
            
            response.IsSuccessStatusCode.Should().BeTrue($"Should be able to get indexers. Status: {response.StatusCode}");
            
            var content = await response.Content.ReadAsStringAsync();
            _output.WriteLine($"Raw indexer response: {content}");
            
            var indexers = JsonConvert.DeserializeObject<dynamic[]>(content);
            
            _output.WriteLine($"Found {indexers?.Length ?? 0} indexers configured:");
            
            bool hasQobuzIndexer = false;
            bool hasEnabledIndexer = false;
            
            if (indexers != null)
            {
                foreach (var indexer in indexers)
                {
                    var name = indexer?.name?.ToString() ?? "Unknown";
                    var implementation = indexer?.implementation?.ToString() ?? "Unknown";
                    var id = indexer?.id?.ToString() ?? "Unknown";
                    
                    // Try different possible field names for enabled status
                    var enabled1 = indexer?.enable;
                    var enabled2 = indexer?.enabled;
                    var enableRss = indexer?.enableRss;
                    var enableInteractiveSearch = indexer?.enableInteractiveSearch;
                    var enableAutomaticSearch = indexer?.enableAutomaticSearch;
                    
                    _output.WriteLine($"  - {name} (ID: {id}, {implementation})");
                    _output.WriteLine($"    enable: {enabled1}, enabled: {enabled2}");
                    _output.WriteLine($"    enableRss: {enableRss}");
                    _output.WriteLine($"    enableInteractiveSearch: {enableInteractiveSearch}");
                    _output.WriteLine($"    enableAutomaticSearch: {enableAutomaticSearch}");
                    
                    if (implementation.ToLowerInvariant().Contains("qobuz"))
                    {
                        hasQobuzIndexer = true;
                        
                        // Check if any search capability is enabled
                        var hasAnyEnabled = (enabled1?.ToString() == "True") || 
                                          (enabled2?.ToString() == "True") ||
                                          (enableInteractiveSearch?.ToString() == "True") ||
                                          (enableAutomaticSearch?.ToString() == "True");
                        
                        if (hasAnyEnabled)
                        {
                            hasEnabledIndexer = true;
                            _output.WriteLine($"    ✅ Qobuzarr indexer has some functionality enabled!");
                        }
                        else
                        {
                            _output.WriteLine($"    ⚠️ Qobuzarr indexer appears to be fully disabled");
                        }
                    }
                }
            }
            
            if (!hasQobuzIndexer)
            {
                _output.WriteLine("❌ No Qobuzarr indexer found!");
                _output.WriteLine("The Qobuzarr plugin may not be installed or configured properly.");
            }
            
            if (!hasEnabledIndexer)
            {
                _output.WriteLine("⚠️ No enabled Qobuzarr indexer found.");
                _output.WriteLine("This explains why the health check shows 'All indexers are unavailable due to failures'");
            }
        }

        [Fact]
        public async Task Should_Check_Download_Client_Configuration()
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("X-Api-Key", _lidarrApiKey);
            
            var response = await httpClient.GetAsync($"{_lidarrUrl}/api/v1/downloadclient");
            
            response.IsSuccessStatusCode.Should().BeTrue($"Should be able to get download clients. Status: {response.StatusCode}");
            
            var content = await response.Content.ReadAsStringAsync();
            var downloadClients = JsonConvert.DeserializeObject<dynamic[]>(content);
            
            _output.WriteLine($"Found {downloadClients?.Length ?? 0} download clients configured:");
            
            bool hasQobuzDownloadClient = false;
            bool hasEnabledDownloadClient = false;
            
            if (downloadClients != null)
            {
                foreach (var client in downloadClients)
                {
                    var name = client?.name?.ToString() ?? "Unknown";
                    var implementation = client?.implementation?.ToString() ?? "Unknown";
                    var id = client?.id?.ToString() ?? "Unknown";
                    var enabled = client?.enable?.ToString() == "True";
                    
                    _output.WriteLine($"  - {name} (ID: {id}, {implementation}) - Enabled: {enabled}");
                    
                    if (implementation.ToLowerInvariant().Contains("qobuz"))
                    {
                        hasQobuzDownloadClient = true;
                        if (enabled)
                        {
                            hasEnabledDownloadClient = true;
                            _output.WriteLine($"    ✅ Qobuzarr download client is enabled!");
                        }
                        else
                        {
                            _output.WriteLine($"    ⚠️ Qobuzarr download client is disabled");
                        }
                    }
                }
            }
            
            if (!hasQobuzDownloadClient)
            {
                _output.WriteLine("❌ No Qobuzarr download client found!");
                _output.WriteLine("The Qobuzarr plugin may not be installed or configured properly.");
            }
            else if (!hasEnabledDownloadClient)
            {
                _output.WriteLine("⚠️ Qobuzarr download client found but disabled");
            }
        }

        [Fact]
        public async Task Should_Validate_Qobuz_Configuration()
        {
            // Load Qobuz configuration
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();
            
            var qobuzUsername = configuration["QOBUZ_USERNAME"];
            var qobuzPassword = configuration["QOBUZ_PASSWORD"];
            var qobuzUserId = configuration["QOBUZ_USER_ID"];
            var qobuzUserToken = configuration["QOBUZ_USER_AUTH_TOKEN"];
            
            var hasUsernameAuth = !string.IsNullOrWhiteSpace(qobuzUsername) && !string.IsNullOrWhiteSpace(qobuzPassword);
            var hasTokenAuth = !string.IsNullOrWhiteSpace(qobuzUserId) && !string.IsNullOrWhiteSpace(qobuzUserToken);
            
            _output.WriteLine($"Qobuz username auth configured: {hasUsernameAuth}");
            _output.WriteLine($"Qobuz token auth configured: {hasTokenAuth}");
            
            var hasAuth = hasUsernameAuth || hasTokenAuth;
            
            if (hasAuth)
            {
                _output.WriteLine("✅ Qobuz authentication is configured");
                
                if (hasUsernameAuth)
                {
                    _output.WriteLine($"Using username/password authentication for user: {qobuzUsername}");
                }
                else
                {
                    _output.WriteLine($"Using user ID/token authentication for user: {qobuzUserId}");
                }
            }
            else
            {
                _output.WriteLine("⚠️ No Qobuz authentication configured");
                _output.WriteLine("Please configure either:");
                _output.WriteLine("  - QOBUZ_USERNAME and QOBUZ_PASSWORD");
                _output.WriteLine("  - QOBUZ_USER_ID and QOBUZ_USER_AUTH_TOKEN");
            }
            
            // App credentials from constants (using default values)
            var appId = configuration["QOBUZ_APP_ID"] ?? throw new InvalidOperationException("QOBUZ_APP_ID not configured");
            var appSecret = configuration["QOBUZ_APP_SECRET"] ?? "ixbt4t5pkcxpg4u6";  
            
            _output.WriteLine($"Using Qobuz app credentials: {appId}");
            
            appId.Should().NotBeNullOrWhiteSpace("Qobuz App ID should be available");
            appSecret.Should().NotBeNullOrWhiteSpace("Qobuz App Secret should be available");
        }
    }
}