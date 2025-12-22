using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Qobuzarr.IntegrationTests.Helpers;
using Xunit.Abstractions;

namespace Qobuzarr.IntegrationTests
{
    /// <summary>
    /// Comprehensive integration testing framework for live Lidarr instances.
    /// Supports Docker containers, Unraid systems, and direct API access.
    /// </summary>
    public class LiveLidarrIntegrationFramework : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly HttpClient _httpClient;
        private readonly string _lidarrUrl;
        private readonly string _lidarrApiKey;
        private readonly string? _dockerContainerName;
        private readonly string? _unraidHost;
        private readonly string? _unraidApiKey;
        private readonly bool _isDockerEnvironment;
        private readonly bool _isUnraidEnvironment;

        public LiveLidarrIntegrationFramework(ITestOutputHelper output)
        {
            _output = output;
            // Require explicit opt-in for live integration tests to avoid accidental long-running builds
            var enableLive = Environment.GetEnvironmentVariable("ENABLE_LIVE_INTEGRATION_TESTS");
            if (!string.Equals(enableLive, "true", StringComparison.OrdinalIgnoreCase))
            {
                // Throwing here will be caught by callers that translate to SkipException
                throw new InvalidOperationException("Live integration tests are disabled (set ENABLE_LIVE_INTEGRATION_TESTS=true to enable)");
            }
            _httpClient = new HttpClient();
            
            // Load configuration from environment and .env file
            DotNetEnv.Env.TraversePath().Load();
            
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();
            
            _lidarrUrl = configuration["LIDARR_URL"] ?? throw new InvalidOperationException("LIDARR_URL not configured");
            _lidarrApiKey = configuration["LIDARR_API_KEY"] ?? throw new InvalidOperationException("LIDARR_API_KEY not configured");
            
            _dockerContainerName = configuration["DOCKER_CONTAINER_NAME"];
            _unraidHost = configuration["UNRAID_HOST"];
            _unraidApiKey = configuration["UNRAID_API_KEY"];
            
            _isDockerEnvironment = !string.IsNullOrWhiteSpace(_dockerContainerName);
            _isUnraidEnvironment = !string.IsNullOrWhiteSpace(_unraidHost) && !string.IsNullOrWhiteSpace(_unraidApiKey);
            
            _httpClient.DefaultRequestHeaders.Add("X-Api-Key", _lidarrApiKey);
            // Keep tight timeouts in test framework to avoid long hangs
            _httpClient.Timeout = TimeSpan.FromSeconds(5);
            
            _output.WriteLine($"🎵 Live Lidarr Integration Framework");
            _output.WriteLine($"  Lidarr URL: {_lidarrUrl}");
            _output.WriteLine($"  Docker: {(_isDockerEnvironment ? $"✅ {_dockerContainerName}" : "❌ Not configured")}");
            _output.WriteLine($"  Unraid: {(_isUnraidEnvironment ? $"✅ {_unraidHost}" : "❌ Not configured")}");
        }

        /// <summary>
        /// Validates the basic connectivity and plugin presence
        /// </summary>
        public async Task<ValidationResult> ValidateBasicConnectivityAsync()
        {
            var result = new ValidationResult("Basic Connectivity");
            
            try
            {
                // Test Lidarr API connectivity
                _output.WriteLine("🔍 Testing Lidarr API connectivity...");
                var statusResponse = await _httpClient.GetAsync($"{_lidarrUrl}/api/v1/system/status");
                
                if (!statusResponse.IsSuccessStatusCode)
                {
                    result.AddError($"Lidarr API not accessible: {statusResponse.StatusCode}");
                    return result;
                }
                
                var statusContent = await statusResponse.Content.ReadAsStringAsync();
                var statusData = JsonConvert.DeserializeObject<dynamic>(statusContent);
                result.AddInfo($"Lidarr Version: {statusData?.version}");
                result.AddInfo($"Build Time: {statusData?.buildTime}");
                
                // Check if Qobuzarr plugin is loaded
                var indexerResponse = await _httpClient.GetAsync($"{_lidarrUrl}/api/v1/indexer");
                if (indexerResponse.IsSuccessStatusCode)
                {
                    var indexerContent = await indexerResponse.Content.ReadAsStringAsync();
                    var indexers = JsonConvert.DeserializeObject<JArray>(indexerContent);
                    
                    var qobuzIndexer = indexers?.FirstOrDefault(i => i["implementation"]?.ToString() == "QobuzIndexer");
                    if (qobuzIndexer != null)
                    {
                        var indexerId = JsonExtractor.RequireInt(
                            qobuzIndexer["id"], 
                            "indexer.id", 
                            "/api/v1/indexer", 
                            indexerContent);
                        
                        result.AddSuccess($"Qobuzarr indexer found (ID: {indexerId})");
                        result.Data["QobuzIndexerId"] = indexerId;
                        result.Data["QobuzIndexerEnabled"] = JsonExtractor.TryGetBool(qobuzIndexer["enable"]);
                    }
                    else
                    {
                        result.AddError("Qobuzarr indexer not found in Lidarr");
                    }
                }
                
                // Check download client
                var downloadClientResponse = await _httpClient.GetAsync($"{_lidarrUrl}/api/v1/downloadclient");
                if (downloadClientResponse.IsSuccessStatusCode)
                {
                    var downloadContent = await downloadClientResponse.Content.ReadAsStringAsync();
                    var downloadClients = JsonConvert.DeserializeObject<JArray>(downloadContent);
                    
                    var qobuzDownloadClient = downloadClients?.FirstOrDefault(d => d["implementation"]?.ToString() == "QobuzDownloadClient");
                    if (qobuzDownloadClient != null)
                    {
                        var clientId = JsonExtractor.RequireInt(
                            qobuzDownloadClient["id"], 
                            "downloadclient.id", 
                            "/api/v1/downloadclient", 
                            downloadContent);
                        
                        result.AddSuccess($"Qobuzarr download client found (ID: {clientId})");
                        result.Data["QobuzDownloadClientId"] = clientId;
                        result.Data["QobuzDownloadClientEnabled"] = JsonExtractor.TryGetBool(qobuzDownloadClient["enable"]);
                    }
                    else
                    {
                        result.AddWarning("Qobuzarr download client not configured");
                    }
                }
                
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                result.AddError($"Connectivity test failed: {ex.Message}");
            }
            
            return result;
        }

        /// <summary>
        /// Deploys the plugin to the live Lidarr instance
        /// </summary>
        public async Task<ValidationResult> DeployPluginAsync(string? pluginPath = null)
        {
            var result = new ValidationResult("Plugin Deployment");
            
            try
            {
                // Default to the standard build output
                pluginPath ??= "bin/Lidarr.Plugin.Qobuzarr.dll";
                
                if (!File.Exists(pluginPath))
                {
                    result.AddError($"Plugin file not found: {pluginPath}");
                    return result;
                }
                
                _output.WriteLine($"🚀 Deploying plugin from: {pluginPath}");
                
                if (_isDockerEnvironment)
                {
                    result = await DeployToDockerAsync(pluginPath);
                }
                else if (_isUnraidEnvironment)
                {
                    result = await DeployToUnraidAsync(pluginPath);
                }
                else
                {
                    result.AddWarning("No deployment method configured (Docker/Unraid)");
                    result.AddInfo("Manual deployment required");
                }
            }
            catch (Exception ex)
            {
                result.AddError($"Deployment failed: {ex.Message}");
            }
            
            return result;
        }

        /// <summary>
        /// Restarts Lidarr and waits for it to come back online
        /// </summary>
        public async Task<ValidationResult> RestartLidarrAsync()
        {
            var result = new ValidationResult("Lidarr Restart");
            
            try
            {
                _output.WriteLine("🔄 Restarting Lidarr...");
                
                if (_isDockerEnvironment)
                {
                    result = await RestartDockerContainerAsync();
                }
                else if (_isUnraidEnvironment)
                {
                    result = await RestartUnraidContainerAsync();
                }
                else
                {
                    // Try API restart command
                    var restartPayload = JsonConvert.SerializeObject(new { name = "Restart" });
                    var content = new StringContent(restartPayload, Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync($"{_lidarrUrl}/api/v1/command", content);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        result.AddSuccess("Restart command sent via API");
                        await WaitForLidarrOnlineAsync();
                        result.IsSuccess = true;
                    }
                    else
                    {
                        result.AddError($"API restart failed: {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.AddError($"Restart failed: {ex.Message}");
            }
            
            return result;
        }

        /// <summary>
        /// Monitors Lidarr logs for plugin-related activity
        /// </summary>
        public async Task<ValidationResult> MonitorLogsAsync(TimeSpan duration, string filter = "Qobuz")
        {
            var result = new ValidationResult("Log Monitoring");
            var logs = new List<string>();
            
            try
            {
                _output.WriteLine($"📋 Monitoring logs for {duration.TotalMinutes:F1} minutes (filter: '{filter}')...");
                
                var endTime = DateTime.UtcNow.Add(duration);
                
                while (DateTime.UtcNow < endTime)
                {
                    if (_isDockerEnvironment)
                    {
                        var dockerLogs = await GetDockerLogsAsync(filter);
                        logs.AddRange(dockerLogs);
                    }
                    else
                    {
                        // Try to get logs via API (if available)
                        var apiLogs = await GetLidarrLogsViaApiAsync(filter);
                        logs.AddRange(apiLogs);
                    }
                    
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
                
                result.Data["LogEntries"] = logs;
                result.AddInfo($"Collected {logs.Count} log entries");
                
                // Analyze logs for issues
                var errors = logs.Where(log => log.Contains("Error") || log.Contains("Exception")).ToList();
                var warnings = logs.Where(log => log.Contains("Warn")).ToList();
                
                if (errors.Any())
                {
                    result.AddError($"Found {errors.Count} errors in logs");
                    foreach (var error in errors.Take(5))
                    {
                        result.AddError($"  {error}");
                    }
                }
                
                if (warnings.Any())
                {
                    result.AddWarning($"Found {warnings.Count} warnings in logs");
                }
                
                result.IsSuccess = !errors.Any();
            }
            catch (Exception ex)
            {
                result.AddError($"Log monitoring failed: {ex.Message}");
            }
            
            return result;
        }

        /// <summary>
        /// Runs a comprehensive end-to-end test scenario
        /// </summary>
        public async Task<ValidationResult> RunEndToEndTestAsync()
        {
            var result = new ValidationResult("End-to-End Test");
            
            try
            {
                _output.WriteLine("🎯 Starting comprehensive end-to-end test...");
                
                // Step 1: Validate connectivity
                var connectivityResult = await ValidateBasicConnectivityAsync();
                if (!connectivityResult.IsSuccess)
                {
                    result.AddError("Connectivity validation failed");
                    result.Merge(connectivityResult);
                    return result;
                }
                
                // Step 2: Test search functionality
                var searchResult = await TestSearchFunctionalityAsync();
                result.Merge(searchResult);
                
                // Step 3: Test download functionality (if downloads found)
                if (searchResult.IsSuccess && searchResult.Data.ContainsKey("FoundReleases"))
                {
                    var downloadResult = await TestDownloadFunctionalityAsync();
                    result.Merge(downloadResult);
                }
                
                // Step 4: Monitor for errors during operations
                var monitorResult = await MonitorLogsAsync(TimeSpan.FromMinutes(2), "Qobuz");
                result.Merge(monitorResult);
                
                result.IsSuccess = connectivityResult.IsSuccess && searchResult.IsSuccess;
                result.AddInfo($"End-to-end test completed. Success: {result.IsSuccess}");
            }
            catch (Exception ex)
            {
                result.AddError($"End-to-end test failed: {ex.Message}");
            }
            
            return result;
        }

        /// <summary>
        /// Tests search functionality with a known good query
        /// </summary>
        public async Task<ValidationResult> TestSearchFunctionalityAsync()
        {
            var result = new ValidationResult("Search Functionality");
            
            try
            {
                _output.WriteLine("🔍 Testing search functionality...");
                
                // Get a wanted album to search for
                var wantedResponse = await _httpClient.GetAsync($"{_lidarrUrl}/api/v1/wanted/missing?pageSize=3");
                if (!wantedResponse.IsSuccessStatusCode)
                {
                    result.AddError("Cannot access wanted albums");
                    return result;
                }
                
                var wantedContent = await wantedResponse.Content.ReadAsStringAsync();
                var wantedData = JsonConvert.DeserializeObject<JObject>(wantedContent);
                var albums = wantedData?["records"] as JArray;
                
                if (albums == null || albums.Count == 0)
                {
                    result.AddWarning("No wanted albums found for testing");
                    return result;
                }
                
                var testAlbum = albums[0];
                var albumId = JsonExtractor.RequireInt(
                    testAlbum["id"], 
                    "records[0].id", 
                    "/api/v1/wanted/missing", 
                    wantedContent);
                var albumTitle = JsonExtractor.TryGetString(testAlbum["title"]);
                var artistName = JsonExtractor.TryGetString(testAlbum["artist"]?["artistName"]);
                
                _output.WriteLine($"🎯 Testing search for: {artistName} - {albumTitle}");
                
                // Trigger album search
                var searchPayload = JsonConvert.SerializeObject(new 
                { 
                    name = "AlbumSearch",
                    albumIds = new[] { albumId } 
                });
                var searchContent = new StringContent(searchPayload, Encoding.UTF8, "application/json");
                
                var searchResponse = await _httpClient.PostAsync($"{_lidarrUrl}/api/v1/command", searchContent);
                
                if (searchResponse.IsSuccessStatusCode)
                {
                    var searchResult = await searchResponse.Content.ReadAsStringAsync();
                    var searchResultData = JsonConvert.DeserializeObject<JObject>(searchResult);
                    var commandId = JsonExtractor.RequireInt(
                        searchResultData?["id"], 
                        "id", 
                        "/api/v1/command", 
                        searchResult);
                    
                    // Monitor search progress
                    var progressResult = await MonitorCommandProgressAsync(commandId);
                    result.Merge(progressResult);
                    
                    // Check if any releases were found
                    var releases = await GetReleasesForAlbumAsync(albumId);
                    if (releases.Any())
                    {
                        result.AddSuccess($"Found {releases.Count} releases for album");
                        result.Data["FoundReleases"] = releases;
                    }
                    else
                    {
                        result.AddWarning("Search completed but no releases found");
                    }
                    
                    result.IsSuccess = true;
                }
                else
                {
                    result.AddError($"Search command failed: {searchResponse.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                result.AddError($"Search test failed: {ex.Message}");
            }
            
            return result;
        }

        /// <summary>
        /// Tests download functionality if releases are available
        /// </summary>
        public async Task<ValidationResult> TestDownloadFunctionalityAsync()
        {
            var result = new ValidationResult("Download Functionality");
            
            try
            {
                _output.WriteLine("📥 Testing download functionality...");
                
                // Get current queue size
                var queueBefore = await GetDownloadQueueAsync();
                
                // Get the first available release
                var releasesResponse = await _httpClient.GetAsync($"{_lidarrUrl}/api/v1/release?albumId=1&pageSize=1");
                if (!releasesResponse.IsSuccessStatusCode)
                {
                    result.AddWarning("No releases available for download testing");
                    return result;
                }
                
                var releasesContent = await releasesResponse.Content.ReadAsStringAsync();
                var releases = JsonConvert.DeserializeObject<JArray>(releasesContent);
                
                if (releases == null || releases.Count == 0)
                {
                    result.AddWarning("No releases found for download testing");
                    return result;
                }
                
                var testRelease = releases[0];
                var releaseTitle = JsonExtractor.TryGetString(testRelease["title"]);
                
                _output.WriteLine($"🎯 Testing download for: {releaseTitle}");
                
                // Trigger download
                var releaseAlbumId = JsonExtractor.RequireInt(
                    testRelease["albumId"], 
                    "[0].albumId", 
                    "/api/v1/release", 
                    releasesContent);
                var downloadPayload = JsonConvert.SerializeObject(new 
                { 
                    name = "DownloadSearch",
                    indexerId = result.Data.GetValueOrDefault("QobuzIndexerId", 1),
                    albumIds = new[] { releaseAlbumId }
                });
                var downloadContent = new StringContent(downloadPayload, Encoding.UTF8, "application/json");
                
                var downloadResponse = await _httpClient.PostAsync($"{_lidarrUrl}/api/v1/command", downloadContent);
                
                if (downloadResponse.IsSuccessStatusCode)
                {
                    result.AddSuccess("Download command sent successfully");
                    
                    // Monitor queue changes
                    await Task.Delay(TimeSpan.FromSeconds(10));
                    var queueAfter = await GetDownloadQueueAsync();
                    
                    if (queueAfter.Count > queueBefore.Count)
                    {
                        result.AddSuccess($"Download queued successfully ({queueAfter.Count - queueBefore.Count} new items)");
                    }
                    
                    result.IsSuccess = true;
                }
                else
                {
                    result.AddError($"Download command failed: {downloadResponse.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                result.AddError($"Download test failed: {ex.Message}");
            }
            
            return result;
        }

        /// <summary>
        /// Gets Docker logs for the Lidarr container
        /// </summary>
        private async Task<List<string>> GetDockerLogsAsync(string? filter = null)
        {
            var logs = new List<string>();
            
            try
            {
                if (!_isDockerEnvironment) return logs;
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"logs --tail 50 {_dockerContainerName}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    var error = await process.StandardError.ReadToEndAsync();
                    
                    await process.WaitForExitAsync();
                    
                    var allLines = output.Split('\n').Concat(error.Split('\n'));
                    
                    foreach (var line in allLines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            if (string.IsNullOrWhiteSpace(filter) || line.Contains(filter, StringComparison.OrdinalIgnoreCase))
                            {
                                logs.Add(line.Trim());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"⚠️ Failed to get Docker logs: {ex.Message}");
            }
            
            return logs;
        }

        /// <summary>
        /// Restarts Docker container
        /// </summary>
        private async Task<ValidationResult> RestartDockerContainerAsync()
        {
            var result = new ValidationResult("Docker Container Restart");
            
            try
            {
                _output.WriteLine($"🔄 Restarting Docker container: {_dockerContainerName}");
                
                var restartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"restart {_dockerContainerName}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(restartInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    
                    if (process.ExitCode == 0)
                    {
                        result.AddSuccess("Docker container restarted successfully");
                        await WaitForLidarrOnlineAsync();
                        result.IsSuccess = true;
                    }
                    else
                    {
                        var error = await process.StandardError.ReadToEndAsync();
                        result.AddError($"Docker restart failed: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.AddError($"Docker restart failed: {ex.Message}");
            }
            
            return result;
        }

        /// <summary>
        /// Deploys plugin to Docker container
        /// </summary>
        private async Task<ValidationResult> DeployToDockerAsync(string pluginPath)
        {
            var result = new ValidationResult("Docker Deployment");
            
            try
            {
                // Copy plugin files to container
                var copyCommands = new[]
                {
                    $"docker cp \"{pluginPath}\" {_dockerContainerName}:/app/Plugins/Qobuzarr/",
                    $"docker cp \"plugin.json\" {_dockerContainerName}:/app/Plugins/Qobuzarr/",
                    $"docker cp \"bin/*.pdb\" {_dockerContainerName}:/app/Plugins/Qobuzarr/ 2>nul || echo 'No PDB files'"
                };
                
                foreach (var command in copyCommands)
                {
                    var parts = command.Split(' ', 3);
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = parts[0],
                        Arguments = string.Join(" ", parts.Skip(1)),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    
                    using var process = Process.Start(startInfo);
                    if (process != null)
                    {
                        await process.WaitForExitAsync();
                        
                        if (process.ExitCode == 0)
                        {
                            result.AddSuccess($"Executed: {command}");
                        }
                        else
                        {
                            var error = await process.StandardError.ReadToEndAsync();
                            result.AddWarning($"Command warning: {command} - {error}");
                        }
                    }
                }
                
                result.IsSuccess = true;
                result.AddSuccess("Plugin deployed to Docker container");
            }
            catch (Exception ex)
            {
                result.AddError($"Docker deployment failed: {ex.Message}");
            }
            
            return result;
        }

        /// <summary>
        /// Deploys plugin to Unraid system via API
        /// </summary>
        private async Task<ValidationResult> DeployToUnraidAsync(string pluginPath)
        {
            var result = new ValidationResult("Unraid Deployment");
            
            try
            {
                _output.WriteLine($"🚀 Deploying to Unraid system: {_unraidHost}");
                
                // This would require Unraid API implementation
                // For now, provide guidance on manual deployment
                result.AddInfo("Unraid deployment requires manual file copy or SSH automation");
                result.AddInfo($"Copy {pluginPath} to Unraid Lidarr plugins directory");
                result.AddWarning("Automated Unraid deployment not yet implemented");
                
                result.IsSuccess = false; // Mark as incomplete for now
            }
            catch (Exception ex)
            {
                result.AddError($"Unraid deployment failed: {ex.Message}");
            }
            
            return result;
        }

        /// <summary>
        /// Waits for Lidarr to come back online after restart
        /// </summary>
        private async Task WaitForLidarrOnlineAsync()
        {
            _output.WriteLine("⏳ Waiting for Lidarr to come back online...");
            
            var maxWait = TimeSpan.FromMinutes(5);
            var startTime = DateTime.UtcNow;
            
            while (DateTime.UtcNow - startTime < maxWait)
            {
                try
                {
                    var statusResponse = await _httpClient.GetAsync($"{_lidarrUrl}/api/v1/system/status");
                    if (statusResponse.IsSuccessStatusCode)
                    {
                        _output.WriteLine("✅ Lidarr is back online!");
                        return;
                    }
                }
                catch
                {
                    // Expected while Lidarr is restarting
                }
                
                await Task.Delay(TimeSpan.FromSeconds(10));
                _output.WriteLine("  ⏳ Still waiting...");
            }
            
            throw new TimeoutException("Lidarr did not come back online within timeout period");
        }

        /// <summary>
        /// Monitors command progress and reports status
        /// </summary>
        private async Task<ValidationResult> MonitorCommandProgressAsync(int commandId)
        {
            var result = new ValidationResult($"Command {commandId} Progress");
            
            var maxWait = TimeSpan.FromMinutes(3);
            var startTime = DateTime.UtcNow;
            
            while (DateTime.UtcNow - startTime < maxWait)
            {
                try
                {
                    var commandResponse = await _httpClient.GetAsync($"{_lidarrUrl}/api/v1/command/{commandId}");
                    if (commandResponse.IsSuccessStatusCode)
                    {
                        var commandContent = await commandResponse.Content.ReadAsStringAsync();
                        var commandData = JsonConvert.DeserializeObject<dynamic>(commandContent);
                        
                        var status = commandData?.status?.ToString();
                        var message = commandData?.message?.ToString();
                        var progress = commandData?.progress?.ToString();
                        
                        _output.WriteLine($"  📊 Status: {status} | Progress: {progress}% | {message}");
                        
                        if (status == "completed")
                        {
                            result.AddSuccess("Command completed successfully");
                            result.IsSuccess = true;
                            return result;
                        }
                        else if (status == "failed")
                        {
                            var exception = commandData?.exception?.ToString();
                            result.AddError($"Command failed: {exception ?? message}");
                            return result;
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.AddWarning($"Error monitoring command: {ex.Message}");
                }
                
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
            
            result.AddWarning("Command monitoring timed out");
            return result;
        }

        /// <summary>
        /// Gets current download queue
        /// </summary>
        private async Task<JArray> GetDownloadQueueAsync()
        {
            try
            {
                var queueResponse = await _httpClient.GetAsync($"{_lidarrUrl}/api/v1/queue");
                if (queueResponse.IsSuccessStatusCode)
                {
                    var queueContent = await queueResponse.Content.ReadAsStringAsync();
                    var queueData = JsonConvert.DeserializeObject<dynamic>(queueContent);
                    return queueData?.records as JArray ?? new JArray();
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"⚠️ Failed to get download queue: {ex.Message}");
            }
            
            return new JArray();
        }

        /// <summary>
        /// Gets releases for a specific album
        /// </summary>
        private async Task<List<dynamic>> GetReleasesForAlbumAsync(int albumId)
        {
            try
            {
                var releasesResponse = await _httpClient.GetAsync($"{_lidarrUrl}/api/v1/release?albumId={albumId}");
                if (releasesResponse.IsSuccessStatusCode)
                {
                    var releasesContent = await releasesResponse.Content.ReadAsStringAsync();
                    var releases = JsonConvert.DeserializeObject<JArray>(releasesContent);
                    return releases?.ToObject<List<dynamic>>() ?? new List<dynamic>();
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"⚠️ Failed to get releases: {ex.Message}");
            }
            
            return new List<dynamic>();
        }

        /// <summary>
        /// Gets Lidarr logs via API (if available)
        /// </summary>
        private async Task<List<string>> GetLidarrLogsViaApiAsync(string? filter = null)
        {
            var logs = new List<string>();
            
            try
            {
                var logsResponse = await _httpClient.GetAsync($"{_lidarrUrl}/api/v1/log?pageSize=50");
                if (logsResponse.IsSuccessStatusCode)
                {
                    var logsContent = await logsResponse.Content.ReadAsStringAsync();
                    var logsData = JsonConvert.DeserializeObject<dynamic>(logsContent);
                    var records = logsData?.records as JArray;
                    
                    if (records != null)
                    {
                        foreach (var record in records)
                        {
                            var message = record["message"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(message))
                            {
                                if (string.IsNullOrWhiteSpace(filter) || 
                                    message.Contains(filter, StringComparison.OrdinalIgnoreCase))
                                {
                                    var time = record["time"]?.ToString();
                                    var level = record["level"]?.ToString();
                                    logs.Add($"[{time}] {level}: {message}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"⚠️ Failed to get API logs: {ex.Message}");
            }
            
            return logs;
        }

        /// <summary>
        /// Restart Unraid container via API
        /// </summary>
        private async Task<ValidationResult> RestartUnraidContainerAsync()
        {
            var result = new ValidationResult("Unraid Container Restart");
            
            // This would require Unraid API implementation
            result.AddWarning("Unraid restart automation not yet implemented");
            result.AddInfo("Manual restart required via Unraid web interface");
            
            return result;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    /// Result container for validation operations
    /// </summary>
    public class ValidationResult
    {
        public string TestName { get; }
        public bool IsSuccess { get; set; }
        public List<string> Successes { get; } = new();
        public List<string> Warnings { get; } = new();
        public List<string> Errors { get; } = new();
        public Dictionary<string, object> Data { get; } = new();

        public ValidationResult(string testName)
        {
            TestName = testName;
        }

        public void AddSuccess(string message) => Successes.Add(message);
        public void AddWarning(string message) => Warnings.Add(message);
        public void AddError(string message) => Errors.Add(message);
        public void AddInfo(string message) => AddSuccess(message);

        public void Merge(ValidationResult other)
        {
            Successes.AddRange(other.Successes);
            Warnings.AddRange(other.Warnings);
            Errors.AddRange(other.Errors);
            
            foreach (var kvp in other.Data)
            {
                Data[kvp.Key] = kvp.Value;
            }
            
            if (!other.IsSuccess)
            {
                IsSuccess = false;
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {TestName} ===");
            sb.AppendLine($"Result: {(IsSuccess ? "✅ SUCCESS" : "❌ FAILED")}");
            
            if (Successes.Any())
            {
                sb.AppendLine("✅ Successes:");
                foreach (var success in Successes)
                    sb.AppendLine($"  {success}");
            }
            
            if (Warnings.Any())
            {
                sb.AppendLine("⚠️ Warnings:");
                foreach (var warning in Warnings)
                    sb.AppendLine($"  {warning}");
            }
            
            if (Errors.Any())
            {
                sb.AppendLine("❌ Errors:");
                foreach (var error in Errors)
                    sb.AppendLine($"  {error}");
            }
            
            return sb.ToString();
        }
    }
}
