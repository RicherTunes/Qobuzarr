using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Lidarr.Plugin.Qobuzarr.Models.Lidarr
{
    /// <summary>
    /// Represents filtering and sorting options for Lidarr API requests.
    /// Used to customize queries for wanted albums, artists, and tracks.
    /// </summary>
    public class LidarrFilterOptions
    {
        /// <summary>
        /// Page number for pagination (1-based)
        /// </summary>
        public int Page { get; set; } = 1;

        /// <summary>
        /// Number of items per page
        /// </summary>
        public int PageSize { get; set; } = 20;

        /// <summary>
        /// Sort field name
        /// </summary>
        public string SortKey { get; set; } = "releaseDate";

        /// <summary>
        /// Sort direction (asc, desc)
        /// </summary>
        public string SortDirection { get; set; } = "desc";

        /// <summary>
        /// Include statistics in the response
        /// </summary>
        public bool IncludeStatistics { get; set; } = true;

        /// <summary>
        /// Include artist information in album responses
        /// </summary>
        public bool IncludeArtist { get; set; } = true;

        /// <summary>
        /// Filter by monitored status
        /// </summary>
        public bool? Monitored { get; set; }

        /// <summary>
        /// Filter by grabbed status
        /// </summary>
        public bool? Grabbed { get; set; }

        /// <summary>
        /// Filter by availability (has files)
        /// </summary>
        public bool? Available { get; set; }

        /// <summary>
        /// Filter by specific artist ID
        /// </summary>
        public int? ArtistId { get; set; }

        /// <summary>
        /// Filter by quality profile IDs
        /// </summary>
        public List<int> QualityProfileIds { get; set; } = new List<int>();

        /// <summary>
        /// Filter by metadata profile IDs
        /// </summary>
        public List<int> MetadataProfileIds { get; set; } = new List<int>();

        /// <summary>
        /// Filter by release date range (from)
        /// </summary>
        public DateTime? ReleaseDateFrom { get; set; }

        /// <summary>
        /// Filter by release date range (to)
        /// </summary>
        public DateTime? ReleaseDateTo { get; set; }

        /// <summary>
        /// Filter by album types
        /// </summary>
        public List<string> AlbumTypes { get; set; } = new List<string>();

        /// <summary>
        /// Filter by genres
        /// </summary>
        public List<string> Genres { get; set; } = new List<string>();

        /// <summary>
        /// Filter by tags
        /// </summary>
        public List<int> Tags { get; set; } = new List<int>();

        /// <summary>
        /// Search term for text filtering
        /// </summary>
        public string SearchTerm { get; set; }

        /// <summary>
        /// Convert filter options to query parameters for Lidarr API
        /// </summary>
        public Dictionary<string, string> ToQueryParameters()
        {
            var parameters = new Dictionary<string, string>
            {
                ["page"] = Page.ToString(),
                ["pageSize"] = PageSize.ToString(),
                ["sortKey"] = SortKey,
                ["sortDirection"] = SortDirection,
                ["includeStatistics"] = IncludeStatistics.ToString().ToLower(),
                ["includeArtist"] = IncludeArtist.ToString().ToLower()
            };

            if (Monitored.HasValue)
                parameters["monitored"] = Monitored.Value.ToString().ToLower();

            if (Grabbed.HasValue)
                parameters["grabbed"] = Grabbed.Value.ToString().ToLower();

            if (Available.HasValue)
                parameters["available"] = Available.Value.ToString().ToLower();

            if (ArtistId.HasValue)
                parameters["artistId"] = ArtistId.Value.ToString();

            if (QualityProfileIds.Count > 0)
                parameters["qualityProfileIds"] = string.Join(",", QualityProfileIds);

            if (MetadataProfileIds.Count > 0)
                parameters["metadataProfileIds"] = string.Join(",", MetadataProfileIds);

            if (ReleaseDateFrom.HasValue)
                parameters["releaseDateFrom"] = ReleaseDateFrom.Value.ToString("yyyy-MM-dd");

            if (ReleaseDateTo.HasValue)
                parameters["releaseDateTo"] = ReleaseDateTo.Value.ToString("yyyy-MM-dd");

            if (AlbumTypes.Count > 0)
                parameters["albumTypes"] = string.Join(",", AlbumTypes);

            if (Genres.Count > 0)
                parameters["genres"] = string.Join(",", Genres);

            if (Tags.Count > 0)
                parameters["tags"] = string.Join(",", Tags);

            if (!string.IsNullOrWhiteSpace(SearchTerm))
                parameters["term"] = SearchTerm;

            return parameters;
        }

        /// <summary>
        /// Create filter options for wanted albums (monitored, not grabbed, not available)
        /// </summary>
        public static LidarrFilterOptions ForWantedAlbums()
        {
            return new LidarrFilterOptions
            {
                Monitored = true,
                Grabbed = false,
                Available = false,
                SortKey = "releaseDate",
                SortDirection = "desc",
                PageSize = 50
            };
        }

        /// <summary>
        /// Create filter options for recent releases (last 30 days)
        /// </summary>
        public static LidarrFilterOptions ForRecentReleases()
        {
            return new LidarrFilterOptions
            {
                ReleaseDateFrom = DateTime.UtcNow.AddDays(-30),
                SortKey = "releaseDate",
                SortDirection = "desc",
                PageSize = 50
            };
        }

        /// <summary>
        /// Create filter options for missing albums by specific artist
        /// </summary>
        public static LidarrFilterOptions ForMissingByArtist(int artistId)
        {
            return new LidarrFilterOptions
            {
                ArtistId = artistId,
                Monitored = true,
                Available = false,
                SortKey = "releaseDate",
                SortDirection = "desc"
            };
        }

        /// <summary>
        /// Create filter options for albums by quality profile
        /// </summary>
        public static LidarrFilterOptions ForQualityProfile(int qualityProfileId)
        {
            return new LidarrFilterOptions
            {
                QualityProfileIds = new List<int> { qualityProfileId },
                SortKey = "title",
                SortDirection = "asc"
            };
        }

        /// <summary>
        /// Create filter options for search with term
        /// </summary>
        public static LidarrFilterOptions ForSearch(string searchTerm)
        {
            return new LidarrFilterOptions
            {
                SearchTerm = searchTerm,
                SortKey = "releaseDate",
                SortDirection = "desc",
                PageSize = 100
            };
        }
    }

    /// <summary>
    /// Represents the response wrapper for paginated Lidarr API results
    /// </summary>
    /// <typeparam name="T">The type of items in the result set</typeparam>
    public class LidarrPagedResponse<T>
    {
        [JsonProperty("page")]
        public int Page { get; set; }

        [JsonProperty("pageSize")]
        public int PageSize { get; set; }

        [JsonProperty("sortKey")]
        public string SortKey { get; set; }

        [JsonProperty("sortDirection")]
        public string SortDirection { get; set; }

        [JsonProperty("totalRecords")]
        public int TotalRecords { get; set; }

        [JsonProperty("records")]
        public List<T> Records { get; set; } = new List<T>();

        /// <summary>
        /// Get total number of pages
        /// </summary>
        public int GetTotalPages()
        {
            if (PageSize == 0)
                return 0;

            return (int)Math.Ceiling((double)TotalRecords / PageSize);
        }

        /// <summary>
        /// Check if there are more pages available
        /// </summary>
        public bool HasNextPage()
        {
            return Page < GetTotalPages();
        }

        /// <summary>
        /// Check if there are previous pages
        /// </summary>
        public bool HasPreviousPage()
        {
            return Page > 1;
        }

        /// <summary>
        /// Get next page number (or null if no next page)
        /// </summary>
        public int? GetNextPageNumber()
        {
            return HasNextPage() ? Page + 1 : null;
        }

        /// <summary>
        /// Get previous page number (or null if no previous page)
        /// </summary>
        public int? GetPreviousPageNumber()
        {
            return HasPreviousPage() ? Page - 1 : null;
        }
    }
}
