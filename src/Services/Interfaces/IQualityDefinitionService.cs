using System.Collections.Generic;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services.Consolidated;

namespace Lidarr.Plugin.Qobuzarr.Services.Interfaces
{
    /// <summary>
    /// Interface for managing Qobuz audio quality definitions.
    /// </summary>
    /// <remarks>
    /// This interface provides access to quality definitions, format mappings,
    /// and quality-related metadata for Qobuz streaming.
    /// 
    /// Key Features:
    /// - Complete quality format definitions
    /// - Bitrate and format mapping
    /// - Quality hierarchy and ordering
    /// - Format validation and normalization
    /// - Lidarr quality profile integration
    /// 
    /// This is a pure domain service focused on quality definitions
    /// without external API dependencies.
    /// </remarks>
    public interface IQualityDefinitionService
    {
        /// <summary>
        /// Gets all available Qobuz quality definitions.
        /// </summary>
        /// <returns>List of all quality definitions</returns>
        IReadOnlyList<QobuzQuality> GetAllQualities();

        /// <summary>
        /// Gets a quality definition by its ID.
        /// </summary>
        /// <param name="qualityId">The quality ID</param>
        /// <returns>The quality definition or null if not found</returns>
        QobuzQuality? GetQualityById(int qualityId);

        /// <summary>
        /// Gets the highest quality available.
        /// </summary>
        /// <returns>The highest quality definition</returns>
        QobuzQuality GetHighestQuality();

        /// <summary>
        /// Gets the default quality for fallback scenarios.
        /// </summary>
        /// <returns>The default quality definition</returns>
        QobuzQuality GetDefaultQuality();

        /// <summary>
        /// Validates if a quality ID is valid.
        /// </summary>
        /// <param name="qualityId">The quality ID to validate</param>
        /// <returns>True if the quality ID is valid</returns>
        bool IsValidQualityId(int qualityId);

        /// <summary>
        /// Gets qualities ordered by preference (highest to lowest).
        /// </summary>
        /// <returns>Qualities in preference order</returns>
        IReadOnlyList<QobuzQuality> GetQualitiesInPreferenceOrder();

        /// <summary>
        /// Maps a Lidarr quality profile to a Qobuz quality.
        /// </summary>
        /// <param name="lidarrQuality">The Lidarr quality object</param>
        /// <returns>The corresponding Qobuz quality ID</returns>
        int MapLidarrQualityToQobuz(object lidarrQuality);

        /// <summary>
        /// Gets the bitrate for a quality ID.
        /// </summary>
        /// <param name="qualityId">The quality ID</param>
        /// <returns>The bitrate in kbps or 0 if unknown</returns>
        int GetBitrate(int qualityId);

        /// <summary>
        /// Gets the format name for a quality ID.
        /// </summary>
        /// <param name="qualityId">The quality ID</param>
        /// <returns>The format name (e.g., "FLAC", "MP3")</returns>
        string GetFormatName(int qualityId);

        /// <summary>
        /// Gets all supported qualities as QualityFormat objects.
        /// </summary>
        /// <returns>List of supported quality formats</returns>
        IReadOnlyList<QualityFormat> GetSupportedQualities();

        /// <summary>
        /// Gets a quality by ID as QualityFormat (legacy method).
        /// </summary>
        /// <param name="qualityId">The quality ID</param>
        /// <returns>The quality format</returns>
        QualityFormat GetQualityByIdLegacy(int qualityId);

        /// <summary>
        /// Checks if a quality is supported.
        /// </summary>
        /// <param name="qualityId">The quality ID</param>
        /// <returns>True if supported</returns>
        bool IsQualitySupported(int qualityId);
    }
}