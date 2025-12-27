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
using Lidarr.Plugin.Qobuzarr.Indexers.Parsing;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    public class QobuzParser : IParseIndexerResponse
    {
        private readonly QobuzIndexerSettings _settings;
        private readonly Logger _logger;
        private readonly ITitleGenerator _titleGenerator;
        private SearchCriteriaBase _currentSearchCriteria;

        public QobuzParser(QobuzIndexerSettings settings, Logger logger)
        {
            _settings = settings;
            _logger = logger;
            _titleGenerator = new TitleGenerator(logger);
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
            // Validate required album data - check raw Title before GetFullTitle() which returns "Unknown Album" for empty titles
            if (string.IsNullOrWhiteSpace(album?.Title))
            {
                _logger.Warn("Album has no title, skipping release creation");
                return null;
            }

            var year = album.ReleaseDate.Year > 1900 ? album.ReleaseDate.Year : 0;
            
            var artistName = album.GetArtistName();
            var albumTitle = album.Title;
            var albumFullTitle = album.GetFullTitle();
            
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
                
                // CRITICAL: Set the download protocol to fix frontend display
                DownloadProtocol = nameof(QobuzarrDownloadProtocol),
                
                // Basic metadata - ENSURE NON-EMPTY NAMES
                Artist = artistName,
                Album = albumTitle,
                DownloadUrl = GenerateDownloadUrl(album, quality),
                InfoUrl = GenerateInfoUrl(album),
                PublishDate = album.ReleaseDate,
                // Note: Indexer is set here for unit tests that use QobuzParser directly.
                // In production, QobuzIndexer.FetchReleases() overwrites this with Definition.Name
                // after parsing. This ensures tests can assert on Indexer without needing
                // a full IndexerDefinition. See QobuzParserTests.ParseResponse_WithValidAlbum_ShouldPopulateAllRequiredFields
                Indexer = "Qobuzarr",
                
                // Note: Codec and Container properties are ignored by Lidarr's quality detection
                // Quality is determined solely from the Title using regex patterns
                
                // CRITICAL: Quality-specific size calculation
                Size = CalculateSizeForQuality(album, quality)
            };

            // Generate quality-specific title
            release.Title = _titleGenerator.GenerateQualitySpecificTitle(album, quality, year);

            // Prefer Lidarr's exact requested album title when available to improve Decision Engine matching.
            if (_currentSearchCriteria?.Albums?.Any() == true)
            {
                var bestMatch = FindBestMatchingAlbum(album, _currentSearchCriteria.Albums, year);
                if (!string.IsNullOrWhiteSpace(bestMatch?.Title))
                {
                    release.Album = bestMatch.Title;
                }
            }
            
            // Critical debugging for album mapping (only during troubleshooting)
            _logger.Debug("ALBUM MAPPING: Qobuz '{0}' ({1}) -> Title '{2}' -> Album '{3}'", 
                album.Id, album.GetFullTitle(), release.Title, release.Album);
            
            // Debug context-aware matching for edition albums
            if (_currentSearchCriteria?.Albums?.Any() == true)
            {
                _logger.Debug("Search Context Available: {0} albums in criteria", _currentSearchCriteria.Albums.Count);
                foreach (var lidarrAlbum in _currentSearchCriteria.Albums)
                {
                    _logger.Debug("   Lidarr Album: '{0}' ({1})", lidarrAlbum.Title, lidarrAlbum.ReleaseDate?.Year);
                }
            }
            else
            {
                _logger.Debug("No search context available - using original Qobuz title");
            }

            return release;
        }

        /// <summary>
        /// Checks if album title indicates a live recording
        /// </summary>
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
            // Delegate to QualitySizeCalculator for testability
            return QualitySizeCalculator.CalculateSize(album, quality);
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
    }
}






