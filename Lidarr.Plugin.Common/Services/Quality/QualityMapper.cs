using System;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Common.Models;

namespace Lidarr.Plugin.Common.Services.Quality
{
    /// <summary>
    /// Utilities for mapping and comparing audio quality across different streaming services.
    /// Provides a unified way to handle quality differences between services.
    /// </summary>
    public static class QualityMapper
    {
        /// <summary>
        /// Standard quality definitions for common scenarios.
        /// </summary>
        public static class StandardQualities
        {
            public static readonly StreamingQuality Mp3Low = new StreamingQuality
            {
                Id = "mp3_128",
                Name = "MP3 128kbps",
                Format = "MP3",
                Bitrate = 128
            };

            public static readonly StreamingQuality Mp3Normal = new StreamingQuality
            {
                Id = "mp3_256",
                Name = "MP3 256kbps", 
                Format = "MP3",
                Bitrate = 256
            };

            public static readonly StreamingQuality Mp3High = new StreamingQuality
            {
                Id = "mp3_320",
                Name = "MP3 320kbps",
                Format = "MP3", 
                Bitrate = 320
            };

            public static readonly StreamingQuality FlacCD = new StreamingQuality
            {
                Id = "flac_cd",
                Name = "FLAC CD Quality",
                Format = "FLAC",
                SampleRate = 44100,
                BitDepth = 16
            };

            public static readonly StreamingQuality FlacHiRes = new StreamingQuality
            {
                Id = "flac_hires",
                Name = "FLAC Hi-Res",
                Format = "FLAC",
                SampleRate = 96000,
                BitDepth = 24
            };

            public static readonly StreamingQuality FlacMax = new StreamingQuality
            {
                Id = "flac_max",
                Name = "FLAC Studio Master",
                Format = "FLAC",
                SampleRate = 192000,
                BitDepth = 24
            };
        }

        /// <summary>
        /// Maps a streaming quality to a universal tier for comparison.
        /// </summary>
        public static StreamingQualityTier GetQualityTier(StreamingQuality quality)
        {
            if (quality == null) return StreamingQualityTier.Low;

            // High-resolution audio (>44.1kHz or >16bit)
            if (quality.IsHighResolution)
                return StreamingQualityTier.HiRes;

            // Lossless formats at CD quality or below
            if (quality.IsLossless)
                return StreamingQualityTier.Lossless;

            // Lossy formats by bitrate
            if (quality.Bitrate.HasValue)
            {
                if (quality.Bitrate >= 320) return StreamingQualityTier.High;
                if (quality.Bitrate >= 160) return StreamingQualityTier.Normal;
                return StreamingQualityTier.Low;
            }

            return StreamingQualityTier.Normal; // Default fallback
        }

        /// <summary>
        /// Finds the best matching quality from available options based on preference.
        /// </summary>
        public static StreamingQuality FindBestMatch(
            IEnumerable<StreamingQuality> availableQualities, 
            StreamingQualityTier preferredTier = StreamingQualityTier.Lossless)
        {
            if (availableQualities == null || !availableQualities.Any())
                return null;

            var qualities = availableQualities.ToList();

            // First, try to find exact tier match
            var exactMatches = qualities.Where(q => GetQualityTier(q) == preferredTier).ToList();
            if (exactMatches.Any())
            {
                return GetBestFromTier(exactMatches);
            }

            // If no exact match, find the closest higher quality
            for (var tier = preferredTier + 1; tier <= StreamingQualityTier.HiRes; tier++)
            {
                var higherMatches = qualities.Where(q => GetQualityTier(q) == tier).ToList();
                if (higherMatches.Any())
                {
                    return GetBestFromTier(higherMatches);
                }
            }

            // If no higher quality available, find the closest lower quality
            for (var tier = preferredTier - 1; tier >= StreamingQualityTier.Low; tier--)
            {
                var lowerMatches = qualities.Where(q => GetQualityTier(q) == tier).ToList();
                if (lowerMatches.Any())
                {
                    return GetBestFromTier(lowerMatches);
                }
            }

            // Fallback to any available quality
            return GetBestFromTier(qualities);
        }

        /// <summary>
        /// Gets the best quality from a group within the same tier.
        /// </summary>
        private static StreamingQuality GetBestFromTier(IList<StreamingQuality> qualities)
        {
            if (!qualities.Any()) return null;
            if (qualities.Count == 1) return qualities[0];

            return qualities
                .OrderByDescending(q => q.BitDepth ?? 0)
                .ThenByDescending(q => q.SampleRate ?? 0) 
                .ThenByDescending(q => q.Bitrate ?? 0)
                .First();
        }

        /// <summary>
        /// Compares two qualities and returns which one is better.
        /// </summary>
        public static int CompareQualities(StreamingQuality quality1, StreamingQuality quality2)
        {
            if (quality1 == null && quality2 == null) return 0;
            if (quality1 == null) return -1;
            if (quality2 == null) return 1;

            var tier1 = GetQualityTier(quality1);
            var tier2 = GetQualityTier(quality2);

            // Compare by tier first
            var tierComparison = tier1.CompareTo(tier2);
            if (tierComparison != 0) return tierComparison;

            // Within same tier, compare by technical specs
            var bitDepthComparison = (quality1.BitDepth ?? 0).CompareTo(quality2.BitDepth ?? 0);
            if (bitDepthComparison != 0) return bitDepthComparison;

            var sampleRateComparison = (quality1.SampleRate ?? 0).CompareTo(quality2.SampleRate ?? 0);
            if (sampleRateComparison != 0) return sampleRateComparison;

            return (quality1.Bitrate ?? 0).CompareTo(quality2.Bitrate ?? 0);
        }

        /// <summary>
        /// Creates a quality preference mapping for a specific use case.
        /// </summary>
        public static QualityPreferenceMap CreatePreferenceMap(StreamingQualityTier preferredTier, bool allowHigher = true, bool allowLower = true)
        {
            return new QualityPreferenceMap
            {
                PreferredTier = preferredTier,
                AllowHigherQuality = allowHigher,
                AllowLowerQuality = allowLower,
                MaxAcceptableTier = allowHigher ? StreamingQualityTier.HiRes : preferredTier,
                MinAcceptableTier = allowLower ? StreamingQualityTier.Low : preferredTier
            };
        }

        /// <summary>
        /// Converts a numeric quality value to a standard quality object.
        /// Useful for services that use numeric quality IDs.
        /// </summary>
        public static StreamingQuality FromNumericId(int qualityId, string serviceName = "Unknown")
        {
            // Common mapping patterns (can be overridden by services)
            return qualityId switch
            {
                5 => new StreamingQuality { Id = "5", Name = "MP3 320kbps", Format = "MP3", Bitrate = 320 },
                6 => new StreamingQuality { Id = "6", Name = "FLAC CD", Format = "FLAC", SampleRate = 44100, BitDepth = 16 },
                7 => new StreamingQuality { Id = "7", Name = "FLAC Hi-Res", Format = "FLAC", SampleRate = 96000, BitDepth = 24 },
                27 => new StreamingQuality { Id = "27", Name = "FLAC Studio Master", Format = "FLAC", SampleRate = 192000, BitDepth = 24 },
                _ => new StreamingQuality { Id = qualityId.ToString(), Name = $"{serviceName} Quality {qualityId}", Format = "Unknown" }
            };
        }

        /// <summary>
        /// Converts a string quality descriptor to a standard quality object.
        /// Useful for services that use string quality descriptors.
        /// </summary>
        public static StreamingQuality FromStringDescriptor(string descriptor, string serviceName = "Unknown")
        {
            if (string.IsNullOrEmpty(descriptor)) return null;

            var lower = descriptor.ToLowerInvariant();

            return lower switch
            {
                "low" or "normal" => StandardQualities.Mp3Low,
                "high" => StandardQualities.Mp3High,
                "lossless" => StandardQualities.FlacCD,
                "master" or "hi_res" or "hires" => StandardQualities.FlacHiRes,
                "studio_master" or "max" => StandardQualities.FlacMax,
                _ => new StreamingQuality 
                { 
                    Id = descriptor, 
                    Name = $"{serviceName} {descriptor}", 
                    Format = "Unknown" 
                }
            };
        }

        /// <summary>
        /// Gets human-readable description of quality differences.
        /// </summary>
        public static string GetQualityDescription(StreamingQuality quality)
        {
            if (quality == null) return "Unknown Quality";

            var parts = new List<string>();

            if (!string.IsNullOrEmpty(quality.Format))
                parts.Add(quality.Format.ToUpperInvariant());

            if (quality.IsLossless)
            {
                if (quality.SampleRate.HasValue && quality.BitDepth.HasValue)
                {
                    parts.Add($"{quality.SampleRate / 1000.0:F1}kHz/{quality.BitDepth}bit");
                }
                
                if (quality.IsHighResolution)
                    parts.Add("Hi-Res");
            }
            else if (quality.Bitrate.HasValue)
            {
                parts.Add($"{quality.Bitrate}kbps");
            }

            var description = string.Join(" ", parts);
            return string.IsNullOrEmpty(description) ? quality.Name ?? "Unknown Quality" : description;
        }
    }

    /// <summary>
    /// Configuration for quality preference mapping.
    /// </summary>
    public class QualityPreferenceMap
    {
        public StreamingQualityTier PreferredTier { get; set; }
        public bool AllowHigherQuality { get; set; } = true;
        public bool AllowLowerQuality { get; set; } = true;
        public StreamingQualityTier MaxAcceptableTier { get; set; } = StreamingQualityTier.HiRes;
        public StreamingQualityTier MinAcceptableTier { get; set; } = StreamingQualityTier.Low;

        /// <summary>
        /// Checks if a quality tier is acceptable according to this preference map.
        /// </summary>
        public bool IsAcceptable(StreamingQualityTier tier)
        {
            return tier >= MinAcceptableTier && tier <= MaxAcceptableTier;
        }

        /// <summary>
        /// Gets a score for a quality tier (higher = more preferred).
        /// </summary>
        public int GetPreferenceScore(StreamingQualityTier tier)
        {
            if (!IsAcceptable(tier)) return -1;
            
            // Perfect match gets highest score
            if (tier == PreferredTier) return 100;
            
            // Calculate distance from preferred tier
            var distance = Math.Abs((int)tier - (int)PreferredTier);
            
            // Higher quality than preferred gets slight bonus if allowed
            if (tier > PreferredTier && AllowHigherQuality)
                return 90 - distance;
            
            // Lower quality than preferred gets penalty but still acceptable if allowed
            if (tier < PreferredTier && AllowLowerQuality)
                return 80 - (distance * 10);
            
            return 0;
        }
    }
}