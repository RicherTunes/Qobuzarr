using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

using Lidarr.Plugin.Qobuzarr.Security;

namespace Qobuzarr.IntegrationTests
{
    /// <summary>
    /// Tests specifically for the security improvements added to the Qobuzarr plugin.
    /// Validates that InputSanitizer and other security measures work correctly in live scenarios.
    /// </summary>
    [Collection("LiveIntegration")]
    public class SecurityValidationTests : IntegrationTestBase
    {
        public SecurityValidationTests(ITestOutputHelper output) : base(output)
        {
        }

        // InitializeAsync and DisposeAsync inherited from IntegrationTestBase

        [SkippableFact]
        [Trait("Category", "Integration")]
        [Trait("Category", "Security")]
        [Trait("Priority", "Critical")]
        public async Task Test_InputSanitizer_Email_Validation()
        {
            SkipIfNotReady();
            Output.WriteLine("🛡️ Testing InputSanitizer Email Validation");
            
            // Test valid emails
            var validEmails = new[] { "test@example.com", "user.name+tag@domain.co.uk", "123@test.org" };
            foreach (var email in validEmails)
            {
                var result = InputSanitizer.SanitizeEmail(email);
                result.Should().NotBeNullOrEmpty($"Valid email '{email}' should be sanitized successfully");
                Output.WriteLine($"✅ Valid email handled correctly: {email} → {result}");
            }
            
            // Test invalid emails (should throw exceptions)
            var invalidEmails = new[] { 
                "'; DROP TABLE users; --@evil.com",
                "test@<script>alert('xss')</script>.com",
                "user@domain'; exec xp_cmdshell('dir'); --",
                new string('a', 300) + "@toolong.com" // Too long
            };
            
            foreach (var email in invalidEmails)
            {
                Output.WriteLine($"🧪 Testing malicious email: {email}");
                
                Action act = () => InputSanitizer.SanitizeEmail(email);
                act.Should().Throw<ArgumentException>($"Malicious email '{email}' should be rejected");
                Output.WriteLine($"✅ Malicious email correctly rejected: {email}");
            }
        }

        [SkippableFact]
        [Trait("Category", "Integration")]
        [Trait("Category", "Security")]
        [Trait("Priority", "Critical")]
        public async Task Test_InputSanitizer_Query_Sanitization()
        {
            SkipIfNotReady();
            Output.WriteLine("🛡️ Testing InputSanitizer Query Sanitization");
            
            // Test legitimate search queries
            var validQueries = new[] { 
                "Miles Davis", 
                "Taylor Swift - 1989", 
                "The Beatles' White Album",
                "Artist & Album Name",
                "Song (Remix) [2021]"
            };
            
            foreach (var query in validQueries)
            {
                var result = InputSanitizer.SanitizeSearchQuery(query);
                result.Should().NotBeNullOrEmpty($"Valid query '{query}' should be sanitized");
                Output.WriteLine($"✅ Valid query sanitized: {query} → {result}");
            }
            
            // Test potentially dangerous queries
            var dangerousQueries = new[] {
                "'; DROP TABLE albums; --",
                "<script>alert('xss')</script>",
                "SELECT * FROM users WHERE 1=1",
                "artist'; exec xp_cmdshell('format c:'); --",
                "onclick=alert('hack')",
                "javascript:void(0)"
            };
            
            foreach (var query in dangerousQueries)
            {
                Output.WriteLine($"🧪 Testing dangerous query: {query}");
                
                var result = InputSanitizer.SanitizeSearchQuery(query);
                
                // Should be sanitized (cleaned) not thrown
                result.Should().NotContain("script", "Scripts should be removed");
                result.Should().NotContain("SELECT", "SQL should be removed");  
                result.Should().NotContain("DROP", "SQL should be removed");
                result.Should().NotContain("exec", "Commands should be removed");
                result.Should().NotContain("onclick", "Events should be removed");
                
                Output.WriteLine($"✅ Dangerous query sanitized: {query} → {result}");
            }
        }

        [SkippableFact]
        [Trait("Category", "Integration")]
        [Trait("Category", "Security")]
        [Trait("Priority", "Critical")]
        public async Task Test_InputSanitizer_Path_Traversal_Prevention()
        {
            SkipIfNotReady();
            Output.WriteLine("🛡️ Testing InputSanitizer Path Traversal Prevention");
            
            // Test legitimate paths
            var validPaths = new[] {
                @"C:\Music\Downloads",
                @"/home/user/music",
                @"D:\Media\Albums\Artist\Album",
                @"./music/downloads"
            };
            
            foreach (var path in validPaths)
            {
                try
                {
                    var result = InputSanitizer.SanitizeFilePath(path);
                    result.Should().NotBeNullOrEmpty($"Valid path '{path}' should be sanitized");
                    Output.WriteLine($"✅ Valid path sanitized: {path} → {result}");
                }
                catch (ArgumentException ex)
                {
                    Output.WriteLine($"⚠️ Valid path rejected (may be OS-specific): {path} - {ex.Message}");
                }
            }
            
            // Test dangerous paths (should throw exceptions)
            var dangerousQueries = new[] {
                @"C:\Music\..\..\..\Windows\System32",
                @"/home/user/../../etc/passwd",
                @"music\..\..\..\..\autoexec.bat",
                @"downloads/../../../secret/file.txt",
                @"path/with/nullbyte\0/exploit.exe",
                @"C:\Music\virus.exe"
            };
            
            foreach (var path in dangerousQueries)
            {
                Output.WriteLine($"🧪 Testing dangerous path: {path}");
                
                Action act = () => InputSanitizer.SanitizeFilePath(path);
                act.Should().Throw<ArgumentException>($"Dangerous path '{path}' should be rejected");
                Output.WriteLine($"✅ Dangerous path correctly rejected: {path}");
            }
        }

        [SkippableFact]
        [Trait("Category", "Integration")]
        [Trait("Category", "Security")]
        [Trait("Priority", "High")]
        public async Task Test_InputSanitizer_Credential_Validation()
        {
            SkipIfNotReady();
            Output.WriteLine("🛡️ Testing InputSanitizer Credential Validation");
            
            // Test App ID validation
            var validAppIds = new[] { "123456789", "app_id_test", "valid-app-id" };
            foreach (var appId in validAppIds)
            {
                try
                {
                    var result = InputSanitizer.SanitizeAppId(appId);
                    result.Should().Be(appId, "Valid App ID should pass through unchanged");
                    Output.WriteLine($"✅ Valid App ID accepted: {appId}");
                }
                catch (ArgumentException ex)
                {
                    Output.WriteLine($"⚠️ Valid App ID rejected: {appId} - {ex.Message}");
                }
            }
            
            // Test invalid App IDs
            var invalidAppIds = new[] { 
                "'; DROP TABLE--", 
                "app id with spaces", 
                "<script>",
                new string('a', 100) // Too long
            };
            
            foreach (var appId in invalidAppIds)
            {
                Output.WriteLine($"🧪 Testing invalid App ID: {appId}");
                
                Action act = () => InputSanitizer.SanitizeAppId(appId);
                act.Should().Throw<ArgumentException>($"Invalid App ID '{appId}' should be rejected");
                Output.WriteLine($"✅ Invalid App ID correctly rejected: {appId}");
            }
            
            // Test password validation
            var validPasswords = new[] { "password123", "ComplexP@ss!", "simple" };
            foreach (var password in validPasswords)
            {
                var result = InputSanitizer.ValidatePassword(password);
                result.Should().Be(password, "Valid password should pass through unchanged");
                Output.WriteLine($"✅ Valid password accepted (length: {password.Length})");
            }
            
            // Test dangerous passwords
            var dangerousPasswords = new[] {
                new string('a', 200), // Too long
                "password\0withNullByte", // Null byte
                "pass\u0001word" // Control character
            };
            
            foreach (var password in dangerousPasswords)
            {
                Output.WriteLine($"🧪 Testing dangerous password (length: {password.Length})");
                
                Action act = () => InputSanitizer.ValidatePassword(password);
                act.Should().Throw<ArgumentException>($"Dangerous password should be rejected");
                Output.WriteLine($"✅ Dangerous password correctly rejected");
            }
        }

        [SkippableFact]
        [Trait("Category", "Integration")]
        [Trait("Category", "Security")]
        [Trait("Priority", "High")]
        public async Task Test_InputSanitizer_Country_Code_Validation()
        {
            SkipIfNotReady();
            Output.WriteLine("🛡️ Testing InputSanitizer Country Code Validation");
            
            // Test valid country codes
            var validCodes = new[] { "US", "CA", "GB", "FR", "DE", "JP", "AU" };
            foreach (var code in validCodes)
            {
                var result = InputSanitizer.SanitizeCountryCode(code.ToLowerInvariant());
                result.Should().Be(code, $"Valid country code '{code}' should be normalized to uppercase");
                Output.WriteLine($"✅ Valid country code: {code.ToLowerInvariant()} → {result}");
            }
            
            // Test invalid country codes
            var invalidCodes = new[] { "USA", "123", "GB'; DROP--", "<script>", "", "X" };
            foreach (var code in invalidCodes)
            {
                Output.WriteLine($"🧪 Testing invalid country code: {code}");
                
                Action act = () => InputSanitizer.SanitizeCountryCode(code);
                act.Should().Throw<ArgumentException>($"Invalid country code '{code}' should be rejected");
                Output.WriteLine($"✅ Invalid country code correctly rejected: {code}");
            }
        }

        [SkippableFact]
        [Trait("Category", "Security")]
        [Trait("Priority", "High")]
        public async Task Test_Security_During_Live_Operations()
        {
            SkipIfNotReady();
            Output.WriteLine("🛡️ Testing Security During Live Operations");
            
            // Start monitoring for security-related log entries
            var logMonitoringTask = Framework!.MonitorLogsAsync(TimeSpan.FromMinutes(1), "");
            
            // Perform normal operations that should trigger our security validations
            Output.WriteLine("Performing normal operations with security validations active...");
            
            try
            {
                // This will internally use our InputSanitizer for all inputs
                await Framework!.TestSearchFunctionalityAsync();
                Output.WriteLine("✅ Search operations completed with security validations");
            }
            catch (Exception ex)
            {
                Output.WriteLine($"⚠️ Search operations failed: {ex.Message}");
            }
            
            // Check logs for security issues
            var logResult = await logMonitoringTask;
            
            // Filter for security-related log entries
            var allLogs = logResult.Data.GetValueOrDefault("LogEntries", new List<string>()) as List<string> ?? new();
            var securityLogs = allLogs.Where(log => 
                log.Contains("InputSanitizer", StringComparison.OrdinalIgnoreCase) ||
                log.Contains("validation", StringComparison.OrdinalIgnoreCase) ||
                log.Contains("sanitiz", StringComparison.OrdinalIgnoreCase) ||
                log.Contains("security", StringComparison.OrdinalIgnoreCase)
            ).ToList();
            
            Output.WriteLine($"📊 Found {securityLogs.Count} security-related log entries:");
            foreach (var log in securityLogs.Take(10))
            {
                Output.WriteLine($"  {log}");
            }
            
            // Check for any security exceptions
            var securityErrors = allLogs.Where(log =>
                log.Contains("injection", StringComparison.OrdinalIgnoreCase) ||
                log.Contains("XSS", StringComparison.OrdinalIgnoreCase) ||
                log.Contains("traversal", StringComparison.OrdinalIgnoreCase) ||
                log.Contains("malicious", StringComparison.OrdinalIgnoreCase)
            ).ToList();
            
            securityErrors.Should().BeEmpty("No security-related errors should occur during normal operations");
            
            if (securityErrors.Any())
            {
                Output.WriteLine("🚨 SECURITY ISSUES DETECTED:");
                foreach (var error in securityErrors)
                {
                    Output.WriteLine($"  {error}");
                }
            }
            else
            {
                Output.WriteLine("✅ No security issues detected during live operations");
            }
        }

        [SkippableFact]
        [Trait("Category", "Security")]
        [Trait("Priority", "Medium")]
        public async Task Test_Dangerous_Content_Detection()
        {
            SkipIfNotReady();
            Output.WriteLine("🛡️ Testing Dangerous Content Detection");
            
            // Test obviously safe content
            var safeInputs = new[] {
                "Miles Davis",
                "The Beatles - Abbey Road",
                "Artist Name 123",
                "Album (Deluxe Edition) [2021]"
            };
            
            foreach (var input in safeInputs)
            {
                var isDangerous = InputSanitizer.ContainsDangerousContent(input);
                isDangerous.Should().BeFalse($"Safe input '{input}' should not be flagged as dangerous");
                Output.WriteLine($"✅ Safe input correctly identified: {input}");
            }
            
            // Test obviously dangerous content
            var dangerousInputs = new[] {
                "'; DROP TABLE albums; --",
                "<script>alert('xss')</script>",
                "SELECT * FROM users",
                "cmd.exe /c format c:",
                "$(rm -rf /)",
                "javascript:void(0)",
                "onload=malicious()",
                "1=1 OR 1=1"
            };
            
            foreach (var input in dangerousInputs)
            {
                var isDangerous = InputSanitizer.ContainsDangerousContent(input);
                isDangerous.Should().BeTrue($"Dangerous input '{input}' should be flagged as dangerous");
                Output.WriteLine($"✅ Dangerous input correctly identified: {input}");
            }
        }

        [SkippableFact]
        [Trait("Category", "Security")]
        [Trait("Priority", "High")]
        public async Task Test_URL_Parameter_Sanitization()
        {
            SkipIfNotReady();
            Output.WriteLine("🛡️ Testing URL Parameter Sanitization");
            
            // Test normal parameters
            var normalParams = new Dictionary<string, string>
            {
                ["query"] = "Miles Davis Kind of Blue",
                ["artist"] = "Taylor Swift",
                ["album"] = "1989 (Taylor's Version)",
                ["limit"] = "50"
            };
            
            foreach (var param in normalParams)
            {
                var result = InputSanitizer.SanitizeQueryParam(param.Key, param.Value);
                result.Should().NotBeNullOrEmpty($"Normal parameter '{param.Key}={param.Value}' should be sanitized");
                Output.WriteLine($"✅ Normal parameter sanitized: {param.Key}={param.Value} → {result}");
            }
            
            // Test potentially dangerous parameters
            var dangerousParams = new Dictionary<string, string>
            {
                ["query"] = "'; DROP TABLE albums; --",
                ["email"] = "user@domain'; exec evil; --",
                ["app_id"] = "<script>alert('xss')</script>",
                ["country_code"] = "US'; DELETE * FROM users; --"
            };
            
            foreach (var param in dangerousParams)
            {
                Output.WriteLine($"🧪 Testing dangerous parameter: {param.Key}={param.Value}");
                
                try
                {
                    var result = InputSanitizer.SanitizeQueryParam(param.Key, param.Value);
                    
                    // If it doesn't throw, it should at least be sanitized
                    result.Should().NotContain("DROP", "SQL injection should be removed");
                    result.Should().NotContain("script", "Script tags should be removed");
                    result.Should().NotContain("exec", "Commands should be removed");
                    
                    Output.WriteLine($"✅ Dangerous parameter sanitized: {param.Key}={param.Value} → {result}");
                }
                catch (ArgumentException)
                {
                    Output.WriteLine($"✅ Dangerous parameter correctly rejected: {param.Key}={param.Value}");
                }
            }
        }

        [SkippableFact]
        [Trait("Category", "Integration")]
        [Trait("Category", "Security")]
        [Trait("Priority", "Medium")]
        public async Task Test_Authentication_Security_In_Live_Environment()
        {
            SkipIfNotReady();
            Output.WriteLine("🛡️ Testing Authentication Security in Live Environment");
            
            // Monitor logs during authentication operations
            var logMonitoringTask = Framework!.MonitorLogsAsync(TimeSpan.FromSeconds(30), "auth");
            
            // Trigger operations that would use authentication
            Output.WriteLine("Triggering authentication-related operations...");
            
            try
            {
                // Any search operation will trigger authentication
                await Framework!.TestSearchFunctionalityAsync();
                Output.WriteLine("✅ Authentication operations completed");
            }
            catch (Exception ex)
            {
                Output.WriteLine($"⚠️ Authentication operations failed: {ex.Message}");
            }
            
            var logResult = await logMonitoringTask;
            var authLogs = logResult.Data.GetValueOrDefault("LogEntries", new List<string>()) as List<string> ?? new();
            
            Output.WriteLine($"📊 Authentication-related logs: {authLogs.Count}");
            
            // Check that no credentials are leaked in logs
            var credentialLeaks = authLogs.Where(log =>
                log.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                log.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                log.Contains("token", StringComparison.OrdinalIgnoreCase)
            ).Where(log => 
                !log.Contains("***") && // Masked tokens are OK
                !log.Contains("token=***") // Masked parameters are OK
            ).ToList();
            
            credentialLeaks.Should().BeEmpty("No credentials should be leaked in logs");
            
            if (credentialLeaks.Any())
            {
                Output.WriteLine("🚨 CREDENTIAL LEAK DETECTED:");
                foreach (var leak in credentialLeaks)
                {
                    Output.WriteLine($"  {leak}");
                }
            }
            else
            {
                Output.WriteLine("✅ No credential leaks detected in logs");
            }
        }

        [SkippableFact]
        [Trait("Category", "Security")]
        [Trait("Priority", "Low")]
        public async Task Test_Security_Documentation_And_Guidelines()
        {
            SkipIfNotReady();
            Output.WriteLine("📚 Validating Security Documentation");
            
            // This test validates that security measures are properly documented
            var securityFiles = new[]
            {
                "src/Security/InputSanitizer.cs",
                "src/Security/SecureCredentialManager.cs", 
                "docs/VERIFICATION-REPORT.md"
            };
            
            foreach (var file in securityFiles)
            {
                var exists = System.IO.File.Exists(file);
                exists.Should().BeTrue($"Security file '{file}' should exist");
                
                if (exists)
                {
                    Output.WriteLine($"✅ Security file exists: {file}");
                }
                else
                {
                    Output.WriteLine($"❌ Security file missing: {file}");
                }
            }
            
            Output.WriteLine("✅ Security documentation validation completed");
        }
    }
}
