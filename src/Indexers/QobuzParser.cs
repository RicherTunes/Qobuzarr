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
using NLog;
using NzbDrone.Core.Qualities;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Configuration;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    public class QobuzParser : IParseIndexerResponse
    {
        private readonly QobuzIndexerSettings _settings;
        private readonly Logger _logger;

        public QobuzParser(QobuzIndexerSettings settings, Logger logger)
        {
            _settings = settings;
            _logger = logger;
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
                
                // CRITICAL: Set the download protocol to fix frontend display
                DownloadProtocol = nameof(UsenetDownloadProtocol),
                
                // Basic metadata - ENSURE NON-EMPTY NAMES
                Artist = artistName,
                Album = albumTitle,
                DownloadUrl = GenerateDownloadUrl(album, quality),
                InfoUrl = GenerateInfoUrl(album),
                PublishDate = album.ReleaseDate,
                Indexer = "Qobuzarr", // Must match QobuzIndexer.Name
                
                // Note: Codec and Container properties are ignored by Lidarr's quality detection
                // Quality is determined solely from the Title using regex patterns
                
                // CRITICAL: Quality-specific size calculation
                Size = CalculateSizeForQuality(album, quality)
            };

            // Generate quality-specific title
            release.Title = GenerateQualitySpecificTitle(album, quality, year);
            
            // Debug logging to help diagnose quality detection issues
            _logger.Debug("Generated release title for quality {0}: '{1}'", quality, release.Title);

            return release;
        }

        private string GenerateQualitySpecificTitle(QobuzAlbum album, QobuzAudioQuality quality, int year)
        {
            var artist = album.GetArtistName();
            var albumTitle = album.GetFullTitle();
            var yearStr = year > 0 ? $" ({year})" : "";
            
            // Generate quality-specific format strings that match Lidarr's regex patterns EXACTLY
            var formatStr = quality switch
            {
                // BitRateRegex looks for: (?<B320>320[ ]?kbps|320|[\[\(].*320.*[\]\)]|q9)
                QobuzAudioQuality.MP3320 => "MP3 320kbps",
                
                // CodecRegex looks for: (?<FLAC>(web)?flac(?:24(?:[-._ ]?bit)?)?|TR24) + SampleSizeRegex for 24bit
                QobuzAudioQuality.FLACLossless => "FLAC",
                
                // SampleSizeRegex looks for: (?<S24>24[-._ ]?bit|flac24(?:[-._ ]?bit)?|tr24|24-(?:44|48|96|192))
                QobuzAudioQuality.FLACHiRes24Bit96kHz => "FLAC 24bit 96kHz",
                QobuzAudioQuality.FLACHiRes24Bit192Khz => "FLAC 24bit 192kHz",
                _ => "Unknown"
            };
            
            // Add explicit warning if applicable
            var explicitStr = album.ParentalWarning ? " [Explicit]" : "";
            
            // Format: Artist - Album (Year) [Explicit] [Quality] [WEB]
            // The quality info needs to be in the title text, not just in brackets
            return $"{artist} - {albumTitle}{yearStr}{explicitStr} [{formatStr}] [WEB]";
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
            var durationSeconds = album.Duration.TotalSeconds; // Convert TimeSpan to seconds
            
            // Convert bits per second to bytes per second, then multiply by duration
            return (long)(durationSeconds * (bitrate / 8.0));
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