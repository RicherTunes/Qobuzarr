using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NLog;
using Newtonsoft.Json;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Indexers.Parsing;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Refactored Qobuz API response parser with decomposed responsibilities.
    /// Reduced from 821 lines to ~120 lines by extracting specialized services:
    /// - ResponseParser: Handles different response types
    /// - ReleaseInfoFactory: Creates ReleaseInfo objects
    /// - TitleGenerator: Generates formatted titles
    /// - AlbumMatcher: Matches and filters albums
    /// 
    /// Maintains all original functionality including intelligent title generation and quality handling.
    /// </summary>
    public class QobuzParser : IParseIndexerResponse
    {
        private readonly QobuzIndexerSettings _settings;
        private readonly Logger _logger;
        
        // Decomposed service dependencies
        private readonly IResponseParser _responseParser;
        private readonly IReleaseInfoFactory _releaseInfoFactory;
        private readonly ITitleGenerator _titleGenerator;
        private readonly IAlbumMatcher _albumMatcher;
        
        // Context from request generator
        private SearchCriteriaBase _searchCriteria;

        public QobuzParser(QobuzIndexerSettings settings, Logger logger)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Initialize decomposed services
            _albumMatcher = new AlbumMatcher(logger);
            _titleGenerator = new TitleGenerator(logger);
            _releaseInfoFactory = new ReleaseInfoFactory(settings, logger);
            _responseParser = new ResponseParser(settings, logger, _albumMatcher, _releaseInfoFactory);
        }

        public void SetSearchContext(SearchCriteriaBase searchCriteria)
        {
            _searchCriteria = searchCriteria;
            _logger.Debug("Updated search context for parser");
        }

        public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
        {
            var releases = new List<ReleaseInfo>();

            try
            {
                if (indexerResponse?.Content == null)
                {
                    _logger.Warn("Received null or empty response");
                    return releases;
                }

                _logger.Debug("Parsing Qobuz response ({0} characters)", indexerResponse.Content.Length);

                // Try to parse as album search response first
                var albumSearchResponse = TryParseAlbumSearchResponse(indexerResponse.Content);
                if (albumSearchResponse != null)
                {
                    var albumReleases = _responseParser.ParseAlbumSearchResponse(albumSearchResponse, GetOriginalQuery(indexerResponse));
                    releases.AddRange(albumReleases);
                    _logger.Info("🎵 Parsed {0} releases from album search response", albumReleases.Count());
                    return releases;
                }

                // Try to parse as general search response
                var generalSearchResponse = TryParseGeneralSearchResponse(indexerResponse.Content);
                if (generalSearchResponse != null)
                {
                    var generalReleases = _responseParser.ParseGeneralSearchResponse(generalSearchResponse, GetOriginalQuery(indexerResponse));
                    releases.AddRange(generalReleases);
                    _logger.Info("🎵 Parsed {0} releases from general search response", generalReleases.Count());
                    return releases;
                }

                _logger.Warn("Unable to parse response as known Qobuz format");
                return releases;
            }
            catch (JsonException jsonEx)
            {
                _logger.Error(jsonEx, "JSON parsing error in Qobuz response");
                return releases;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error parsing Qobuz response");
                return releases;
            }
        }

        // Backward-compatibility for tests: expose legacy private method name via reflection
        private ReleaseInfo CreateReleaseInfoForQuality(QobuzAlbum album, QobuzAudioQuality quality, string originalQuery)
        {
            return _releaseInfoFactory.CreateReleaseInfoForQuality(album, quality, originalQuery);
        }

        private QobuzAlbumSearchResponse TryParseAlbumSearchResponse(string content)
        {
            try
            {
                return JsonConvert.DeserializeObject<QobuzAlbumSearchResponse>(content);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Content is not an album search response");
                return null;
            }
        }

        private QobuzSearchResponse TryParseGeneralSearchResponse(string content)
        {
            try
            {
                return JsonConvert.DeserializeObject<QobuzSearchResponse>(content);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Content is not a general search response");
                return null;
            }
        }

        private string GetOriginalQuery(IndexerResponse indexerResponse)
        {
            try
            {
                var uri = new Uri(indexerResponse.Request.Url.ToString());
                var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
                return queryParams["query"] ?? "";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error extracting original query from request");
                return "";
            }
        }

        // Expose decomposed services for testing/extension
        public IResponseParser ResponseParser => _responseParser;
        public IReleaseInfoFactory ReleaseInfoFactory => _releaseInfoFactory;
        public ITitleGenerator TitleGenerator => _titleGenerator;
        public IAlbumMatcher AlbumMatcher => _albumMatcher;
    }

    /// <summary>
    /// Implementation of response parsing logic.
    /// Extracted from QobuzParser god class.
    /// </summary>
    internal class ResponseParser : IResponseParser
    {
        private readonly QobuzIndexerSettings _settings;
        private readonly Logger _logger;
        private readonly IAlbumMatcher _albumMatcher;
        private readonly IReleaseInfoFactory _releaseInfoFactory;

        public ResponseParser(QobuzIndexerSettings settings, Logger logger, IAlbumMatcher albumMatcher, IReleaseInfoFactory releaseInfoFactory)
        {
            _settings = settings;
            _logger = logger;
            _albumMatcher = albumMatcher;
            _releaseInfoFactory = releaseInfoFactory;
        }

        public IEnumerable<ReleaseInfo> ParseAlbumSearchResponse(QobuzAlbumSearchResponse response, string originalQuery)
        {
            var releases = new List<ReleaseInfo>();
            
            if (response?.Albums?.Items == null)
            {
                _logger.Debug("No albums found in search response");
                return releases;
            }

            _logger.Debug("Processing {0} albums from search response", response.Albums.Items.Count);

            foreach (var album in response.Albums.Items.Take(50)) // Limit for performance
            {
                try
                {
                    var albumReleases = ConvertAlbumToReleases(album, originalQuery);
                    releases.AddRange(albumReleases);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error processing album: {0} - {1}", album.Artist?.Name, album.Title);
                }
            }

            return releases;
        }

        public IEnumerable<ReleaseInfo> ParseGeneralSearchResponse(QobuzSearchResponse response, string originalQuery)
        {
            var releases = new List<ReleaseInfo>();
            
            if (response?.Albums?.Items == null)
            {
                _logger.Debug("No albums found in general search response");
                return releases;
            }

            foreach (var album in response.Albums.Items.Take(50))
            {
                try
                {
                    var albumReleases = ConvertAlbumToReleases(album, originalQuery);
                    releases.AddRange(albumReleases);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error processing general search album: {0} - {1}", album.Artist?.Name, album.Title);
                }
            }

            return releases;
        }

        public IEnumerable<ReleaseInfo> ConvertAlbumToReleases(QobuzAlbum album, string originalQuery)
        {
            var releases = new List<ReleaseInfo>();
            
            if (album == null || string.IsNullOrWhiteSpace(album.Title))
            {
                return releases;
            }

            // Filter albums based on settings
            if (!_albumMatcher.ShouldIncludeAlbum(album, _settings))
            {
                return releases;
            }

            // Create release for each available quality
            var availableQualities = GetAvailableQualities(album);

            foreach (var quality in availableQualities)
            {
                try
                {
                    var release = _releaseInfoFactory.CreateReleaseInfoForQuality(album, quality, originalQuery);
                    if (release != null)
                    {
                        releases.Add(release);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error creating release for quality {0}", (int)quality);
                }
            }

            return releases;
        }

        private List<QobuzAudioQuality> GetAvailableQualities(QobuzAlbum album)
        {
            var qualities = new List<QobuzAudioQuality>();

            // Add qualities based on album's maximum quality and streamable flags
            if (album.MaximumBitDepth.HasValue && album.MaximumSampleRate.HasValue)
            {
                // Hi-Res quality available
                if (album.Streamable && album.Downloadable)
                {
                    qualities.Add(QobuzAudioQuality.FLACHiRes24Bit192Khz);
                }
            }

            // Always include CD quality if available
            if (album.Streamable)
            {
                qualities.Add(QobuzAudioQuality.FLACLossless);
            }

            // Add MP3 320 as fallback
            qualities.Add(QobuzAudioQuality.MP3320);

            return qualities.Distinct().ToList();
        }
    }
}
