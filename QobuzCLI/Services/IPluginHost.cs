using QobuzCLI.Models;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Services;

namespace QobuzCLI.Services;

/// <summary>
/// Defines the interface for the CLI's plugin host service that bridges CLI commands 
/// with the core Qobuzzarr plugin functionality. This abstraction allows the CLI to use
/// plugin services while maintaining separation of concerns.
/// </summary>
/// <remarks>
/// The plugin host serves as the primary integration point between the CLI application
/// and the Qobuzzarr plugin. It manages plugin lifecycle, exposes plugin services to CLI commands,
/// and handles the configuration mapping between CLI config format and plugin requirements.
/// 
/// Key responsibilities:
/// - Initialize plugin services with CLI configuration
/// - Provide simplified API methods for common CLI operations
/// - Abstract away plugin complexity from CLI commands
/// - Handle authentication state management
/// - Manage download operations and progress tracking
/// </remarks>
public interface IPluginHost
{
    /// <summary>
    /// Initializes the plugin host with the provided configuration.
    /// Sets up all plugin services and prepares them for use by CLI commands.
    /// </summary>
    /// <param name="config">The CLI configuration containing Qobuz credentials and settings.</param>
    /// <returns>A task representing the asynchronous initialization operation.</returns>
    Task InitializeAsync(QobuzConfig config);

    /// <summary>
    /// Gets a value indicating whether the plugin host has been successfully initialized.
    /// </summary>
    bool IsInitialized { get; }

    // Plugin services - direct access to plugin functionality
    IQobuzAuthenticationService Auth { get; }
    // IQobuzApiClient Api { get; }
    // QobuzIndexer Indexer { get; }  
    // QobuzDownloadClient DownloadClient { get; }

    /// <summary>
    /// Tests the current authentication configuration by attempting to authenticate with Qobuz.
    /// Validates credentials and verifies API connectivity.
    /// </summary>
    /// <returns>True if authentication succeeds; false otherwise.</returns>
    Task<bool> TestAuthenticationAsync();

    /// <summary>
    /// Performs a search in the Qobuz catalog using the specified query and type.
    /// </summary>
    /// <param name="query">The search query string (artist name, album title, etc.).</param>
    /// <param name="type">The type of search to perform (Album, Artist, Track).</param>
    /// <returns>A list of search results matching the query.</returns>
    Task<List<SearchResult>> SearchAsync(string query, QobuzCLI.Models.SearchType type);

    /// <summary>
    /// Downloads a complete album from Qobuz using the default quality settings.
    /// </summary>
    /// <param name="albumId">The Qobuz album ID to download.</param>
    /// <param name="outputPath">The directory where album files should be saved.</param>
    /// <returns>The download result containing success status and file information.</returns>
    Task<Lidarr.Plugin.Qobuzarr.Services.DownloadResult> DownloadAlbumAsync(string albumId, string outputPath);

    /// <summary>
    /// Downloads a complete album from Qobuz with the specified quality preference.
    /// </summary>
    /// <param name="albumId">The Qobuz album ID to download.</param>
    /// <param name="outputPath">The directory where album files should be saved.</param>
    /// <param name="quality">Optional quality preference (MP3, FLAC, Hi-Res). If null, uses default from config.</param>
    /// <returns>The download result containing success status and file information.</returns>
    Task<CliDownloadResult> DownloadAlbumAsync(string albumId, string outputPath, string? quality = null);

    /// <summary>
    /// Downloads all albums from a specific artist's discography.
    /// </summary>
    /// <param name="artistId">The Qobuz artist ID whose discography should be downloaded.</param>
    /// <param name="outputPath">The directory where artist albums should be saved.</param>
    /// <returns>The download result containing success status and file information.</returns>
    Task<CliDownloadResult> DownloadArtistAsync(string artistId, string outputPath);

    /// <summary>
    /// Downloads a complete playlist from Qobuz including all tracks.
    /// Creates M3U8 playlist file and preserves track order.
    /// </summary>
    /// <param name="playlistId">The Qobuz playlist ID to download.</param>
    /// <param name="outputPath">The directory where playlist files should be saved.</param>
    /// <param name="quality">Optional quality preference (MP3, FLAC, Hi-Res). If null, uses default from config.</param>
    /// <param name="createM3u8">Whether to create an M3U8 playlist file. Default is true.</param>
    /// <returns>The playlist download result containing success status and track information.</returns>
    Task<CliPlaylistDownloadResult> DownloadPlaylistAsync(
        string playlistId,
        string outputPath,
        string? quality = null,
        bool createM3u8 = true);

    /// <summary>
    /// Downloads all albums from a specific record label.
    /// Organizes albums by artist within the label folder.
    /// </summary>
    /// <param name="labelId">The Qobuz label ID whose albums should be downloaded.</param>
    /// <param name="outputPath">The directory where label albums should be saved.</param>
    /// <param name="quality">Optional quality preference (MP3, FLAC, Hi-Res). If null, uses default from config.</param>
    /// <param name="maxAlbums">Maximum number of albums to download. Default is 100.</param>
    /// <returns>The label download result containing success status and album information.</returns>
    Task<Lidarr.Plugin.Qobuzarr.Download.Services.LabelDownloadResult> DownloadLabelAsync(
        string labelId,
        string outputPath,
        string? quality = null,
        int maxAlbums = 100);

    /// <summary>
    /// Logs performance metrics about plugin operations for monitoring and debugging.
    /// Includes API call statistics, download speeds, and error rates.
    /// </summary>
    void LogPerformanceMetrics();

    /// <summary>
    /// Validates that an album is actually downloadable before queuing.
    /// Checks if the album exists and has tracks available for download.
    /// </summary>
    /// <param name="albumId">The Qobuz album ID to validate.</param>
    /// <param name="preferredQuality">The preferred quality ID to check availability for.</param>
    /// <returns>True if the album has downloadable content; false otherwise.</returns>
    Task<bool> ValidateAlbumDownloadabilityAsync(string albumId, int preferredQuality = 27);

    /// <summary>
    /// Checks if an album already exists locally with adequate quality.
    /// </summary>
    /// <param name="albumId">The album ID to check.</param>
    /// <param name="albumDir">The directory where the album would be stored.</param>
    /// <param name="requestedQuality">The requested quality level (e.g., "flac-max", "mp3-320").</param>
    /// <returns>Result indicating if the album exists with adequate quality.</returns>
    Task<(bool AlreadyExists, int ExistingTrackCount, string Reason)> CheckExistingAlbumAsync(string albumId, string albumDir, string requestedQuality);

    /// <summary>
    /// Gets access to Lidarr integration functionality from the plugin.
    /// This provides CLI access to the plugin's LidarrIntegrationService without reimplementation.
    /// </summary>
    /// <returns>The plugin's Lidarr integration service if available; null if not configured.</returns>
    Lidarr.Plugin.Qobuzarr.Services.ILidarrIntegrationService? GetLidarrIntegrationService();
}
