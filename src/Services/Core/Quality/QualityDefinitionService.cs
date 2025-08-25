using System;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Services.Core.Quality
{
    /// <summary>
    /// Provides immutable quality definitions and metadata for Qobuz audio formats.
    /// Single responsibility: Define and provide access to quality format specifications.
    /// </summary>
    public interface IQualityDefinitionService
    {
        /// <summary>
        /// Gets all supported quality formats ordered by priority.
        /// </summary>
        IReadOnlyList<QualityFormat> GetSupportedQualities();

        /// <summary>
        /// Gets quality format by ID.
        /// </summary>
        QualityFormat GetQualityById(int qualityId);

        /// <summary>
        /// Checks if a quality ID is supported.
        /// </summary>
        bool IsQualitySupported(int qualityId);

        /// <summary>
        /// Gets the default fallback quality (CD quality).
        /// </summary>
        QualityFormat GetDefaultQuality();

        /// <summary>
        /// Gets the highest available quality format.
        /// </summary>
        QualityFormat GetHighestQuality();
    }

    /// <summary>
    /// Implementation of quality definition service with immutable quality specifications.
    /// </summary>
    public class QualityDefinitionService : IQualityDefinitionService
    {
        private static readonly IReadOnlyDictionary<int, QualityFormat> QualityFormats = 
            new Dictionary<int, QualityFormat>
            {
                { 5, new QualityFormat { Id = 5, Name = "MP3 320", DisplayName = "MP3 320kbps", BitRate = 320, IsLossless = false, Priority = 1 } },
                { 6, new QualityFormat { Id = 6, Name = "FLAC CD", DisplayName = "FLAC CD 16bit/44.1kHz", BitRate = 1411, IsLossless = true, Priority = 2 } },
                { 7, new QualityFormat { Id = 7, Name = "FLAC Hi-Res 96", DisplayName = "FLAC Hi-Res 24bit/96kHz", BitRate = 4608, IsLossless = true, Priority = 3 } },
                { 27, new QualityFormat { Id = 27, Name = "FLAC Hi-Res 192", DisplayName = "FLAC Hi-Res 24bit/192kHz", BitRate = 9216, IsLossless = true, Priority = 4 } }
            };

        private static readonly IReadOnlyList<QualityFormat> SortedQualities = 
            QualityFormats.Values.OrderByDescending(q => q.Priority).ToList();

        public IReadOnlyList<QualityFormat> GetSupportedQualities() => SortedQualities;

        public QualityFormat GetQualityById(int qualityId)
        {
            return QualityFormats.TryGetValue(qualityId, out var format) 
                ? format 
                : throw new ArgumentException($"Quality ID {qualityId} is not supported", nameof(qualityId));
        }

        public bool IsQualitySupported(int qualityId) => QualityFormats.ContainsKey(qualityId);

        public QualityFormat GetDefaultQuality() => QualityFormats[6]; // CD quality

        public QualityFormat GetHighestQuality() => QualityFormats[27]; // Hi-Res 192
    }

    /// <summary>
    /// Quality format definition with immutable properties.
    /// </summary>
    public class QualityFormat
    {
        public int Id { get; init; }
        public string Name { get; init; }
        public string DisplayName { get; init; }
        public int BitRate { get; init; }
        public bool IsLossless { get; init; }
        public int Priority { get; init; }
    }
}