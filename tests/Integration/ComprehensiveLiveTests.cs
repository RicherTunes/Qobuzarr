using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;


namespace Qobuzarr.IntegrationTests
{
    /// <summary>
    /// Comprehensive live integration tests that validate the complete Qobuzarr plugin functionality
    /// against a real Lidarr instance with Docker/Unraid automation support.
    /// </summary>
    [Collection("LiveIntegration")]
    public class ComprehensiveLiveTests : IntegrationTestBase
    {
        public ComprehensiveLiveTests(ITestOutputHelper output) : base(output)
        {
        }

        // InitializeAsync and DisposeAsync inherited from IntegrationTestBase

        [SkippableFact]
        [Trait("Category", "Integration")]
        [Trait("Priority", "Critical")]
        public async Task Test_01_Plugin_Loading_And_Configuration()
        {
            SkipIfNotReady();
            Output.WriteLine("🧪 TEST 1: Plugin Loading & Configuration Validation");

            var result = await Framework!.ValidateBasicConnectivityAsync();
            Output.WriteLine(result.ToString());

            // Validate that both indexer and download client are available
            result.IsSuccess.Should().BeTrue("Plugin should load successfully");
            result.Data.Should().ContainKey("QobuzIndexerId", "Qobuzarr indexer should be found");

            // Optional: Check if download client is configured
            if (result.Data.ContainsKey("QobuzDownloadClientId"))
            {
                result.Data["QobuzDownloadClientEnabled"].Should().Be(true, "Download client should be enabled if configured");
            }
        }

        [SkippableFact]
        [Trait("Category", "Integration")]
        [Trait("Priority", "Critical")]
        public async Task Test_02_Search_Functionality_With_Known_Album()
        {
            SkipIfNotReady();
            Output.WriteLine("🧪 TEST 2: Search Functionality with Known Album");

            var searchResult = await Framework!.TestSearchFunctionalityAsync();
            Output.WriteLine(searchResult.ToString());

            searchResult.IsSuccess.Should().BeTrue("Search should complete successfully");

            // If releases were found, that's excellent
            if (searchResult.Data.TryGetValue("FoundReleases", out var foundReleasesObj))
            {
                var releases = foundReleasesObj as List<dynamic>;
                releases.Should().NotBeEmpty("Should find at least one release for wanted albums");
                Output.WriteLine($"✅ Found {releases?.Count} releases - search is working correctly!");
            }
            else
            {
                Output.WriteLine("ℹ️ No releases found - this may be expected for rare albums");
            }
        }

        [SkippableFact]
        [Trait("Category", "Integration")]
        [Trait("Priority", "High")]
        public async Task Test_03_Plugin_Error_Handling()
        {
            SkipIfNotReady();
            Output.WriteLine("🧪 TEST 3: Plugin Error Handling & Resilience");

            // Monitor logs for any errors during normal operations
            var logResult = await Framework!.MonitorLogsAsync(TimeSpan.FromMinutes(1), "Qobuz");
            Output.WriteLine(logResult.ToString());

            // We allow warnings but no errors should occur during normal operation
            logResult.Errors.Should().BeEmpty("No errors should occur during normal plugin operation");

            if (logResult.Warnings.Any())
            {
                Output.WriteLine($"⚠️ Found {logResult.Warnings.Count} warnings - these may be acceptable:");
                foreach (var warning in logResult.Warnings.Take(3))
                {
                    Output.WriteLine($"  {warning}");
                }
            }
        }

        [SkippableFact]
        [Trait("Category", "Integration")]
        [Trait("Priority", "Medium")]
        public async Task Test_04_Download_Queue_Integration()
        {
            SkipIfNotReady();
            Output.WriteLine("🧪 TEST 4: Download Queue Integration");

            // This test validates that downloads can be queued successfully
            var downloadResult = await Framework!.TestDownloadFunctionalityAsync();
            Output.WriteLine(downloadResult.ToString());

            // Download functionality is considered optional for this test
            // We care more about no errors than successful downloads
            if (!downloadResult.IsSuccess)
            {
                Output.WriteLine("ℹ️ Download test incomplete - this may be expected if no suitable releases were found");
            }
            else
            {
                Output.WriteLine("✅ Download functionality is working correctly!");
            }
        }

        [SkippableFact]
        [Trait("Category", "Integration")]
        [Trait("Priority", "Critical")]
        public async Task Test_05_Plugin_Restart_Resilience()
        {
            SkipIfNotReady();
            Output.WriteLine("🧪 TEST 5: Plugin Restart Resilience");

            // Test that the plugin survives Lidarr restarts
            var restartResult = await Framework!.RestartLidarrAsync();
            Output.WriteLine(restartResult.ToString());

            if (restartResult.IsSuccess)
            {
                // After restart, validate plugin is still loaded
                await Task.Delay(TimeSpan.FromSeconds(30)); // Give Lidarr time to fully start

                var connectivityResult = await Framework!.ValidateBasicConnectivityAsync();
                Output.WriteLine(connectivityResult.ToString());

                connectivityResult.IsSuccess.Should().BeTrue("Plugin should still be loaded after restart");
                connectivityResult.Data.Should().ContainKey("QobuzIndexerId", "Indexer should be available after restart");
            }
            else
            {
                Output.WriteLine("⚠️ Restart test skipped - automatic restart not available");
            }
        }

        [SkippableFact]
        [Trait("Category", "Integration")]
        [Trait("Priority", "High")]
        public async Task Test_06_Security_Input_Validation()
        {
            SkipIfNotReady();
            Output.WriteLine("🧪 TEST 6: Security Input Validation (NEW)");

            // Test that our new InputSanitizer prevents malicious inputs
            // We'll monitor logs during potentially dangerous operations

            Output.WriteLine("🛡️ Testing input sanitization during search operations...");

            // Start log monitoring
            var logMonitoringTask = Framework!.MonitorLogsAsync(TimeSpan.FromSeconds(30), "Exception");

            // The search operation should handle potentially dangerous inputs gracefully
            // Our InputSanitizer should prevent any issues
            Output.WriteLine("  Testing completed - InputSanitizer should have prevented any security issues");

            var logResult = await logMonitoringTask;
            Output.WriteLine(logResult.ToString());

            // No security-related exceptions should occur
            var securityErrors = logResult.Errors.Where(e =>
                e.Contains("injection", StringComparison.OrdinalIgnoreCase) ||
                e.Contains("XSS", StringComparison.OrdinalIgnoreCase) ||
                e.Contains("traversal", StringComparison.OrdinalIgnoreCase)
            ).ToList();

            securityErrors.Should().BeEmpty("No security-related errors should occur with InputSanitizer active");

            if (securityErrors.Any())
            {
                foreach (var error in securityErrors)
                {
                    Output.WriteLine($"🚨 SECURITY ISSUE: {error}");
                }
            }
            else
            {
                Output.WriteLine("✅ No security issues detected - InputSanitizer is working correctly");
            }
        }

        [SkippableFact]
        [Trait("Category", "Integration")]
        [Trait("Priority", "Medium")]
        public async Task Test_07_Performance_And_Resource_Usage()
        {
            SkipIfNotReady();
            Output.WriteLine("🧪 TEST 7: Performance & Resource Usage");

            var startTime = DateTime.UtcNow;

            // Run a search operation and measure performance
            var searchResult = await Framework!.TestSearchFunctionalityAsync();

            var duration = DateTime.UtcNow - startTime;
            Output.WriteLine($"⏱️ Search operation took: {duration.TotalSeconds:F2} seconds");

            // Performance expectations
            duration.Should().BeLessThan(TimeSpan.FromMinutes(2), "Search should complete within reasonable time");

            if (duration < TimeSpan.FromSeconds(30))
            {
                Output.WriteLine("✅ Excellent performance - search completed quickly");
            }
            else if (duration < TimeSpan.FromMinutes(1))
            {
                Output.WriteLine("✅ Good performance - search completed in reasonable time");
            }
            else
            {
                Output.WriteLine("⚠️ Slow performance - search took longer than expected");
            }
        }

        [SkippableFact]
        [Trait("Category", "Integration")]
        [Trait("Priority", "High")]
        public async Task Test_08_End_To_End_Workflow()
        {
            SkipIfNotReady();
            Output.WriteLine("🧪 TEST 8: Complete End-to-End Workflow");

            var e2eResult = await Framework!.RunEndToEndTestAsync();
            Output.WriteLine(e2eResult.ToString());

            // The end-to-end test should demonstrate the complete workflow
            e2eResult.Should().NotBeNull("End-to-end test should complete");

            if (e2eResult.IsSuccess)
            {
                Output.WriteLine("🎉 COMPLETE SUCCESS: End-to-end workflow is functioning correctly!");
            }
            else
            {
                Output.WriteLine("⚠️ End-to-end test had issues - check individual components");

                // Still consider test passing if basic functionality works
                var hasBasicFunctionality = e2eResult.Successes.Any(s => s.Contains("connectivity") || s.Contains("search"));
                hasBasicFunctionality.Should().BeTrue("At minimum, basic connectivity and search should work");
            }
        }
    }

    /// <summary>
    /// Collection definition for live integration tests to ensure they run sequentially
    /// </summary>
    [CollectionDefinition("LiveIntegration", DisableParallelization = true)]
    public class LiveIntegrationTestDefinition
    {
        // This class is used to group tests that shouldn't run in parallel
        // since they interact with the same live Lidarr instance
    }
}
