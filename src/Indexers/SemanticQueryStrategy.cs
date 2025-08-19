using System;
using System.Collections.Generic;
using System.Linq;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Semantic-aware query strategy that intelligently determines search approach
    /// based on album content analysis rather than just complexity scoring
    /// </summary>
    public class SemanticQueryStrategy
    {
        private readonly QueryComplexityClassifier _complexityClassifier;
        private readonly AlbumComponentClassifier _componentClassifier;
        private readonly Logger _logger;

        public SemanticQueryStrategy(Logger logger = null)
        {
            _complexityClassifier = new QueryComplexityClassifier();
            _componentClassifier = new AlbumComponentClassifier();
            _logger = logger;
        }

        /// <summary>
        /// Sanitizes input to prevent command injection, XSS, and SQL injection attacks
        /// </summary>
        private string SanitizeInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            // Limit input length to prevent buffer overflow attacks
            const int maxLength = 1000;
            if (input.Length > maxLength)
            {
                input = input.Substring(0, maxLength);
            }

            var sanitized = input;
            
            // Remove HTML/XSS patterns first
            var xssPatterns = new[] {
                "<script", "</script", "javascript:", "onerror", "onmouseover", "onclick",
                "onload", "alert(", "<img", "<iframe", "<object", "<embed", "<svg",
                "</title>", "document.", "window.", "eval("
            };
            
            foreach (var pattern in xssPatterns)
            {
                while (sanitized.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    var index = sanitized.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                    sanitized = sanitized.Remove(index, pattern.Length);
                }
            }
            
            // Remove SQL injection patterns
            var sqlPatterns = new[] {
                "UNION", "SELECT", "DELETE", "INSERT", "UPDATE", "DROP", "CREATE",
                "ALTER", "EXEC", "EXECUTE", "--", "/*", "*/", "xp_", "sp_"
            };
            
            foreach (var pattern in sqlPatterns)
            {
                while (sanitized.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    var index = sanitized.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                    sanitized = sanitized.Remove(index, pattern.Length);
                }
            }

            // Remove dangerous characters used in various injection attacks
            var dangerous = new[] { ";", "|", "&", "$", "`", "\"", "'", "<", ">", "\n", "\r", "\0" };
            
            foreach (var ch in dangerous)
            {
                sanitized = sanitized.Replace(ch, "");
            }
            
            // Remove path traversal patterns
            sanitized = sanitized.Replace("..", "");
            sanitized = sanitized.Replace("//", "/");
            sanitized = sanitized.Replace("\\\\", "\\");
            sanitized = sanitized.Replace("\\", "");

            // Remove common command injection patterns and system paths
            var cmdPatterns = new[] { 
                "rm ", "del ", "format ", "nc ", "wget ", "curl ", "powershell", 
                "cmd ", "bash ", "sh ", "-rf", "-f", "-r", "-e", "-p", "-l",
                "/etc/", "/bin/", "/usr/", "/var/", "Windows", "System32", "C:"
            };
            
            foreach (var pattern in cmdPatterns)
            {
                if (sanitized.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    sanitized = sanitized.Replace(pattern, "", StringComparison.OrdinalIgnoreCase);
                }
            }

            return sanitized.Trim();
        }

        /// <summary>
        /// Determines optimal query strategy based on semantic content analysis
        /// </summary>
        /// <param name="artist">Artist name</param>
        /// <param name="album">Album title</param>
        /// <returns>QueryStrategy with semantic-aware settings</returns>
        public QueryStrategy DetermineStrategy(string artist, string album)
        {
            // Analyze album components semantically
            var components = _componentClassifier.ClassifyComponents(album);
            var hasVersionDescriptor = components.Values.Any(c => c == AlbumComponentType.VersionDescriptor);
            var hasEditionMarker = components.Values.Any(c => c == AlbumComponentType.EditionMarker);
            var preservedTerms = _componentClassifier.GetPreservedTerms(album);
            var cleaningLevel = _componentClassifier.RecommendCleaningLevel(album);
            
            // Get traditional complexity for fallback decisions
            var complexity = _complexityClassifier.ClassifyComplexity(artist, album);
            
            _logger?.Debug("Semantic analysis for '{0} - {1}': HasVersionDescriptor={2}, HasEditionMarker={3}, CleaningLevel={4}, Complexity={5}", 
                          artist, album, hasVersionDescriptor, hasEditionMarker, cleaningLevel, complexity);

            // Determine strategy based on semantic analysis
            if (hasVersionDescriptor)
            {
                // Albums with version descriptors need special handling
                return new QueryStrategy
                {
                    CleaningLevel = CleaningLevel.Minimal,
                    PreserveTerms = preservedTerms,
                    QueryVariants = GenerateVersionAwareQueryCount(album, hasEditionMarker),
                    OptimizationLevel = OptimizationLevel.Conservative,
                    RequireExactMatch = true, // First query should be exact for version descriptors
                    Rationale = $"Version descriptor detected: {string.Join(", ", preservedTerms.Where(t => IsVersionDescriptor(t)))}"
                };
            }
            else if (hasEditionMarker && cleaningLevel == CleaningLevel.Moderate)
            {
                // Albums with edition markers can have moderate optimization
                return new QueryStrategy
                {
                    CleaningLevel = CleaningLevel.Moderate,
                    PreserveTerms = preservedTerms,
                    QueryVariants = 2, // Primary + one alternative
                    OptimizationLevel = OptimizationLevel.Balanced,
                    RequireExactMatch = false,
                    Rationale = "Edition marker detected, moderate cleaning appropriate"
                };
            }
            else if (complexity == QueryComplexity.Simple && !hasVersionDescriptor && !hasEditionMarker)
            {
                // Simple albums without special terms can be aggressively optimized
                return new QueryStrategy
                {
                    CleaningLevel = CleaningLevel.Aggressive,
                    PreserveTerms = new List<string>(),
                    QueryVariants = 1, // Single optimized query
                    OptimizationLevel = OptimizationLevel.Maximum,
                    RequireExactMatch = false,
                    Rationale = "Simple album, maximum optimization safe"
                };
            }
            else
            {
                // Default balanced approach for unclear cases
                return new QueryStrategy
                {
                    CleaningLevel = CleaningLevel.Moderate,
                    PreserveTerms = preservedTerms,
                    QueryVariants = 2,
                    OptimizationLevel = OptimizationLevel.Balanced,
                    RequireExactMatch = false,
                    Rationale = "Mixed complexity, balanced approach"
                };
            }
        }

        /// <summary>
        /// Builds context-aware queries based on semantic strategy
        /// </summary>
        /// <param name="artist">Artist name</param>
        /// <param name="album">Album title</param>
        /// <param name="strategy">Pre-determined strategy</param>
        /// <returns>List of optimized queries</returns>
        public List<string> BuildQueriesForStrategy(string artist, string album, QueryStrategy strategy)
        {
            // Sanitize inputs to prevent command injection
            artist = SanitizeInput(artist);
            album = SanitizeInput(album);
            
            var queries = new List<string>();

            switch (strategy.CleaningLevel)
            {
                case CleaningLevel.None:
                    // Exact match only
                    queries.Add($"{artist} {album}");
                    break;

                case CleaningLevel.Minimal:
                    // Preserve all meaningful terms
                    var minimalClean = CleanQueryWithPreservation(album, strategy.PreserveTerms);
                    queries.Add($"{artist} {minimalClean}");
                    
                    // For version descriptors, also try album-only search
                    if (strategy.PreserveTerms.Any(t => IsVersionDescriptor(t)))
                    {
                        queries.Add(minimalClean);
                        
                        // Also try with artist last for some version descriptors
                        if (strategy.QueryVariants >= 3)
                        {
                            queries.Add($"{minimalClean} {artist}");
                        }
                    }
                    break;

                case CleaningLevel.Moderate:
                    // Remove some edition markers but preserve core content
                    var moderateClean = CleanQuerySelectively(album, strategy.PreserveTerms);
                    queries.Add($"{artist} {moderateClean}");
                    if (strategy.QueryVariants >= 2)
                    {
                        queries.Add($"{artist} - {moderateClean}");
                    }
                    break;

                case CleaningLevel.Aggressive:
                    // Standard aggressive cleaning for simple albums
                    var aggressiveClean = CleanQueryAggressive(album);
                    queries.Add($"{artist} {aggressiveClean}");
                    break;
            }

            // Ensure we don't exceed the requested number of variants
            return queries.Take(strategy.QueryVariants).ToList();
        }

        /// <summary>
        /// Creates a test case specifically for the reported bug
        /// </summary>
        /// <param name="artist">"070 Shake"</param>
        /// <param name="album">"Modus Vivendi Instrumental"</param>
        /// <returns>Optimized queries that should find the album</returns>
        public List<string> BuildQueriesForBugCase(string artist, string album)
        {
            // Sanitize inputs first
            artist = SanitizeInput(artist);
            album = SanitizeInput(album);
            
            var strategy = DetermineStrategy(artist, album);
            var queries = BuildQueriesForStrategy(artist, album, strategy);
            
            _logger?.Info("🐛 BUG FIX TEST: Generated {0} queries for '{1} - {2}': {3}", 
                         queries.Count, artist, album, string.Join(", ", queries));
            
            return queries;
        }

        private int GenerateVersionAwareQueryCount(string album, bool hasEditionMarker)
        {
            // Version descriptors need more query variants to be found correctly
            if (album.Contains("Instrumental", StringComparison.OrdinalIgnoreCase) ||
                album.Contains("Live", StringComparison.OrdinalIgnoreCase) ||
                album.Contains("Acoustic", StringComparison.OrdinalIgnoreCase))
            {
                return hasEditionMarker ? 3 : 2; // More variants if also has edition markers
            }
            
            return 2; // Standard version descriptor handling
        }

        private bool IsVersionDescriptor(string term)
        {
            var versionDescriptors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Instrumental", "Acoustic", "Live", "Unplugged", "Demo", "Sessions",
                "Orchestra", "Symphony", "Quartet", "Ensemble", "Radio", "Mix", "Remix"
            };
            
            return versionDescriptors.Contains(term);
        }

        private string CleanQueryWithPreservation(string query, List<string> preserveTerms)
        {
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;

            // For minimal cleaning, just remove obvious noise but preserve all meaningful terms
            var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cleaned = new List<string>();

            foreach (var word in words)
            {
                var cleanWord = word.Trim('.', ',', '!', '?', '(', ')', '[', ']');
                
                // Always preserve terms flagged for preservation
                if (preserveTerms.Any(p => p.Equals(cleanWord, StringComparison.OrdinalIgnoreCase)))
                {
                    cleaned.Add(cleanWord);
                    continue;
                }
                
                // Skip years in parentheses but keep standalone years
                if (System.Text.RegularExpressions.Regex.IsMatch(cleanWord, @"^\d{4}$") && 
                    (word.Contains('(') || word.Contains('[')))
                {
                    continue;
                }
                
                // Keep everything else
                if (!string.IsNullOrWhiteSpace(cleanWord))
                {
                    cleaned.Add(cleanWord);
                }
            }

            return string.Join(" ", cleaned);
        }

        private string CleanQuerySelectively(string query, List<string> preserveTerms)
        {
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;

            // Moderate cleaning removes some edition markers but preserves core content
            var result = CleanQueryWithPreservation(query, preserveTerms);
            
            // Remove edition markers that aren't in preserve list
            var editionMarkers = new[] { "Deluxe", "Remastered", "Anniversary", "Special", "Limited" };
            foreach (var marker in editionMarkers)
            {
                if (!preserveTerms.Contains(marker, StringComparer.OrdinalIgnoreCase))
                {
                    result = System.Text.RegularExpressions.Regex.Replace(result, 
                        $@"\b{marker}\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                        .Trim();
                }
            }
            
            // Clean up extra whitespace
            return System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ").Trim();
        }

        private string CleanQueryAggressive(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;

            // Aggressive cleaning for simple albums
            var result = query;
            
            // Remove years
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\b\d{4}\b", "");
            
            // Remove common edition markers
            var editionPatterns = new[]
            {
                @"\b(deluxe|expanded|remastered|anniversary|special|limited|collector|bonus|extended)\s*(edition|version)?\b",
                @"\b(re-?master(ed)?|re-?issue|re-?release)\b"
            };
            
            foreach (var pattern in editionPatterns)
            {
                result = System.Text.RegularExpressions.Regex.Replace(result, pattern, " ", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            
            // Remove extra whitespace and normalize
            return System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ").Trim();
        }
    }

    /// <summary>
    /// Query strategy configuration
    /// </summary>
    public class QueryStrategy
    {
        public CleaningLevel CleaningLevel { get; set; }
        public List<string> PreserveTerms { get; set; } = new();
        public int QueryVariants { get; set; } = 1;
        public OptimizationLevel OptimizationLevel { get; set; }
        public bool RequireExactMatch { get; set; }
        public string Rationale { get; set; } = "";
    }

    /// <summary>
    /// Query optimization aggressiveness levels
    /// </summary>
    public enum OptimizationLevel
    {
        None,
        Conservative,
        Balanced,
        Maximum
    }
}