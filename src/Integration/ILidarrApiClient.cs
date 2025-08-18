using System.Collections.Generic;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;

namespace Lidarr.Plugin.Qobuzarr.Integration
{
    /// <summary>
    /// Defines the contract for interacting with the Lidarr REST API.
    /// Handles HTTP communication, authentication, and data retrieval from Lidarr instances.
    /// </summary>
    public interface ILidarrApiClient
    {
        /// <summary>
        /// Sets the Lidarr server configuration including URL and API key.
        /// Must be called before making any API requests.
        /// </summary>
        /// <param name="baseUrl">The base URL of the Lidarr instance (e.g., "http://localhost:8686").</param>
        /// <param name="apiKey">The API key for authentication with Lidarr.</param>
        void SetConfiguration(string baseUrl, string apiKey);

        /// <summary>
        /// Tests the connection to Lidarr and validates the API key.
        /// </summary>
        /// <returns>True if the connection is successful and API key is valid; false otherwise.</returns>
        /// <exception cref="LidarrApiException">Thrown when connection fails or API key is invalid.</exception>
        Task<bool> ValidateConnectionAsync();

        /// <summary>
        /// Retrieves system status information from Lidarr.
        /// Used for health checks and determining system availability.
        /// </summary>
        /// <returns>System status information including version, uptime, and configuration.</returns>
        /// <exception cref="LidarrApiException">Thrown when the API request fails.</exception>
        Task<LidarrSystemStatus> GetSystemStatusAsync();

        /// <summary>
        /// Retrieves health check information from Lidarr.
        /// </summary>
        /// <returns>List of health check results indicating system health status.</returns>
        /// <exception cref="LidarrApiException">Thrown when the API request fails.</exception>
        Task<List<LidarrHealthCheck>> GetHealthCheckAsync();

        /// <summary>
        /// Retrieves disk space information for all storage locations.
        /// </summary>
        /// <returns>List of disk space information for each storage path.</returns>
        /// <exception cref="LidarrApiException">Thrown when the API request fails.</exception>
        Task<List<LidarrDiskSpace>> GetDiskSpaceAsync();

        /// <summary>
        /// Retrieves a list of wanted albums based on the specified filter criteria.
        /// This is the core method for finding albums that need to be downloaded.
        /// </summary>
        /// <param name="filterOptions">Optional filter criteria to customize the query.</param>
        /// <returns>Paginated list of wanted albums matching the filter criteria.</returns>
        /// <exception cref="LidarrApiException">Thrown when the API request fails.</exception>
        Task<LidarrPagedResponse<LidarrAlbum>> GetWantedAlbumsAsync(LidarrFilterOptions filterOptions = null);

        /// <summary>
        /// Retrieves a specific album by its Lidarr ID.
        /// </summary>
        /// <param name="albumId">The Lidarr album ID.</param>
        /// <param name="includeStatistics">Whether to include statistics in the response.</param>
        /// <returns>The album information if found.</returns>
        /// <exception cref="LidarrApiException">Thrown when the album is not found or the API request fails.</exception>
        Task<LidarrAlbum> GetAlbumAsync(int albumId, bool includeStatistics = true);

        /// <summary>
        /// Retrieves a list of albums for a specific artist.
        /// </summary>
        /// <param name="artistId">The Lidarr artist ID.</param>
        /// <param name="includeStatistics">Whether to include statistics in the response.</param>
        /// <returns>List of albums for the specified artist.</returns>
        /// <exception cref="LidarrApiException">Thrown when the artist is not found or the API request fails.</exception>
        Task<List<LidarrAlbum>> GetAlbumsByArtistAsync(int artistId, bool includeStatistics = true);

        /// <summary>
        /// Retrieves a specific artist by its Lidarr ID.
        /// </summary>
        /// <param name="artistId">The Lidarr artist ID.</param>
        /// <param name="includeStatistics">Whether to include statistics in the response.</param>
        /// <returns>The artist information if found.</returns>
        /// <exception cref="LidarrApiException">Thrown when the artist is not found or the API request fails.</exception>
        Task<LidarrArtist> GetArtistAsync(int artistId, bool includeStatistics = true);

        /// <summary>
        /// Retrieves a list of all artists in Lidarr.
        /// </summary>
        /// <param name="includeStatistics">Whether to include statistics in the response.</param>
        /// <returns>List of all artists.</returns>
        /// <exception cref="LidarrApiException">Thrown when the API request fails.</exception>
        Task<List<LidarrArtist>> GetArtistsAsync(bool includeStatistics = false);

        /// <summary>
        /// Retrieves tracks for a specific album.
        /// </summary>
        /// <param name="albumId">The Lidarr album ID.</param>
        /// <returns>List of tracks in the specified album.</returns>
        /// <exception cref="LidarrApiException">Thrown when the album is not found or the API request fails.</exception>
        Task<List<LidarrTrack>> GetTracksAsync(int albumId);

        /// <summary>
        /// Searches for albums in Lidarr using the specified search term.
        /// </summary>
        /// <param name="searchTerm">The search term to use for finding albums.</param>
        /// <param name="limit">Maximum number of results to return (default: 50).</param>
        /// <returns>List of albums matching the search criteria.</returns>
        /// <exception cref="LidarrApiException">Thrown when the API request fails.</exception>
        Task<List<LidarrAlbum>> SearchAlbumsAsync(string searchTerm, int limit = 50);

        /// <summary>
        /// Searches for artists in Lidarr using the specified search term.
        /// </summary>
        /// <param name="searchTerm">The search term to use for finding artists.</param>
        /// <param name="limit">Maximum number of results to return (default: 50).</param>
        /// <returns>List of artists matching the search criteria.</returns>
        /// <exception cref="LidarrApiException">Thrown when the API request fails.</exception>
        Task<List<LidarrArtist>> SearchArtistsAsync(string searchTerm, int limit = 50);

        /// <summary>
        /// Retrieves overall system statistics from Lidarr.
        /// </summary>
        /// <returns>System statistics including counts and sizes.</returns>
        /// <exception cref="LidarrApiException">Thrown when the API request fails.</exception>
        Task<LidarrSystemStatistics> GetSystemStatisticsAsync();

        /// <summary>
        /// Checks if the Lidarr instance is currently available and responding.
        /// This is a lightweight health check that doesn't require authentication.
        /// </summary>
        /// <returns>True if Lidarr is available; false otherwise.</returns>
        Task<bool> IsAvailableAsync();

        /// <summary>
        /// Retrieves all quality profiles configured in Lidarr.
        /// Quality profiles define the preferred audio formats and qualities for downloads.
        /// </summary>
        /// <returns>List of all quality profiles with their quality definitions.</returns>
        /// <exception cref="LidarrApiException">Thrown when the API request fails.</exception>
        Task<List<LidarrQualityProfile>> GetQualityProfilesAsync();

        /// <summary>
        /// Retrieves a specific quality profile by its ID.
        /// </summary>
        /// <param name="profileId">The quality profile ID.</param>
        /// <returns>The quality profile information if found.</returns>
        /// <exception cref="LidarrApiException">Thrown when the profile is not found or the API request fails.</exception>
        Task<LidarrQualityProfile> GetQualityProfileAsync(int profileId);

        /// <summary>
        /// Retrieves all quality definitions available in Lidarr.
        /// These are the building blocks used to create quality profiles.
        /// </summary>
        /// <returns>List of all quality definitions.</returns>
        /// <exception cref="LidarrApiException">Thrown when the API request fails.</exception>
        Task<List<LidarrQuality>> GetQualityDefinitionsAsync();
    }
}