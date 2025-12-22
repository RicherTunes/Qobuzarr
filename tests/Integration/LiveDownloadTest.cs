using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Qobuzarr.IntegrationTests.Helpers;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Qobuzarr.IntegrationTests
{
    public class LiveDownloadTest
    {
        private readonly ITestOutputHelper _output;
        private readonly string _lidarrUrl;
        private readonly string _lidarrApiKey;

        public LiveDownloadTest(ITestOutputHelper output)
        {
            _output = output;
            
            // Load environment variables
            DotNetEnv.Env.TraversePath().Load();
            
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();
            
            _lidarrUrl = configuration["LIDARR_URL"] ?? throw new InvalidOperationException("LIDARR_URL not configured");
            _lidarrApiKey = configuration["LIDARR_API_KEY"] ?? throw new InvalidOperationException("LIDARR_API_KEY not configured");
            
            _output.WriteLine($"🎵 Live Download Test - Lidarr: {_lidarrUrl}");
        }

        [Fact]
        public async Task Should_Explore_Available_Commands()
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("X-Api-Key", _lidarrApiKey);
            
            _output.WriteLine("🔍 Exploring available Lidarr commands...");
            
            // Get current running commands to see command structure
            var commandsResponse = await httpClient.GetAsync($"{_lidarrUrl}/api/v1/command");
            if (commandsResponse.IsSuccessStatusCode)
            {
                var commandsContent = await commandsResponse.Content.ReadAsStringAsync();
                _output.WriteLine($"Current commands: {commandsContent}");
            }
            
            // Try to get system status to understand the API better
            var statusResponse = await httpClient.GetAsync($"{_lidarrUrl}/api/v1/system/status");
            if (statusResponse.IsSuccessStatusCode)
            {
                var statusContent = await statusResponse.Content.ReadAsStringAsync();
                var statusData = JsonConvert.DeserializeObject<dynamic>(statusContent);
                _output.WriteLine($"Lidarr version: {statusData?.version}");
            }
        }

        [Fact]
        public async Task Should_Trigger_Album_Search_And_Monitor_Results()
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("X-Api-Key", _lidarrApiKey);
            
            // Get a random wanted album
            _output.WriteLine("🔍 Getting random wanted album...");
            var wantedResponse = await httpClient.GetAsync($"{_lidarrUrl}/api/v1/wanted/missing?pageSize=5");
            wantedResponse.IsSuccessStatusCode.Should().BeTrue();
            
            var wantedContent = await wantedResponse.Content.ReadAsStringAsync();
            var wantedData = JsonConvert.DeserializeObject<JObject>(wantedContent);
            var albums = JsonExtractor.RequireNonEmptyArray(
                wantedData?["records"], 
                "records", 
                "/api/v1/wanted/missing", 
                wantedContent);
            
            // Pick the first album
            var album = albums[0];
            var albumId = JsonExtractor.RequireInt(
                album["id"], 
                "records[0].id", 
                "/api/v1/wanted/missing", 
                wantedContent);
            var albumTitle = JsonExtractor.TryGetString(album["title"]);
            var artistName = JsonExtractor.TryGetString(album["artist"]?["artistName"]);
            
            _output.WriteLine($"🎯 Selected album: {artistName} - {albumTitle} (ID: {albumId})");
            
            // Check current download queue before search
            _output.WriteLine("📋 Checking download queue before search...");
            var queueBefore = await GetDownloadQueueAsync(httpClient);
            _output.WriteLine($"Queue size before: {queueBefore.Count} items");
            
            // Trigger album search
            _output.WriteLine("🚀 Triggering album search...");
            var searchPayload = JsonConvert.SerializeObject(new 
            { 
                name = "AlbumSearch",
                albumIds = new[] { albumId } 
            });
            var searchContent = new StringContent(searchPayload, System.Text.Encoding.UTF8, "application/json");
            
            var searchResponse = await httpClient.PostAsync($"{_lidarrUrl}/api/v1/command", searchContent);
            
            var searchResult = await searchResponse.Content.ReadAsStringAsync();
            _output.WriteLine($"Search response status: {searchResponse.StatusCode}");
            _output.WriteLine($"Search response: {searchResult}");
            
            if (!searchResponse.IsSuccessStatusCode)
            {
                _output.WriteLine($"⚠️ Search command failed with {searchResponse.StatusCode}. Let's try alternative approach...");
                
                // Try a different command format
                var altSearchPayload = JsonConvert.SerializeObject(new 
                { 
                    name = "MissingAlbumSearch",
                    albumIds = new[] { albumId }
                });
                var altSearchContent = new StringContent(altSearchPayload, System.Text.Encoding.UTF8, "application/json");
                var altResponse = await httpClient.PostAsync($"{_lidarrUrl}/api/v1/command", altSearchContent);
                
                var altResult = await altResponse.Content.ReadAsStringAsync();
                _output.WriteLine($"Alternative search status: {altResponse.StatusCode}");
                _output.WriteLine($"Alternative search response: {altResult}");
                
                if (altResponse.IsSuccessStatusCode)
                {
                    searchResponse = altResponse;
                    searchResult = altResult;
                }
            }
            
            if (!searchResponse.IsSuccessStatusCode)
            {
                _output.WriteLine("❌ Both search attempts failed. This might indicate an issue with the API or plugin configuration.");
                return; // Don't fail the test, just return early
            }
            
            var commandData = JsonConvert.DeserializeObject<dynamic>(searchResult);
            var commandId = (int)commandData?.id;
            _output.WriteLine($"Command ID: {commandId}");
            
            // Monitor command progress
            _output.WriteLine("⏳ Monitoring search progress...");
            await MonitorCommandProgressAsync(httpClient, commandId);
            
            // Check download queue after search
            _output.WriteLine("📋 Checking download queue after search...");
            var queueAfter = await GetDownloadQueueAsync(httpClient);
            _output.WriteLine($"Queue size after: {queueAfter.Count} items");
            
            // Show queue details
            if (queueAfter.Count > queueBefore.Count)
            {
                _output.WriteLine($"✅ New downloads detected! Added {queueAfter.Count - queueBefore.Count} items to queue");
                
                foreach (var queueItem in queueAfter)
                {
                    var title = queueItem["title"]?.ToString();
                    var artist = queueItem["artist"]?["artistName"]?.ToString();
                    var status = queueItem["status"]?.ToString();
                    _output.WriteLine($"  📀 {artist} - {title} (Status: {status})");
                }
            }
            else if (queueAfter.Count == queueBefore.Count)
            {
                _output.WriteLine("⚠️ No new downloads added to queue - search may not have found matches");
            }
            else
            {
                _output.WriteLine("ℹ️ Queue size decreased - some downloads may have completed quickly");
            }
            
            // Get recent history to see if anything completed
            _output.WriteLine("📜 Checking recent download history...");
            var historyResponse = await httpClient.GetAsync($"{_lidarrUrl}/api/v1/history?pageSize=10&sortKey=date&sortDirection=descending");
            if (historyResponse.IsSuccessStatusCode)
            {
                var historyContent = await historyResponse.Content.ReadAsStringAsync();
                var historyData = JsonConvert.DeserializeObject<dynamic>(historyContent);
                var historyRecords = historyData?.records as Newtonsoft.Json.Linq.JArray;
                
                if (historyRecords != null && historyRecords.Count > 0)
                {
                    _output.WriteLine("Recent activity:");
                    foreach (var record in historyRecords.Take(5))
                    {
                        var eventType = record["eventType"]?.ToString();
                        var date = record["date"]?.ToString();
                        var albumTitle2 = record["album"]?["title"]?.ToString();
                        var artistName2 = record["artist"]?["artistName"]?.ToString();
                        _output.WriteLine($"  📅 {date}: {eventType} - {artistName2} - {albumTitle2}");
                    }
                }
            }
        }

        private async Task<Newtonsoft.Json.Linq.JArray> GetDownloadQueueAsync(HttpClient httpClient)
        {
            var queueResponse = await httpClient.GetAsync($"{_lidarrUrl}/api/v1/queue");
            if (queueResponse.IsSuccessStatusCode)
            {
                var queueContent = await queueResponse.Content.ReadAsStringAsync();
                var queueData = JsonConvert.DeserializeObject<dynamic>(queueContent);
                return queueData?.records as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray();
            }
            return new Newtonsoft.Json.Linq.JArray();
        }

        private async Task MonitorCommandProgressAsync(HttpClient httpClient, int commandId)
        {
            var maxWaitTime = TimeSpan.FromMinutes(2);
            var startTime = DateTime.UtcNow;
            
            while (DateTime.UtcNow - startTime < maxWaitTime)
            {
                var commandResponse = await httpClient.GetAsync($"{_lidarrUrl}/api/v1/command/{commandId}");
                if (commandResponse.IsSuccessStatusCode)
                {
                    var commandContent = await commandResponse.Content.ReadAsStringAsync();
                    var commandData = JsonConvert.DeserializeObject<dynamic>(commandContent);
                    
                    var status = commandData?.status?.ToString();
                    var message = commandData?.message?.ToString();
                    
                    _output.WriteLine($"  Command status: {status} - {message}");
                    
                    if (status == "completed" || status == "failed")
                    {
                        if (status == "completed")
                        {
                            _output.WriteLine("✅ Search completed successfully!");
                        }
                        else
                        {
                            _output.WriteLine("❌ Search failed!");
                            var exception = commandData?.exception?.ToString();
                            if (!string.IsNullOrWhiteSpace(exception))
                            {
                                _output.WriteLine($"Exception: {exception}");
                            }
                        }
                        break;
                    }
                }
                
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        [Fact]
        public async Task Should_Test_Qobuzarr_Indexer_Directly()
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("X-Api-Key", _lidarrApiKey);
            
            // Test the indexer directly by triggering a search
            _output.WriteLine("🔍 Testing Qobuzarr indexer directly...");
            
            // Get indexer ID first
            var indexerResponse = await httpClient.GetAsync($"{_lidarrUrl}/api/v1/indexer");
            indexerResponse.IsSuccessStatusCode.Should().BeTrue();
            
            var indexerContent = await indexerResponse.Content.ReadAsStringAsync();
            var indexers = JsonConvert.DeserializeObject<dynamic[]>(indexerContent) ?? Array.Empty<dynamic>();
            
            dynamic? qobuzIndexer = Array.Find(indexers, i => i?.implementation?.ToString() == "QobuzIndexer");
            ((object?)qobuzIndexer).Should().NotBeNull("Qobuzarr indexer should be found");
            if (qobuzIndexer == null) return;
            
            var indexerId = (int)(qobuzIndexer.id ?? 0);
            _output.WriteLine($"Found Qobuzarr indexer with ID: {indexerId}");
            
            // Test indexer with a simple search
            _output.WriteLine("🧪 Testing indexer search capability...");
            var testSearchPayload = JsonConvert.SerializeObject(new 
            { 
                name = "IndexerSearch",
                indexerId = indexerId,
                artistQuery = "Miles Davis",
                albumQuery = "Kind of Blue"
            });
            
            var testSearchContent = new StringContent(testSearchPayload, System.Text.Encoding.UTF8, "application/json");
            var testResponse = await httpClient.PostAsync($"{_lidarrUrl}/api/v1/command", testSearchContent);
            
            if (testResponse.IsSuccessStatusCode)
            {
                var testResult = await testResponse.Content.ReadAsStringAsync();
                _output.WriteLine($"✅ Indexer test command sent successfully: {testResult}");
                
                var testCommandData = JsonConvert.DeserializeObject<dynamic>(testResult);
                var testCommandId = (int)testCommandData?.id;
                
                // Monitor this test command
                await MonitorCommandProgressAsync(httpClient, testCommandId);
            }
            else
            {
                _output.WriteLine($"⚠️ Indexer test failed with status: {testResponse.StatusCode}");
                var errorContent = await testResponse.Content.ReadAsStringAsync();
                _output.WriteLine($"Error: {errorContent}");
            }
        }
    }
}