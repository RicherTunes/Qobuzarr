using System;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;

namespace Lidarr.Plugin.Qobuzarr.Services.Quality
{
    /// <summary>
    /// Service for mapping between Lidarr and Qobuz quality profiles.
    /// Extracted from QobuzQualityManager to follow Single Responsibility Principle.
    /// </summary>
    public class QualityMappingService : IQualityMappingService
    {
        // Quality format definitions (consolidated from multiple services)
        public static readonly Dictionary<int, Models.QualityFormat> QobuzQualityFormats = new()
        {
            { 5, new Models.QualityFormat { Id = 5, Name = "MP3 320", DisplayName = "MP3 320kbps", BitRate = 320, IsLossless = false, Priority = 1 } },
            { 6, new Models.QualityFormat { Id = 6, Name = "FLAC CD", DisplayName = "FLAC CD 16bit/44.1kHz", BitRate = 1411, IsLossless = true, Priority = 2 } },
            { 7, new Models.QualityFormat { Id = 7, Name = "FLAC Hi-Res 96", DisplayName = "FLAC Hi-Res 24bit/96kHz", BitRate = 4608, IsLossless = true, Priority = 3 } },
            { 27, new Models.QualityFormat { Id = 27, Name = "FLAC Hi-Res 192", DisplayName = "FLAC Hi-Res 24bit/192kHz", BitRate = 9216, IsLossless = true, Priority = 4 } }
        };

        // Lidarr quality profile mappings (consolidated from QualityMappingService)
        private static readonly Dictionary<string, int> LidarrQualityMappings = new(StringComparer.OrdinalIgnoreCase)
        {
            // Hi-Res patterns
            ["Hi-Res"] = 27, ["HiRes"] = 27, ["High Resolution"] = 27,
            ["24bit"] = 27, ["24-bit"] = 27, ["192khz"] = 27, ["96khz"] = 7,
            
            // CD Quality patterns
            ["Lossless"] = 6, ["FLAC"] = 6, ["CD"] = 6,
            ["16bit"] = 6, ["16-bit"] = 6, ["44.1khz"] = 6,
            
            // Lossy patterns
            ["MP3"] = 5, ["320"] = 5, ["Lossy"] = 5, ["Standard"] = 5
        };

        public QobuzQuality MapLidarrQuality(LidarrQualityProfile profile)
        {
            if (profile == null)
            {
                return GetDefaultQuality();
            }

            // Try mapping based on profile name
            foreach (var mapping in LidarrQualityMappings)
            {
                if (profile.Name.Contains(mapping.Key, StringComparison.OrdinalIgnoreCase))
                {
                    var format = QobuzQualityFormats[mapping.Value];
                    return new QobuzQuality
                    {
                        Id = format.Id,
                        Name = format.Name,
                        DisplayName = format.DisplayName
                    };
                }
            }

            // Analyze quality items in profile
            var preferredQuality = profile.GetPreferredQuality();
            if (preferredQuality != null)
            {
                return MapLidarrQuality(preferredQuality);
            }

            // Default fallback
            return GetDefaultQuality();
        }

        public QobuzQuality MapLidarrQuality(LidarrQuality quality)
        {
            // Map based on quality properties
            var qualityId = 6; // Default to CD quality
            
            if (quality.Name.Contains("Hi-Res", StringComparison.OrdinalIgnoreCase) ||
                quality.Name.Contains("24", StringComparison.OrdinalIgnoreCase))
            {
                qualityId = 27;
            }
            else if (quality.Name.Contains("MP3", StringComparison.OrdinalIgnoreCase) ||
                     quality.Name.Contains("320", StringComparison.OrdinalIgnoreCase))
            {
                qualityId = 5;
            }

            var format = QobuzQualityFormats[qualityId];
            return new QobuzQuality
            {
                Id = format.Id,
                Name = format.Name,
                DisplayName = format.DisplayName
            };
        }

        public List<QobuzQuality> GetQualityFallbackChain(QobuzQuality preferred)
        {
            var chain = new List<QobuzQuality>();
            
            // Start with preferred quality
            if (preferred != null && QobuzQualityFormats.ContainsKey(preferred.Id))
            {
                chain.Add(preferred);
            }
            
            // Add lower qualities as fallbacks
            var preferredPriority = QobuzQualityFormats.TryGetValue(preferred?.Id ?? 0, out var format) 
                ? format.Priority 
                : int.MaxValue;
                
            foreach (var quality in QobuzQualityFormats.Values
                .Where(q => q.Priority < preferredPriority)
                .OrderByDescending(q => q.Priority))
            {
                chain.Add(new QobuzQuality
                {
                    Id = quality.Id,
                    Name = quality.Name,
                    DisplayName = quality.DisplayName
                });
            }
            
            // Ensure at least MP3 is in the chain
            if (!chain.Any(q => q.Id == 5))
            {
                var mp3 = QobuzQualityFormats[5];
                chain.Add(new QobuzQuality
                {
                    Id = mp3.Id,
                    Name = mp3.Name,
                    DisplayName = mp3.DisplayName
                });
            }
            
            return chain;
        }

        public QobuzQuality GetDefaultQuality()
        {
            var format = QobuzQualityFormats[6]; // CD quality as default
            return new QobuzQuality
            {
                Id = format.Id,
                Name = format.Name,
                DisplayName = format.DisplayName
            };
        }

        public Dictionary<int, Models.QualityFormat> GetQualityFormats()
        {
            return QobuzQualityFormats;
        }
    }
}