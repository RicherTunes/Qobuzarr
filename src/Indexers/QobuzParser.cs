using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using Lidarr.Plugin.Qobuzarr.Security;
using Lidarr.Plugin.Qobuzarr.Download;
using NLog;
using NzbDrone.Core.Qualities;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Constants;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    public class QobuzParser : IParseIndexerResponse
    {
        private readonly QobuzIndexerSettings _settings;
        private readonly Logger _logger;
        private SearchCriteriaBase _currentSearchCriteria;

        public QobuzParser(QobuzIndexerSettings settings, Logger logger)
        {
            _settings = settings;
            _logger = logger;
        }

        /// <summary>
        /// Sets the current search criteria context for intelligent title generation.
        /// This allows the parser to generate titles that match Lidarr's exact expectations.
        /// </summary>
        public void SetSearchContext(SearchCriteriaBase searchCriteria)
        {
            _currentSearchCriteria = searchCriteria;
            if (searchCriteria != null)
            {
                _logger.Debug("Parser context set: Artist='{0}', Albums={1}",
                    searchCriteria.Artist?.Name,
                    searchCriteria.Albums?.Count ?? 0);
            }
        }

        public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
        {
            var releases = new List<ReleaseInfo>();

            try
            {
                if (indexerResponse.HttpResponse.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Warn("Qobuz API returned status code: {0}", indexerResponse.HttpResponse.StatusCode);
                    return releases;
                }

                var responseContent = indexerResponse.Content;
                if (string.IsNullOrWhiteSpace(responseContent))
                {
                    _logger.Warn("Qobuz API returned empty response");
                    return releases;
                }

                // Try to parse as album search response first
                try
                {
                    var albumSearchResponse = JsonConvert.DeserializeObject<QobuzAlbumSearchResponse>(responseContent);
                    if (albumSearchResponse?.IsSuccess == true && albumSearchResponse.HasResults())
                    {
                        releases.AddRange(ParseAlbumSearchResponse(albumSearchResponse, indexerResponse.HttpRequest.Url.Query));
                    }
                }
                catch (JsonException)
                {
                    // Try parsing as general search response
                    try
                    {
                        var searchResponse = JsonConvert.DeserializeObject<QobuzSearchResponse>(responseContent);
                        if (searchResponse?.IsSuccess == true)
                        {
                            releases.AddRange(ParseGeneralSearchResponse(searchResponse, indexerResponse.HttpRequest.Url.Query));
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.Error(ex, "Failed to parse Qobuz API response");
                    }
                }

                _logger.Debug("Parsed {0} releases from Qobuz response", releases.Count);
                
                // Deduplicate releases by album ID and quality to prevent duplicates from multiple query formats
                var deduplicatedReleases = releases
                    .GroupBy(r => r.Guid) // GUID contains both album ID and quality
                    .Select(g => g.First()) // Take first occurrence of each unique album+quality combination
                    .ToList();
                    
                if (deduplicatedReleases.Count != releases.Count)
                {
                    _logger.Debug("Removed {0} duplicate releases, {1} unique releases remaining", 
                        releases.Count - deduplicatedReleases.Count, deduplicatedReleases.Count);
                }
                
                releases = deduplicatedReleases;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error parsing Qobuz response");
            }

            return releases;
        }

        private IEnumerable<ReleaseInfo> ParseAlbumSearchResponse(QobuzAlbumSearchResponse response, string originalQuery)
        {
            var releases = new List<ReleaseInfo>();

            foreach (var album in response.GetAlbums())
            {
                try
                {
                    var albumReleases = ConvertAlbumToReleases(album, originalQuery);
                    releases.AddRange(albumReleases);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to convert album {0} to release", album.Id);
                }
            }

            return releases;
        }

        private IEnumerable<ReleaseInfo> ParseGeneralSearchResponse(QobuzSearchResponse response, string originalQuery)
        {
            var releases = new List<ReleaseInfo>();

            if (response.Albums?.Items != null)
            {
                foreach (var album in response.Albums.Items)
                {
                    try
                    {
                        var albumReleases = ConvertAlbumToReleases(album, originalQuery);
                        releases.AddRange(albumReleases);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Failed to convert album {0} to release", album.Id);
                    }
                }
            }

            return releases;
        }

        private IEnumerable<ReleaseInfo> ConvertAlbumToReleases(QobuzAlbum album, string originalQuery)
        {
            var releases = new List<ReleaseInfo>();
            
            if (album == null || string.IsNullOrWhiteSpace(album.Id))
            {
                return releases;
            }

            // Apply filters
            if (!ShouldIncludeAlbum(album))
            {
                _logger.Debug("Filtered out album: {0} - {1}", album.GetArtistName(), album.GetFullTitle());
                return releases;
            }

            // Determine available audio qualities
            var qualityList = new List<QobuzAudioQuality> { QobuzAudioQuality.MP3320, QobuzAudioQuality.FLACLossless };
            
            if (album.HasHiResQuality())
            {
                qualityList.Add(QobuzAudioQuality.FLACHiRes24Bit192Khz);
                qualityList.Add(QobuzAudioQuality.FLACHiRes24Bit96kHz);
            }

            // Create one ReleaseInfo per quality
            foreach (var quality in qualityList)
            {
                var release = CreateReleaseInfoForQuality(album, quality, originalQuery);
                if (release != null)
                {
                    releases.Add(release);
                }
            }

            return releases;
        }


        private ReleaseInfo CreateReleaseInfoForQuality(QobuzAlbum album, QobuzAudioQuality quality, string originalQuery)
        {
            var year = album.ReleaseDate.Year > 1900 ? album.ReleaseDate.Year : 0;
            
            var artistName = album.GetArtistName();
            var albumTitle = album.GetFullTitle();
            
            // Ensure we have valid non-empty names
            if (string.IsNullOrWhiteSpace(artistName))
            {
                _logger.Warn("Album {0} has empty artist name, skipping", album.Id);
                return null; 
            }
            
            if (string.IsNullOrWhiteSpace(albumTitle))
            {
                _logger.Warn("Album {0} has empty title, skipping", album.Id);
                return null;
            }
            
            var release = new ReleaseInfo
            {
                // CRITICAL: Include quality in GUID to differentiate releases
                Guid = $"qobuz-{album.Id}-{(int)quality}",
                
                // Protocol assignment happens via reflection for compatibility (see TrySetDownloadProtocol)
                
                // Basic metadata - ENSURE NON-EMPTY NAMES
                Artist = artistName,
                Album = albumTitle,
                DownloadUrl = GenerateDownloadUrl(album, quality),
                InfoUrl = GenerateInfoUrl(album),
                PublishDate = album.ReleaseDate,
                Indexer = QobuzarrConstants.PluginName,
                
                // Note: Codec and Container properties are ignored by Lidarr's quality detection
                // Quality is determined solely from the Title using regex patterns
                
                // CRITICAL: Quality-specific size calculation
                Size = CalculateSizeForQuality(album, quality)
            };

            // Ensure protocol is set without binding to specific Lidarr API shape
            TrySetDownloadProtocol(release);

            // Generate quality-specific title
            release.Title = GenerateQualitySpecificTitle(album, quality, year);
            
            // Critical debugging for album mapping (only during troubleshooting)
            _logger.Debug("≡ƒöì ALBUM MAPPING: Qobuz '{0}' ({1}) ΓåÆ Title '{2}' ΓåÆ Album '{3}'", 
                album.Id, album.Title, release.Title, release.Album);
            
            // Debug context-aware matching for edition albums
            if (_currentSearchCriteria?.Albums?.Any() == true)
            {
                _logger.Debug("≡ƒÄ» Search Context Available: {0} albums in criteria", _currentSearchCriteria.Albums.Count);
                foreach (var lidarrAlbum in _currentSearchCriteria.Albums)
                {
                    _logger.Debug("   Lidarr Album: '{0}' ({1})", lidarrAlbum.Title, lidarrAlbum.ReleaseDate?.Year);
                }
            }
            else
            {
                _logger.Debug("Γ¥î No search context available - using original Qobuz title");
            }

            return release;
        }

        /// <summary>
        /// Generates title formatted for optimal Lidarr parser compatibility
        /// Uses dual-format approach: hyphen format for editions, bracket format for standard albums
        /// </summary>
        /// <remarks>
        /// Architecture rationale:
        /// - Edition albums use hyphen format to match Parser.cs:73 regex pattern
        /// - Standard albums use bracket format for backward compatibility
        /// - Context-aware matching preserves exact Lidarr titles when available
        /// - Fallback mechanisms ensure robustness when parsing fails
        /// </remarks>
        private string GenerateQualitySpecificTitle(QobuzAlbum album, QobuzAudioQuality quality, int year)
        {
            var artist = MetadataSanitizer.SanitizeArtistName(album.GetArtistName());
            var albumTitle = album.Title; // Use base title without version
            var version = album.Version?.Trim(); // Get version separately
            
            // CONTEXT-AWARE: Use exact Lidarr title if available
            if (_currentSearchCriteria?.Albums?.Any() == true)
            {
                var targetAlbum = FindBestMatchingAlbum(album, _currentSearchCriteria.Albums, year);
                if (targetAlbum != null)
                {
                    _logger.Debug("Using exact Lidarr title for album {0}: '{1}' -> '{2}'",
                        album.Id, album.Title, targetAlbum.Title);
                    albumTitle = targetAlbum.Title; // Use EXACT title from Lidarr
                    year = targetAlbum.ReleaseDate?.Year ?? year;
                    version = null; // Don't double-add version if Lidarr title already has it
                }
            }
            
            // Generate quality string
            var formatStr = quality switch
            {
                QobuzAudioQuality.MP3320 => "MP3 320kbps",
                QobuzAudioQuality.FLACLossless => "FLAC",
                QobuzAudioQuality.FLACHiRes24Bit96kHz => "FLAC 24bit 96kHz",
                QobuzAudioQuality.FLACHiRes24Bit192Khz => "FLAC 24bit 192kHz",
                _ => "Unknown"
            };
            
            // ARCHITECTURAL DECISION: Dual-format title generation based on album type
            // Edition albums use hyphen format to trigger Lidarr's version extraction (Parser.cs:73)
            // Standard albums use existing bracket format for backward compatibility
            
            // Check for edition info in Version field OR album title
            var hasVersionField = !string.IsNullOrWhiteSpace(version) && ContainsEditionKeywords(version);
            var hasEditionInTitle = ContainsEditionKeywords(albumTitle);
            
            _logger.Trace("≡ƒöì EDITION CHECK: Album='{0}', HasEdition={1}", albumTitle, hasVersionField || hasEditionInTitle);
            
            if (hasVersionField || hasEditionInTitle)
            {
                // Extract version from title if not in Version field
                var versionToUse = version;
                var cleanAlbumTitle = albumTitle;
                
                if (string.IsNullOrWhiteSpace(versionToUse) && hasEditionInTitle)
                {
                    // Extract edition info from title: "Album (Deluxe Edition)" ΓåÆ version="Deluxe Edition"
                    versionToUse = ExtractVersionFromTitle(albumTitle);
                    cleanAlbumTitle = albumTitle.Replace($"({versionToUse})", "").Replace($"[{versionToUse}]", "").Trim();
                    _logger.Debug("Extracted version from title: '{0}' ΓåÆ album='{1}', version='{2}'", 
                        albumTitle, cleanAlbumTitle, versionToUse);
                }
                
                // Test multiple elegant formats to find the best one
                var formats = new[]
                {
                    $"{artist} - {cleanAlbumTitle} [{versionToUse}] - WEB - {year}",           // Bracket format
                    $"{artist} - {cleanAlbumTitle} ({versionToUse}) - WEB - {year}",          // Parentheses format  
                    $"{artist} - {cleanAlbumTitle} ΓÇó {versionToUse} ΓÇó WEB ΓÇó {year}",          // Bullet format
                    $"{artist}-{cleanAlbumTitle}-[{versionToUse}]-WEB-{year}",                // Mixed hyphen-bracket
                    $"{artist}-{cleanAlbumTitle}-{versionToUse}-WEB-{year}"                   // Original hyphen
                };
                
                // For now, use the first (bracket) format - most elegant and likely to work
                var yearStrEd = year > 0 ? $" ({year})" : string.Empty; var sanitizedVersion = (versionToUse ?? string.Empty).Replace("[", "(").Replace("]", ")"); var chosenFormat = $"{artist} - {cleanAlbumTitle}{yearStrEd} [{sanitizedVersion}] [{formatStr}] [WEB]";
                _logger.Debug("≡ƒÄ» EDITION ALBUM: Using elegant format for '{0}'", albumTitle);
                return chosenFormat;
            }
            
            // Standard format for non-edition albums
            var yearStr = year > 0 ? $" ({year})" : "";
            var explicitStr = album.ParentalWarning ? " [Explicit]" : "";
            var liveIndicator = IsLiveAlbum(albumTitle) ? " [LIVE]" : "";
            
            var standardTitle = $"{artist} - {albumTitle}{yearStr}{explicitStr}{liveIndicator} [{formatStr}] [WEB]";
            _logger.Trace("Standard album format: '{0}'", standardTitle);
            return standardTitle;
        }
        
        /// <summary>
        /// Generates hyphen-format title for edition albums to trigger Lidarr's version extraction
        /// </summary>
        /// <remarks>
        /// Format validation:
        /// - Matches Parser.cs:73 regex pattern for version extraction
        /// - Sanitizes components to prevent parser confusion
        /// - Validates length constraints for robust parsing
        /// - Handles edge cases like missing years or special characters
        /// </remarks>
        private string GenerateHyphenFormatTitle(string artist, string albumTitle, string version, string formatStr, int year)
        {
            // Sanitize components to prevent parser issues
            artist = SanitizeForHyphenFormat(artist);
            albumTitle = SanitizeForHyphenFormat(albumTitle);
            version = SanitizeForHyphenFormat(version);
            
            // Handle missing year gracefully
            var yearStr = year > 1900 ? year.ToString() : DateTime.Now.Year.ToString();
            
            // Primary format: Artist-Album-Version-Source-Year
            // This matches the regex at Parser.cs:73 for version extraction
            var primaryFormat = $"{artist}-{albumTitle}-{version}-WEB-{yearStr}";
            
            // Validate format won't break parser (basic sanity checks)
            if (primaryFormat.Length > 500) // Excessive length might cause issues
            {
                _logger.Warn("Hyphen format title too long ({0} chars), truncating components", primaryFormat.Length);
                
                // Intelligently truncate to preserve important info
                if (version.Length > 50)
                {
                    version = version.Substring(0, 47) + "...";
                }
                if (albumTitle.Length > 100)
                {
                    albumTitle = albumTitle.Substring(0, 97) + "...";
                }
                
                primaryFormat = $"{artist}-{albumTitle}-{version}-WEB-{yearStr}";
            }
            
            // Final validation pass
            primaryFormat = ValidateHyphenFormat(primaryFormat);
            
            // Log format for debugging
            _logger.Trace("Hyphen format components: Artist='{0}', Album='{1}', Version='{2}', Year='{3}'",
                artist, albumTitle, version, yearStr);
            
            return primaryFormat;
        }
        
        /// <summary>
        /// Sanitizes text for use in hyphen-format titles
        /// </summary>
        private string SanitizeForHyphenFormat(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "Unknown";
                
            // Replace problematic characters that might confuse parser
            text = text.Replace("/", " ");
            text = text.Replace("\\", " ");
            text = text.Replace(":", " ");
            text = text.Replace("|", " ");
            text = text.Replace("?", "");
            text = text.Replace("*", "");
            text = text.Replace("<", "");
            text = text.Replace(">", "");
            text = text.Replace("\"", "'");
            
            // Normalize whitespace
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            
            // Ensure no leading/trailing hyphens
            text = text.Trim('-');
            
            return text;
        }
        
        /// <summary>
        /// Final validation of hyphen format to ensure parser compatibility
        /// </summary>
        private string ValidateHyphenFormat(string format)
        {
            // Ensure no double hyphens that might confuse parser
            format = System.Text.RegularExpressions.Regex.Replace(format, @"-{2,}", "-");
            
            // Ensure format has expected structure (at least 4 hyphens for Artist-Album-Version-Source-Year)
            var hyphenCount = format.Count(c => c == '-');
            if (hyphenCount < 4)
            {
                _logger.Warn("Hyphen format has insufficient delimiters ({0}), format may not parse correctly: '{1}'",
                    hyphenCount, format);
            }
            
            return format;
        }

        /// <summary>
        /// Comprehensive edition detection for all album variant types
        /// Identifies albums requiring special title formatting for Lidarr parser compatibility
        /// </summary>
        private bool ContainsEditionKeywords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
                
            // Comprehensive edition patterns based on production data analysis
            var editionKeywords = new[] 
            { 
                // Core edition types
                "deluxe", "edition", "remaster", "anniversary", "expanded", 
                "special", "collector", "limited", "bonus",
                
                // Live album indicators
                "live at", "live in", "live", "concert", "unplugged", "acoustic",
                
                // Format variants  
                "remix", "instrumental", "extended", "radio",
                
                // Special releases
                "legacy", "archive", "complete", "sessions", "demos"
            };
            
            var hasEdition = editionKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            
            if (hasEdition)
            {
                _logger.Trace("Edition keywords detected in '{0}'", text);
            }
            
            return hasEdition;
        }

        private string ExtractVersionFromTitle(string title)
        {
            // Extract content from parentheses: "Album (Deluxe Edition)" ΓåÆ "Deluxe Edition"
            var parenthesesMatch = System.Text.RegularExpressions.Regex.Match(title, @"\(([^)]+)\)");
            if (parenthesesMatch.Success)
            {
                var content = parenthesesMatch.Groups[1].Value;
                if (ContainsEditionKeywords(content))
                {
                    return content;
                }
            }
            
            // Extract content from brackets: "Album [Live at Venue]" ΓåÆ "Live at Venue"  
            var bracketsMatch = System.Text.RegularExpressions.Regex.Match(title, @"\[([^\]]+)\]");
            if (bracketsMatch.Success)
            {
                var content = bracketsMatch.Groups[1].Value;
                if (ContainsEditionKeywords(content))
                {
                    return content;
                }
            }
            
            return "";
        }

        private bool IsLiveAlbum(string albumTitle)
        {
            var liveTerms = new[] { " live", "(live)", "[live]", "live at", "live in", "concert", "unplugged" };
            return liveTerms.Any(term => albumTitle.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Finds the best matching album from Lidarr's search criteria based on metadata.
        /// This ensures we use the exact title format that Lidarr expects.
        /// </summary>
        private NzbDrone.Core.Music.Album FindBestMatchingAlbum(QobuzAlbum qobuzAlbum, List<NzbDrone.Core.Music.Album> lidarrAlbums, int qobuzYear)
        {
            if (lidarrAlbums == null || !lidarrAlbums.Any())
                return null;

            // CRITICAL FIX: Don't use context for unrelated albums
            // We should only use the Lidarr title if the Qobuz album actually matches
            // the search criteria, not for ALL albums returned by Qobuz
            
            var qobuzTitle = qobuzAlbum.GetFullTitle().ToLowerInvariant();
            var qobuzIsLive = IsLiveAlbum(qobuzAlbum.GetFullTitle());
            
            // Step 1: Try to find exact title match first (case insensitive)
            var titleMatches = lidarrAlbums.Where(a => 
            {
                if (a.Title == null) return false;
                var lidarrTitle = a.Title.ToLowerInvariant();
                
                // Check for substantial overlap in titles
                return CalculateTitleSimilarity(qobuzTitle, lidarrTitle) > 0.7;
            }).ToList();
            
            if (titleMatches.Count == 1)
            {
                _logger.Debug("Found title match for Qobuz '{0}': using Lidarr '{1}'", 
                    qobuzAlbum.GetFullTitle(), titleMatches.First().Title);
                return titleMatches.First();
            }
            
            // Step 2: If we have multiple title matches, disambiguate by year
            if (titleMatches.Count > 1)
            {
                var yearAndTitleMatches = titleMatches.Where(a => 
                    a.ReleaseDate?.Year == qobuzYear || 
                    (qobuzYear == 0 && a.ReleaseDate == null)).ToList();
                    
                if (yearAndTitleMatches.Count == 1)
                {
                    _logger.Debug("Found year+title match for Qobuz '{0}': using Lidarr '{1}'", 
                        qobuzAlbum.GetFullTitle(), yearAndTitleMatches.First().Title);
                    return yearAndTitleMatches.First();
                }
                else if (yearAndTitleMatches.Any())
                {
                    // Multiple matches, pick the one with best title similarity
                    var bestMatch = yearAndTitleMatches
                        .OrderByDescending(a => CalculateTitleSimilarity(qobuzTitle, a.Title?.ToLowerInvariant() ?? ""))
                        .First();
                    _logger.Debug("Found best match for Qobuz '{0}': using Lidarr '{1}'", 
                        qobuzAlbum.GetFullTitle(), bestMatch.Title);
                    return bestMatch;
                }
            }

            // Step 3: Only apply live album matching if titles are similar
            if (qobuzIsLive)
            {
                var liveMatches = lidarrAlbums.Where(a => 
                    IsLiveAlbum(a.Title) && 
                    CalculateTitleSimilarity(qobuzTitle, a.Title?.ToLowerInvariant() ?? "") > 0.5).ToList();

                if (liveMatches.Count == 1)
                {
                    _logger.Debug("Found live album match for Qobuz '{0}': using Lidarr '{1}'", 
                        qobuzAlbum.GetFullTitle(), liveMatches.First().Title);
                    return liveMatches.First();
                }
            }

            // Step 4: NO FALLBACK - if we can't find a good match, don't use context
            // This prevents the bug where all albums get the same title
            _logger.Debug("No matching Lidarr album found for Qobuz '{0}', using original title", 
                qobuzAlbum.GetFullTitle());
            return null; // Return null to indicate no match found
        }
        
        /// <summary>
        /// Calculate similarity between two titles using simple character overlap.
        /// Returns a value between 0 (no match) and 1 (exact match).
        /// </summary>
        private double CalculateTitleSimilarity(string title1, string title2)
        {
            if (string.IsNullOrWhiteSpace(title1) || string.IsNullOrWhiteSpace(title2))
                return 0;
                
            // Remove common words and punctuation for comparison
            var cleanTitle1 = CleanTitleForComparison(title1);
            var cleanTitle2 = CleanTitleForComparison(title2);
            
            if (cleanTitle1 == cleanTitle2)
                return 1.0;
                
            // Check if one title contains the other (for live albums, special editions, etc.)
            if (cleanTitle1.Contains(cleanTitle2) || cleanTitle2.Contains(cleanTitle1))
                return 0.8;
                
            // Calculate word overlap
            var words1 = cleanTitle1.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var words2 = cleanTitle2.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            if (words1.Length == 0 || words2.Length == 0)
                return 0;
                
            var commonWords = words1.Intersect(words2).Count();
            var totalWords = Math.Max(words1.Length, words2.Length);
            
            return (double)commonWords / totalWords;
        }
        
        /// <summary>
        /// Clean title for comparison by removing punctuation and common words.
        /// </summary>
        private string CleanTitleForComparison(string title)
        {
            // Remove punctuation and convert to lowercase
            var cleaned = Regex.Replace(title.ToLowerInvariant(), @"[^\w\s]", " ");
            
            // Remove common words that don't help with matching
            var stopWords = new[] { "the", "a", "an", "and", "or", "of", "in", "on", "at", "to", "for" };
            var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !stopWords.Contains(w))
                .ToArray();
                
            return string.Join(" ", words);
        }

        private string GenerateDownloadUrl(QobuzAlbum album, QobuzAudioQuality quality)
        {
            // Include quality in download URL so download client knows which quality to fetch
            return $"qobuz://album/{album.Id}/{(int)quality}";
        }

        private long CalculateSizeForQuality(QobuzAlbum album, QobuzAudioQuality quality)
        {
            // Use quality-specific bitrate for accurate size estimation
            var bitrate = quality.GetEstimatedBitrate();
            var durationSeconds = CalculateReliableDuration(album);
            
            // Convert bits per second to bytes per second, then multiply by duration
            var estimatedSize = (long)(durationSeconds * (bitrate / 8.0));
            
            // Ensure we don't return 0 size (causes issues in Lidarr)
            return Math.Max(estimatedSize, 1024 * 1024); // Minimum 1MB
        }

        private double CalculateReliableDuration(QobuzAlbum album)
        {
            // Tier 1: Use album duration if available
            if (album.Duration.TotalSeconds > 0)
                return album.Duration.TotalSeconds;

            // Tier 2: Sum track durations if available
            var tracks = album.GetTracks();
            if (tracks.Any())
            {
                var trackSum = tracks.Sum(t => t.Duration.TotalSeconds);
                if (trackSum > 0) return trackSum;
            }

            // Tier 3: Smart estimation based on singles vs albums
            var trackCount = Math.Max(album.TracksCount > 0 ? album.TracksCount : tracks.Count, 1);
            var isSingle = IsLikelySingle(album);
            var avgDuration = isSingle ? 3.25 * 60 : 3.5 * 60; // Singles: 3.25min, Albums: 3.5min
            
            return Math.Max(trackCount * avgDuration, 30); // 30 second minimum
        }

        private bool ShouldIncludeAlbum(QobuzAlbum album)
        {
            // Check if album is streamable
            if (!album.Streamable)
            {
                return false;
            }

            // Filter singles if not included
            if (!_settings.IncludeSingles && IsLikelySingle(album))
            {
                return false;
            }

            // Filter compilations if not included
            if (!_settings.IncludeCompilations && IsLikelyCompilation(album))
            {
                return false;
            }

            return true;
        }

        private bool IsLikelySingle(QobuzAlbum album)
        {
            // Consider it a single if it has few tracks and is short duration
            return album.TracksCount <= QobuzConstants.Parser.SingleTrackMinCount && 
                   album.Duration < QobuzConstants.Parser.SingleTrackMinDuration;
        }

        private bool IsLikelyCompilation(QobuzAlbum album)
        {
            var title = album.GetFullTitle().ToLower();
            var compilationKeywords = new[] { "compilation", "various artists", "best of", "greatest hits", "collection" };
            
            return compilationKeywords.Any(keyword => title.Contains(keyword)) ||
                   album.GetArtistName().Equals("Various Artists", StringComparison.OrdinalIgnoreCase);
        }


        private string GenerateInfoUrl(QobuzAlbum album)
        {
            return $"https://www.qobuz.com/album/{album.Slug ?? album.Id}";
        }


        private List<int> GetCategories(QobuzAlbum album)
        {
            var categories = new List<int>();

            // Map genres to category IDs (these would need to match Lidarr's categories)
            var genre = album.GetGenre().ToLower();

            // Basic genre categorization
            if (genre.Contains("jazz"))
                categories.Add(1001);
            else if (genre.Contains("classical"))
                categories.Add(1002);
            else if (genre.Contains("rock"))
                categories.Add(1003);
            else if (genre.Contains("electronic"))
                categories.Add(1004);
            else if (genre.Contains("pop"))
                categories.Add(1005);
            else
                categories.Add(1000); // General music

            // Add Hi-Res category if applicable
            if (album.HasHiResQuality())
                categories.Add(2000); // Hi-Res

            return categories;
        }

        private string ExtractQueryFromUrl(string queryString)
        {
            if (string.IsNullOrWhiteSpace(queryString))
                return string.Empty;

            var match = Regex.Match(queryString, @"[?&]query=([^&]+)");
            if (match.Success)
            {
                return WebUtility.UrlDecode(match.Groups[1].Value);
            }

            return string.Empty;
        }

        /// <summary>
        /// Calculate relevance score for sorting results
        /// </summary>
        public int CalculateRelevanceScore(ReleaseInfo release, string originalQuery)
        {
            if (string.IsNullOrWhiteSpace(originalQuery))
                return 0;

            var score = 0;
            var queryLower = originalQuery.ToLower();
            var titleLower = release.Title?.ToLower() ?? "";
            var artistLower = release.Artist?.ToLower() ?? "";
            var albumLower = release.Album?.ToLower() ?? "";

            // Exact matches
            if (titleLower.Contains(queryLower))
                score += 100;

            // Artist match
            if (artistLower.Contains(queryLower))
                score += 50;

            // Album match
            if (albumLower.Contains(queryLower))
                score += 75;

            // Quality bonus for Hi-Res
            if (release.Title?.Contains("FLAC") == true)
                score += 10;

            // Recent releases bonus
            if (release.PublishDate != default && release.PublishDate.Year >= DateTime.Now.Year - 2)
                score += 5;

            return score;
        }

        private void TrySetDownloadProtocol(ReleaseInfo release)
        {
            try
            {
                var prop = typeof(ReleaseInfo).GetProperty("DownloadProtocol");
                if (prop == null || !prop.CanWrite) return;

                if (prop.PropertyType == typeof(string))
                {
                    prop.SetValue(release, nameof(QobuzarrDownloadProtocol));
                }
                else if (prop.PropertyType.IsEnum)
                {
                    // Legacy enum-based protocol property
                    var unknown = Enum.Parse(prop.PropertyType, "Unknown");
                    prop.SetValue(release, unknown);
                }
            }
            catch
            {
                // no-op: be resilient to API differences across Lidarr versions
            }
        }
    }
}
