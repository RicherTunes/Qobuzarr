using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;

namespace Lidarr.Plugin.Qobuzarr.Models.Lidarr
{
    /// <summary>
    /// Represents a quality profile in Lidarr that defines the preferred audio qualities for downloads.
    /// Quality profiles determine the order of preference for different audio formats and qualities.
    /// </summary>
    public class LidarrQualityProfile
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("upgradeAllowed")]
        public bool UpgradeAllowed { get; set; }

        [JsonProperty("cutoff")]
        public int Cutoff { get; set; }

        [JsonProperty("items")]
        public List<LidarrQualityProfileItem> Items { get; set; } = new List<LidarrQualityProfileItem>();

        [JsonProperty("language")]
        public LidarrLanguage Language { get; set; }

        [JsonProperty("minFormatScore")]
        public int MinFormatScore { get; set; }

        [JsonProperty("cutoffFormatScore")]
        public int CutoffFormatScore { get; set; }

        [JsonProperty("formatItems")]
        public List<LidarrFormatItem> FormatItems { get; set; } = new List<LidarrFormatItem>();

        /// <summary>
        /// Gets the highest priority (most preferred) quality from this profile.
        /// </summary>
        public LidarrQuality GetPreferredQuality()
        {
            var allowedItems = Items?.Where(i => i.Allowed)
                                   .SelectMany(i => i.Quality != null ? new[] { i.Quality } : i.Items?.Where(q => q.Allowed).Select(q => q.Quality) ?? Enumerable.Empty<LidarrQuality>())
                                   .Where(q => q != null);

            return allowedItems?.OrderByDescending(q => q.Resolution)
                               .ThenByDescending(q => q.Source)
                               .FirstOrDefault();
        }

        /// <summary>
        /// Gets all allowed qualities from this profile in order of preference (highest to lowest).
        /// </summary>
        public List<LidarrQuality> GetAllowedQualities()
        {
            var allowedQualities = new List<LidarrQuality>();

            foreach (var item in Items?.Where(i => i.Allowed) ?? Enumerable.Empty<LidarrQualityProfileItem>())
            {
                if (item.Quality != null)
                {
                    allowedQualities.Add(item.Quality);
                }
                else if (item.Items != null)
                {
                    allowedQualities.AddRange(item.Items.Where(i => i.Allowed).Select(i => i.Quality).Where(q => q != null));
                }
            }

            return allowedQualities.OrderByDescending(q => q.Resolution)
                                  .ThenByDescending(q => q.Source)
                                  .ToList();
        }

        /// <summary>
        /// Checks if a specific quality is allowed in this profile.
        /// </summary>
        public bool IsQualityAllowed(int qualityId)
        {
            return GetAllowedQualities().Any(q => q.Id == qualityId);
        }

        /// <summary>
        /// Gets the cutoff quality for this profile.
        /// </summary>
        public LidarrQuality GetCutoffQuality()
        {
            return GetAllowedQualities().FirstOrDefault(q => q.Id == Cutoff);
        }

        /// <summary>
        /// Determines if this profile prefers lossless audio formats.
        /// </summary>
        public bool PrefersLossless()
        {
            var preferredQuality = GetPreferredQuality();
            return preferredQuality?.Name?.ToLower().Contains("flac") == true ||
                   preferredQuality?.Name?.ToLower().Contains("lossless") == true ||
                   preferredQuality?.Resolution >= 16; // Assuming 16-bit or higher indicates lossless
        }

        /// <summary>
        /// Gets the maximum bit depth preference for this profile.
        /// </summary>
        public int GetMaxBitDepth()
        {
            return GetAllowedQualities().Max(q => q.Resolution);
        }
    }

    /// <summary>
    /// Represents a quality profile item which can be either a single quality or a group of qualities.
    /// </summary>
    public class LidarrQualityProfileItem
    {
        [JsonProperty("quality")]
        public LidarrQuality Quality { get; set; }

        [JsonProperty("items")]
        public List<LidarrQualityProfileItem> Items { get; set; }

        [JsonProperty("allowed")]
        public bool Allowed { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("id")]
        public int Id { get; set; }
    }

    /// <summary>
    /// Represents an audio quality definition in Lidarr.
    /// </summary>
    public class LidarrQuality
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("resolution")]
        public int Resolution { get; set; }

        [JsonProperty("modifier")]
        public string Modifier { get; set; }

        /// <summary>
        /// Maps the Lidarr quality to the closest Qobuz quality format.
        /// </summary>
        /// <returns>The corresponding Qobuz quality string (e.g., "flac-hires", "flac-cd", "mp3-320").</returns>
        public string ToQobuzQuality()
        {
            var name = Name?.ToLower() ?? "";
            var source = Source?.ToLower() ?? "";

            // High-resolution lossless formats
            if (Resolution >= 24 || name.Contains("24bit") || name.Contains("hires") || name.Contains("hi-res"))
            {
                return "flac-hires";
            }

            // Standard lossless formats (CD quality)
            if (Resolution >= 16 || name.Contains("flac") || name.Contains("lossless") || source.Contains("cd"))
            {
                return "flac-cd";
            }

            // High-quality lossy formats
            if (name.Contains("320") || Resolution == 320)
            {
                return "mp3-320";
            }

            // Default to MP3 320 for unknown qualities
            return "mp3-320";
        }

        /// <summary>
        /// Gets a human-readable description of this quality.
        /// </summary>
        public string GetDescription()
        {
            if (Resolution > 0)
            {
                return $"{Name} ({Resolution}-bit)";
            }
            return Name;
        }

        /// <summary>
        /// Determines if this quality represents a lossless format.
        /// </summary>
        public bool IsLossless()
        {
            var name = Name?.ToLower() ?? "";
            return name.Contains("flac") || name.Contains("lossless") || Resolution >= 16;
        }

        /// <summary>
        /// Determines if this quality represents a high-resolution format.
        /// </summary>
        public bool IsHighResolution()
        {
            return Resolution >= 24 || Name?.ToLower().Contains("hires") == true || Name?.ToLower().Contains("hi-res") == true;
        }
    }

    /// <summary>
    /// Represents a language preference in Lidarr.
    /// </summary>
    public class LidarrLanguage
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }

    /// <summary>
    /// Represents a custom format item in a quality profile.
    /// </summary>
    public class LidarrFormatItem
    {
        [JsonProperty("format")]
        public LidarrCustomFormat Format { get; set; }

        [JsonProperty("score")]
        public int Score { get; set; }
    }

    /// <summary>
    /// Represents a custom format definition in Lidarr.
    /// </summary>
    public class LidarrCustomFormat
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("specifications")]
        public List<LidarrFormatSpecification> Specifications { get; set; } = new List<LidarrFormatSpecification>();
    }

    /// <summary>
    /// Represents a specification within a custom format.
    /// </summary>
    public class LidarrFormatSpecification
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("implementation")]
        public string Implementation { get; set; }

        [JsonProperty("negate")]
        public bool Negate { get; set; }

        [JsonProperty("required")]
        public bool Required { get; set; }

        [JsonProperty("fields")]
        public List<LidarrFormatField> Fields { get; set; } = new List<LidarrFormatField>();
    }

    /// <summary>
    /// Represents a field within a format specification.
    /// </summary>
    public class LidarrFormatField
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("value")]
        public object Value { get; set; }
    }
}
