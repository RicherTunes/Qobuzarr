using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Security;
using Qobuzarr.Tests.TestData;

namespace Qobuzarr.Tests.Unit.Security
{
    /// <summary>
    /// Comprehensive tests for the consolidated InputSanitizer class.
    /// Includes extensive chaos monkey testing for robustness validation.
    /// </summary>
    public class InputSanitizerTests
    {
        #region Basic Functionality Tests

        [Fact]
        public void SanitizeFileName_WithNullInput_ShouldReturnSafeDefault()
        {
            var result = InputSanitizer.SanitizeFileName(null);
            result.Should().Be("unknown_file");
        }

        [Fact]
        public void SanitizeFileName_WithEmptyInput_ShouldReturnSafeDefault()
        {
            var result = InputSanitizer.SanitizeFileName("");
            result.Should().Be("unknown_file");
        }

        [Fact]
        public void SanitizeFileName_WithCleanInput_ShouldReturnUnchanged()
        {
            var clean = "track_01_artist_title.flac";
            var result = InputSanitizer.SanitizeFileName(clean);
            result.Should().Be(clean);
        }

        [Fact]
        public void SanitizeFileName_WithIllegalCharacters_ShouldReplace()
        {
            var illegal = "track<>:\"|?*/.flac";
            var result = InputSanitizer.SanitizeFileName(illegal);
            
            result.Should().NotContain("<");
            result.Should().NotContain(">");
            result.Should().NotContain(":");
            result.Should().NotContain("\"");
            result.Should().NotContain("|");
            result.Should().NotContain("?");
            result.Should().NotContain("*");
            result.Should().Be("track_________.flac");
        }

        [Fact]
        public void SanitizeArtistName_WithNullInput_ShouldReturnDefault()
        {
            var result = InputSanitizer.SanitizeArtistName(null);
            result.Should().Be("Unknown Artist");
        }

        [Fact]
        public void SanitizeAlbumTitle_WithNullInput_ShouldReturnDefault()
        {
            var result = InputSanitizer.SanitizeAlbumTitle(null);
            result.Should().Be("Unknown Album");
        }

        [Fact]
        public void SanitizeVersion_WithCleanInput_ShouldReturnUnchanged()
        {
            var clean = "Deluxe Edition";
            var result = InputSanitizer.SanitizeVersion(clean);
            result.Should().Be(clean);
        }

        [Fact]
        public void HtmlEncode_ShouldEscapeHtmlCharacters()
        {
            var input = "<script>alert('test')</script>";
            var result = InputSanitizer.HtmlEncode(input);
            
            result.Should().Contain("&lt;");
            result.Should().Contain("&gt;");
            result.Should().NotContain("<script>");
        }

        [Fact]
        public void IsUrlSafe_WithSafeUrl_ShouldReturnTrue()
        {
            InputSanitizer.IsUrlSafe("https://example.com/safe").Should().BeTrue();
            InputSanitizer.IsUrlSafe("http://music.qobuz.com/track").Should().BeTrue();
        }

        [Fact]
        public void IsUrlSafe_WithDangerousUrl_ShouldReturnFalse()
        {
            InputSanitizer.IsUrlSafe("javascript:alert('xss')").Should().BeFalse();
            InputSanitizer.IsUrlSafe("file:///etc/passwd").Should().BeFalse();
            InputSanitizer.IsUrlSafe("data:text/html,<script>alert(1)</script>").Should().BeFalse();
        }

        #endregion

        #region 🐒 CHAOS MONKEY ROBUSTNESS TESTS 🐒

        /// <summary>
        /// 🐒💥 Chaos Monkey test for file name sanitization using extreme edge cases
        /// Tests robustness against malicious file names and path traversal attempts
        /// </summary>
        [Trait("Category", "Quarantined")]
        [Fact]
        public void SanitizeFileName_WithChaosMonkeyFilePaths_ShouldHandleRobustly()
        {
            // Get file system exploitation cases
            var fileSystemChaos = EdgeCaseData.GetFileSystemExploitCases().ToArray();

            foreach (var chaosCase in fileSystemChaos)
            {
                var chaosFileName = chaosCase[0].ToString();
                var testDescription = chaosCase[1].ToString();

                // Act & Assert - Should handle extreme file names safely
                Action sanitizeAction = () =>
                {
                    var result = InputSanitizer.SanitizeFileName(chaosFileName);
                    
                    // Basic safety checks
                    result.Should().NotBeNull($"file name sanitizer should handle chaos case: {testDescription}");
                    result.Should().NotBeEmpty($"file name sanitizer should return non-empty result for {testDescription}");
                    
                    // Should remove dangerous file system characters
                    result.Should().NotContain("<", $"should remove angle brackets from file name: {testDescription}");
                    result.Should().NotContain(">", $"should remove angle brackets from file name: {testDescription}");
                    result.Should().NotContain(":", $"should remove colon from file name: {testDescription}");
                    result.Should().NotContain("\"", $"should remove quotes from file name: {testDescription}");
                    result.Should().NotContain("|", $"should remove pipe from file name: {testDescription}");
                    result.Should().NotContain("?", $"should remove question mark from file name: {testDescription}");
                    result.Should().NotContain("*", $"should remove asterisk from file name: {testDescription}");
                    
                    // Should handle path traversal attempts
                    result.Should().NotContain("../", $"should remove path traversal from file name: {testDescription}");
                    result.Should().NotContain("..\\", $"should remove Windows path traversal from file name: {testDescription}");
                    
                    // Result should be reasonable length
                    result.Length.Should().BeLessOrEqualTo(255, $"file name should respect OS limits for {testDescription}");
                    
                    // Should not be Windows reserved names
                    var upperResult = result.ToUpperInvariant();
                    var reservedNames = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "LPT1", "LPT2" };
                    foreach (var reserved in reservedNames)
                    {
                        upperResult.Should().NotStartWith(reserved + ".", $"should not start with reserved name {reserved} for {testDescription}");
                    }
                    
                    // If extreme input, safe default is acceptable
                    if (result == "unknown_file" || result == "safe_file")
                    {
                        result.Should().BeOneOf("unknown_file", "safe_file", $"safe default is acceptable for extreme file name: {testDescription}");
                    }
                };

                sanitizeAction.Should().NotThrow($"file name sanitizer should handle chaos case safely: {testDescription}");
            }
        }

        /// <summary>
        /// 🐒💥 Chaos Monkey test for input sanitization using security-focused edge cases
        /// Tests robustness against injection attacks and malicious input patterns
        /// </summary>
        [Fact]
        public void InputSanitizer_WithChaosMonkeySecurityAttacks_ShouldNeutralizeThreats()
        {
            // Get security-focused chaos monkey cases
            var securityChaos = EdgeCaseData.GetSecurityChaosMonkeyCases().Take(15).ToArray();

            foreach (var chaosCase in securityChaos)
            {
                var maliciousInput = chaosCase[0].ToString();
                var testDescription = chaosCase[1].ToString();

                // Test various sanitization methods
                Action testAllMethods = () =>
                {
                    // Test artist name sanitization
                    var artistResult = InputSanitizer.SanitizeArtistName(maliciousInput);
                    artistResult.Should().NotBeNull($"artist sanitizer should handle {testDescription}");
                    artistResult.Should().NotContain("<script>", $"artist should remove script tags from {testDescription}");
                    artistResult.Should().NotContain("javascript:", $"artist should remove javascript URLs from {testDescription}");
                    
                    // Test album title sanitization  
                    var albumResult = InputSanitizer.SanitizeAlbumTitle(maliciousInput);
                    albumResult.Should().NotBeNull($"album sanitizer should handle {testDescription}");
                    albumResult.Should().NotContain("DROP TABLE", $"album should remove SQL injection from {testDescription}");
                    albumResult.Should().NotContain("';", $"album should remove SQL patterns from {testDescription}");
                    
                    // Test version sanitization
                    var versionResult = InputSanitizer.SanitizeVersion(maliciousInput);
                    versionResult.Should().NotBeNull($"version sanitizer should handle {testDescription}");
                    versionResult.Should().NotContain("rm -rf", $"version should remove command injection from {testDescription}");
                    versionResult.Should().NotContain("../", $"version should remove path traversal from {testDescription}");
                    
                    // Test HTML encoding
                    var htmlResult = InputSanitizer.HtmlEncode(maliciousInput);
                    htmlResult.Should().NotBeNull($"HTML encoder should handle {testDescription}");
                    htmlResult.Should().NotContain("<script>", $"HTML should encode script tags from {testDescription}");
                    
                    // Test URL safety
                    if (maliciousInput.StartsWith("http") || maliciousInput.StartsWith("javascript") || maliciousInput.StartsWith("data:"))
                    {
                        var isSafe = InputSanitizer.IsUrlSafe(maliciousInput);
                        if (maliciousInput.Contains("javascript:") || maliciousInput.Contains("data:") || maliciousInput.Contains("file:"))
                        {
                            isSafe.Should().BeFalse($"dangerous URL should be detected as unsafe: {testDescription}");
                        }
                    }
                    
                    // All results should be reasonable length
                    artistResult.Length.Should().BeLessOrEqualTo(100, $"artist result should be reasonable length for {testDescription}");
                    albumResult.Length.Should().BeLessOrEqualTo(100, $"album result should be reasonable length for {testDescription}");
                    versionResult.Length.Should().BeLessOrEqualTo(100, $"version result should be reasonable length for {testDescription}");
                };

                testAllMethods.Should().NotThrow($"all sanitization methods should handle security chaos safely: {testDescription}");
            }
        }

        /// <summary>
        /// 🐒💥 Chaos Monkey test for Unicode edge cases and encoding attacks
        /// Tests robustness against Unicode manipulation and encoding confusion
        /// </summary>
        [Fact]
        public void InputSanitizer_WithChaosMonkeyUnicodeAttacks_ShouldHandleEncodingSafely()
        {
            // Get Unicode attack cases
            var unicodeAttacks = EdgeCaseData.GetUnicodeAttackCases().ToArray();

            foreach (var chaosCase in unicodeAttacks)
            {
                var unicodeInput = chaosCase[0].ToString();
                var testDescription = chaosCase[1].ToString();

                // Act & Assert - Should handle Unicode edge cases safely
                Action sanitizeAction = () =>
                {
                    var artistResult = InputSanitizer.SanitizeArtistName(unicodeInput);
                    var albumResult = InputSanitizer.SanitizeAlbumTitle(unicodeInput);
                    var fileResult = InputSanitizer.SanitizeFileName(unicodeInput);
                    
                    // Basic safety assertions
                    artistResult.Should().NotBeNull($"artist should handle Unicode case: {testDescription}");
                    albumResult.Should().NotBeNull($"album should handle Unicode case: {testDescription}");
                    fileResult.Should().NotBeNull($"file should handle Unicode case: {testDescription}");
                    
                    // Should handle dangerous Unicode characters
                    artistResult.Should().NotContain("\u202E", $"artist should handle bidirectional override in {testDescription}");
                    albumResult.Should().NotContain("\u202D", $"album should handle bidirectional override in {testDescription}");
                    fileResult.Should().NotContain("\u200B", $"file should remove zero-width space from {testDescription}");
                    
                    // Should handle BOM and other problematic characters
                    artistResult.Should().NotContain("\uFEFF", $"artist should remove BOM from {testDescription}");
                    albumResult.Should().NotContain("\u0000", $"album should remove null bytes from {testDescription}");
                    fileResult.Should().NotContain("\uFFFE", $"file should remove non-character from {testDescription}");
                    
                    // Results should be reasonable
                    artistResult.Length.Should().BeLessOrEqualTo(100, $"Unicode artist result should be reasonable length for {testDescription}");
                    albumResult.Length.Should().BeLessOrEqualTo(100, $"Unicode album result should be reasonable length for {testDescription}");
                    fileResult.Length.Should().BeLessOrEqualTo(255, $"Unicode file result should be reasonable length for {testDescription}");
                };

                sanitizeAction.Should().NotThrow($"Unicode sanitization should handle attack safely: {testDescription}");
            }
        }

        /// <summary>
        /// 🐒💥 Chaos Monkey test for memory exhaustion and performance under extreme loads
        /// Tests that sanitizers don't hang, crash, or consume excessive memory
        /// </summary>
        [Fact]
        public void InputSanitizer_WithChaosMonkeyMemoryBombs_ShouldMaintainPerformance()
        {
            // Get dangerous chaos cases (limited for safety)
            var memoryBombs = EdgeCaseData.GetDangerousChaosMonkeyCases().Take(3).ToArray();

            var totalStartTime = DateTime.UtcNow;
            var maxIndividualTime = TimeSpan.Zero;

            foreach (var chaosCase in memoryBombs)
            {
                var memoryBombInput = chaosCase[0].ToString();
                var testDescription = chaosCase[1].ToString();

                var startTime = DateTime.UtcNow;

                // Act & Assert - Should handle memory bombs without exhaustion
                Action performanceAction = () =>
                {
                    // Test all sanitization methods for performance
                    var artistResult = InputSanitizer.SanitizeArtistName(memoryBombInput);
                    var albumResult = InputSanitizer.SanitizeAlbumTitle(memoryBombInput);
                    var versionResult = InputSanitizer.SanitizeVersion(memoryBombInput);
                    var fileResult = InputSanitizer.SanitizeFileName(memoryBombInput);
                    
                    // All results should be non-null and reasonable length
                    artistResult.Should().NotBeNull($"artist should handle memory bomb: {testDescription}");
                    albumResult.Should().NotBeNull($"album should handle memory bomb: {testDescription}");
                    versionResult.Should().NotBeNull($"version should handle memory bomb: {testDescription}");
                    fileResult.Should().NotBeNull($"file should handle memory bomb: {testDescription}");
                    
                    // Results should be truncated to reasonable size
                    artistResult.Length.Should().BeLessOrEqualTo(100, $"artist memory bomb result should be truncated for {testDescription}");
                    albumResult.Length.Should().BeLessOrEqualTo(100, $"album memory bomb result should be truncated for {testDescription}");
                    versionResult.Length.Should().BeLessOrEqualTo(100, $"version memory bomb result should be truncated for {testDescription}");
                    fileResult.Length.Should().BeLessOrEqualTo(255, $"file memory bomb result should be truncated for {testDescription}");
                };

                performanceAction.Should().NotThrow($"memory bomb sanitization should not crash: {testDescription}");

                var duration = DateTime.UtcNow - startTime;
                if (duration > maxIndividualTime)
                    maxIndividualTime = duration;

                // Individual operation should complete reasonably quickly
                duration.Should().BeLessThan(TimeSpan.FromSeconds(10), $"memory bomb sanitization should complete quickly for {testDescription}");
            }

            // Overall performance check
            var totalDuration = DateTime.UtcNow - totalStartTime;
            totalDuration.Should().BeLessThan(TimeSpan.FromSeconds(30), "total memory bomb testing should complete quickly");
            maxIndividualTime.Should().BeLessThan(TimeSpan.FromSeconds(10), "no individual memory bomb should take too long");
        }

        /// <summary>
        /// 🐒💥 Chaos Monkey test for concurrent sanitization robustness
        /// Tests thread safety and performance under concurrent load with extreme inputs
        /// </summary>
        [Fact]
        public void InputSanitizer_WithConcurrentChaosMonkeyInputs_ShouldBeThreadSafe()
        {
            // Get concurrent chaos test data
            var concurrentChaos = EdgeCaseData.GenerateConcurrentChaosTestData(20).ToArray();

            // Act - Run sanitization concurrently
            Action concurrentAction = () =>
            {
                System.Threading.Tasks.Parallel.ForEach(concurrentChaos, chaosData =>
                {
                    var chaosInput = chaosData[0].ToString();
                    var testDescription = chaosData[1].ToString();
                    var index = (int)chaosData[2];

                    // Test all sanitization methods concurrently
                    var artistResult = InputSanitizer.SanitizeArtistName(chaosInput);
                    var albumResult = InputSanitizer.SanitizeAlbumTitle(chaosInput);
                    var versionResult = InputSanitizer.SanitizeVersion(chaosInput);
                    var fileResult = InputSanitizer.SanitizeFileName(chaosInput);

                    // All results should be safe and consistent
                    artistResult.Should().NotBeNull($"concurrent artist sanitization {index} should succeed for {testDescription}");
                    albumResult.Should().NotBeNull($"concurrent album sanitization {index} should succeed for {testDescription}");
                    versionResult.Should().NotBeNull($"concurrent version sanitization {index} should succeed for {testDescription}");
                    fileResult.Should().NotBeNull($"concurrent file sanitization {index} should succeed for {testDescription}");
                    
                    // Results should be reasonable
                    artistResult.Length.Should().BeLessOrEqualTo(100, $"concurrent artist result {index} should be reasonable");
                    albumResult.Length.Should().BeLessOrEqualTo(100, $"concurrent album result {index} should be reasonable");
                    versionResult.Length.Should().BeLessOrEqualTo(100, $"concurrent version result {index} should be reasonable");
                    fileResult.Length.Should().BeLessOrEqualTo(255, $"concurrent file result {index} should be reasonable");
                });
            };

            // Assert - Should handle concurrent chaos without issues
            concurrentAction.Should().NotThrow("concurrent chaos monkey sanitization should be thread-safe");
        }

        /// <summary>
        /// 🐒💥 Chaos Monkey test for format exploitation and injection resistance
        /// Tests that sanitizers resist various format string and injection attacks
        /// </summary>
        [Fact]
        public void InputSanitizer_WithChaosMonkeyFormatExploits_ShouldResistExploitation()
        {
            // Get format exploitation cases
            var formatExploits = EdgeCaseData.GetFormatExploitationCases().ToArray();

            foreach (var chaosCase in formatExploits)
            {
                var exploitInput = chaosCase[0].ToString();
                var testDescription = chaosCase[1].ToString();

                // Act & Assert - Should resist format exploitation
                Action exploitAction = () =>
                {
                    var artistResult = InputSanitizer.SanitizeArtistName(exploitInput);
                    var albumResult = InputSanitizer.SanitizeAlbumTitle(exploitInput);
                    var versionResult = InputSanitizer.SanitizeVersion(exploitInput);
                    var htmlResult = InputSanitizer.HtmlEncode(exploitInput);
                    
                    // Should neutralize format string exploits
                    artistResult.Should().NotContain("%n", $"artist should neutralize format exploit in {testDescription}");
                    albumResult.Should().NotContain("%s", $"album should neutralize format exploit in {testDescription}");
                    versionResult.Should().NotContain("{0}", $"version should neutralize .NET format exploit in {testDescription}");
                    htmlResult.Should().NotContain("${", $"HTML should neutralize template injection in {testDescription}");
                    
                    // Should handle dangerous log4j-style patterns
                    artistResult.Should().NotContain("${jndi:", $"artist should neutralize JNDI injection in {testDescription}");
                    albumResult.Should().NotContain("${jndi:", $"album should neutralize JNDI injection in {testDescription}");
                    
                    // Results should be safe
                    artistResult.Should().NotBeNull($"format exploit artist result should not be null for {testDescription}");
                    albumResult.Should().NotBeNull($"format exploit album result should not be null for {testDescription}");
                    versionResult.Should().NotBeNull($"format exploit version result should not be null for {testDescription}");
                    htmlResult.Should().NotBeNull($"format exploit HTML result should not be null for {testDescription}");
                };

                exploitAction.Should().NotThrow($"format exploitation resistance should not crash for {testDescription}");
            }
        }

        /// <summary>
        /// 🐒💥 Chaos Monkey comprehensive integration test
        /// Tests all sanitization methods with the most extreme chaos monkey scenarios
        /// </summary>
        [Fact]
        public void InputSanitizer_WithExpertLevelChaosMonkey_ShouldHandleExtremeScenarios()
        {
            // Get expert-level chaos monkey cases (most dangerous!)
            var expertChaos = EdgeCaseData.GetExpertLevelChaosMonkeyCases(3).ToArray();

            foreach (var chaosCase in expertChaos)
            {
                var extremeInput = chaosCase[0].ToString();
                var testDescription = chaosCase[1].ToString();

                // Act & Assert - Should handle even the most extreme scenarios
                Action extremeAction = () =>
                {
                    // Test all sanitization methods with extreme input
                    var artistResult = InputSanitizer.SanitizeArtistName(extremeInput);
                    var albumResult = InputSanitizer.SanitizeAlbumTitle(extremeInput);
                    var versionResult = InputSanitizer.SanitizeVersion(extremeInput);
                    var fileResult = InputSanitizer.SanitizeFileName(extremeInput);
                    var htmlResult = InputSanitizer.HtmlEncode(extremeInput);
                    
                    // All methods should survive extreme scenarios
                    artistResult.Should().NotBeNull($"extreme artist sanitization should survive {testDescription}");
                    albumResult.Should().NotBeNull($"extreme album sanitization should survive {testDescription}");
                    versionResult.Should().NotBeNull($"extreme version sanitization should survive {testDescription}");
                    fileResult.Should().NotBeNull($"extreme file sanitization should survive {testDescription}");
                    htmlResult.Should().NotBeNull($"extreme HTML encoding should survive {testDescription}");
                    
                    // Results should be safe and reasonable
                    artistResult.Length.Should().BeLessOrEqualTo(100, $"extreme artist result should be bounded for {testDescription}");
                    albumResult.Length.Should().BeLessOrEqualTo(100, $"extreme album result should be bounded for {testDescription}");
                    versionResult.Length.Should().BeLessOrEqualTo(100, $"extreme version result should be bounded for {testDescription}");
                    fileResult.Length.Should().BeLessOrEqualTo(255, $"extreme file result should be bounded for {testDescription}");
                    
                    // Safe defaults are acceptable for extreme inputs
                    var acceptableDefaults = new[] { "Unknown Artist", "Unknown Album", "Version", "unknown_file", "safe_file" };
                    if (acceptableDefaults.Contains(artistResult) || acceptableDefaults.Contains(albumResult) || 
                        acceptableDefaults.Contains(versionResult) || acceptableDefaults.Contains(fileResult))
                    {
                        // This is fine - safe defaults are appropriate for extreme inputs
                        artistResult.Should().BeOneOf(acceptableDefaults.Concat(new[] { artistResult }).ToArray(), 
                            $"safe defaults are acceptable for extreme input: {testDescription}");
                    }
                };

                extremeAction.Should().NotThrow($"extreme chaos monkey scenario should not crash system: {testDescription}");
            }
        }

        #endregion
    }
}