using System;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services.Interfaces;
using Lidarr.Plugin.Qobuzarr.Services.Consolidated;

namespace Lidarr.Plugin.Qobuzarr.Services.Core.Quality
{
    /// <summary>
    /// Implementation of quality definition service with immutable quality specifications.
    /// Implements the centralized IQualityDefinitionService interface.
    /// </summary>
    public class QualityDefinitionService : IQualityDefinitionService
    {
        // Map legacy QualityFormat to new QobuzQuality
        private static readonly IReadOnlyDictionary<int, QobuzQuality> QualityFormats = 
            new Dictionary<int, QobuzQuality>
            {
                { 5, new QobuzQuality { Id = 5, Name = "MP3 320", DisplayName = "MP3 320kbps", BitRate = 320, IsLossless = false, Priority = 1, Format = "MP3" } },
                { 6, new QobuzQuality { Id = 6, Name = "FLAC CD", DisplayName = "FLAC CD 16bit/44.1kHz", BitRate = 1411, IsLossless = true, Priority = 2, Format = "FLAC" } },
                { 7, new QobuzQuality { Id = 7, Name = "FLAC Hi-Res 96", DisplayName = "FLAC Hi-Res 24bit/96kHz", BitRate = 4608, IsLossless = true, Priority = 3, Format = "FLAC" } },
                { 27, new QobuzQuality { Id = 27, Name = "FLAC Hi-Res 192", DisplayName = "FLAC Hi-Res 24bit/192kHz", BitRate = 9216, IsLossless = true, Priority = 4, Format = "FLAC" } }
            };

        private static readonly IReadOnlyList<QobuzQuality> SortedQualities = 
            QualityFormats.Values.OrderByDescending(q => q.Priority).ToList();

        // Implement centralized interface methods
        public IReadOnlyList<QobuzQuality> GetAllQualities() => SortedQualities;

        public QobuzQuality? GetQualityById(int qualityId)
        {
            return QualityFormats.TryGetValue(qualityId, out var format) ? format : null;
        }

        public QobuzQuality GetHighestQuality() => QualityFormats[27]; // Hi-Res 192

        public QobuzQuality GetDefaultQuality() => QualityFormats[6]; // CD quality

        public bool IsValidQualityId(int qualityId) => QualityFormats.ContainsKey(qualityId);

        public IReadOnlyList<QobuzQuality> GetQualitiesInPreferenceOrder() => SortedQualities;

        public int MapLidarrQualityToQobuz(object lidarrQuality)
        {
            // Simple mapping - in production this would analyze the lidarrQuality object
            // For now, return CD quality as default
            return 6;
        }

        public int GetBitrate(int qualityId)
        {
            var quality = GetQualityById(qualityId);
            return quality?.BitRate ?? 0;
        }

        public string GetFormatName(int qualityId)
        {
            var quality = GetQualityById(qualityId);
            return quality?.Format ?? "Unknown";
        }

        // Legacy support methods for existing code
        public IReadOnlyList<QualityFormat> GetSupportedQualities() 
        {
            return QualityFormats.Values.Select(q => new QualityFormat 
            { 
                Id = q.Id, 
                Name = q.Name, 
                DisplayName = q.DisplayName, 
                BitRate = q.BitRate, 
                IsLossless = q.IsLossless, 
                Priority = q.Priority 
            }).OrderByDescending(q => q.Priority).ToList().AsReadOnly();
        }

        public QualityFormat GetQualityByIdLegacy(int qualityId)
        {
            var quality = GetQualityById(qualityId);
            if (quality == null)
                throw new ArgumentException($"Quality ID {qualityId} is not supported", nameof(qualityId));
            
            return new QualityFormat
            {
                Id = quality.Id,
                Name = quality.Name,
                DisplayName = quality.DisplayName,
                BitRate = quality.BitRate,
                IsLossless = quality.IsLossless,
                Priority = quality.Priority
            };
        }

        public bool IsQualitySupported(int qualityId) => IsValidQualityId(qualityId);
    }

}