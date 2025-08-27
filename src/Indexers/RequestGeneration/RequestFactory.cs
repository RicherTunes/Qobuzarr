using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Indexers;
using NzbDrone.Common.Http;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Lidarr.Plugin.Qobuzarr.Indexers.RequestGeneration
{
    /// <summary>
    /// Creates HTTP requests for Qobuz API endpoints.
    /// Extracted from QobuzRequestGenerator to follow Single Responsibility Principle.
    /// </summary>
    public class RequestFactory : IRequestFactory
    {
        private readonly QobuzIndexerSettings _settings;
        private readonly Logger _logger;
        
        private const string SEARCH_ENDPOINT = "/album/search";
        private const string BASE_URL = "https://www.qobuz.com/api.json/0.2";
        private const int PAGE_SIZE = 100;
        private const int MAX_PAGES = 5;

        public RequestFactory(QobuzIndexerSettings settings, Logger logger)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IndexerRequest CreateSearchRequest(string query, SearchCriteriaBase searchCriteria, QobuzSession session)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    throw new ArgumentException("Query cannot be null or empty", nameof(query));
                }

                var requestBuilder = new HttpRequestBuilder(BASE_URL + SEARCH_ENDPOINT)
                    .SetSegment("query", query)
                    .AddQueryParam("query", query)
                    .AddQueryParam("limit", PAGE_SIZE)
                    .AddQueryParam("offset", 0);

                // Add authentication if session is available
                if (session != null && !string.IsNullOrWhiteSpace(session.AuthToken))
                {
                    requestBuilder.AddQueryParam("user_auth_token", session.AuthToken);
                    
                    if (!string.IsNullOrWhiteSpace(session.AppId))
                    {
                        requestBuilder.AddQueryParam("app_id", session.AppId);
                    }
                }

                // Add quality filter (default to Hi-Res quality)
                var preferredQuality = 7; // Hi-Res quality by default
                requestBuilder.AddQueryParam("format_id", preferredQuality.ToString());

                var httpRequest = requestBuilder.Build();
                var indexerRequest = new IndexerRequest(httpRequest);

                _logger.Debug("Created search request for query: {0}", query);
                return indexerRequest;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error creating search request for query: {0}", query);
                throw;
            }
        }

        public IEnumerable<IndexerRequest> GetPagedRequests(IndexerRequest request)
        {
            try
            {
                var requests = new List<IndexerRequest> { request };

                // Generate additional pages
                for (int page = 1; page < MAX_PAGES; page++)
                {
                    var offset = page * PAGE_SIZE;
                    var pagedRequest = CloneRequestWithOffset(request, offset);
                    requests.Add(pagedRequest);
                }

                _logger.Debug("Generated {0} paged requests", requests.Count);
                return requests;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error generating paged requests");
                return new[] { request }; // Return original request as fallback
            }
        }

        public IndexerRequest CloneRequestWithOffset(IndexerRequest originalRequest, int offset)
        {
            try
            {
                var originalUri = originalRequest.HttpRequest.Url.ToString();
                var uriBuilder = new UriBuilder(originalUri);
                
                // Parse existing query parameters
                var queryParams = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);
                
                // Update offset parameter
                queryParams["offset"] = offset.ToString();
                
                // Rebuild query string
                uriBuilder.Query = queryParams.ToString();

                // Create new request with updated URI
                var newHttpRequest = new HttpRequest(uriBuilder.Uri.ToString())
                {
                    Method = originalRequest.HttpRequest.Method,
                    Headers = originalRequest.HttpRequest.Headers
                };

                // Copy other properties from original request
                foreach (var header in originalRequest.HttpRequest.Headers)
                {
                    if (!newHttpRequest.Headers.ContainsKey(header.Key))
                    {
                        newHttpRequest.Headers[header.Key] = header.Value;
                    }
                }

                return new IndexerRequest(newHttpRequest);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error cloning request with offset: {0}", offset);
                return originalRequest; // Return original as fallback
            }
        }

        public IndexerRequest CreateMockSearchRequest(AlbumSearchCriteria searchCriteria)
        {
            try
            {
                // Create a mock request for cached results
                var mockUrl = $"{BASE_URL}{SEARCH_ENDPOINT}?query=cached&mock=true";
                var mockHttpRequest = new HttpRequest(mockUrl);
                var mockRequest = new IndexerRequest(mockHttpRequest);

                _logger.Debug("Created mock search request for cached results");
                return mockRequest;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error creating mock search request");
                throw;
            }
        }
    }
}