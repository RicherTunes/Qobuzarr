using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;

namespace Lidarr.Plugin.Qobuzarr.Models
{
    public class QobuzStreamResponse
    {
        [JsonProperty("url")]
        public string Url { get; set; } = string.Empty;

        [JsonProperty("format_id")]
        public int FormatId { get; set; }

        [JsonProperty("mime_type")]
        public string MimeType { get; set; } = string.Empty;

        [JsonProperty("bit_depth")]
        public int? BitDepth { get; set; }

        [JsonProperty("sample_rate")]
        public double? SampleRate { get; set; }

        [JsonProperty("bitrate")]
        public int? Bitrate { get; set; }

        [JsonProperty("track_id")]
        public string TrackId { get; set; } = string.Empty;

        [JsonProperty("restrictions")]
        public List<QobuzStreamRestriction> Restrictions { get; set; } = new();

        [JsonProperty("expires")]
        public long? ExpiresTimestamp { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; } = string.Empty;

        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;

        [JsonProperty("code")]
        public int? Code { get; set; }

        /// <summary>
        /// Check if the stream response is successful
        /// </summary>
        public bool IsSuccess => Code == null || Code == 200;

        /// <summary>
        /// Get expiration time as DateTime (if available)
        /// </summary>
        public DateTime? ExpiresAt
        {
            get
            {
                if (ExpiresTimestamp.HasValue && ExpiresTimestamp > 0)
                {
                    return DateTimeOffset.FromUnixTimeSeconds(ExpiresTimestamp.Value).DateTime;
                }
                return null;
            }
        }

        /// <summary>
        /// Check if the stream URL has expired
        /// </summary>
        public bool IsExpired()
        {
            var expiresAt = ExpiresAt;
            return expiresAt.HasValue && DateTime.UtcNow >= expiresAt.Value;
        }

        /// <summary>
        /// Check if the stream URL expires within the specified timespan
        /// </summary>
        public bool ExpiresWithin(TimeSpan timespan)
        {
            var expiresAt = ExpiresAt;
            return expiresAt.HasValue && DateTime.UtcNow.Add(timespan) >= expiresAt.Value;
        }

        /// <summary>
        /// Get file extension based on format
        /// </summary>
        public string GetFileExtension()
        {
            return FormatId switch
            {
                5 => ".mp3",
                6 or 7 or 27 => ".flac",
                _ => ".unknown"
            };
        }

        /// <summary>
        /// Get quality description
        /// </summary>
        public string GetQualityDescription()
        {
            return FormatId switch
            {
                5 => "MP3 320kbps",
                6 => "FLAC 16-bit/44.1kHz",
                7 => $"FLAC {BitDepth ?? 24}-bit/{SampleRate / 1000 ?? 96}kHz",
                27 => $"FLAC {BitDepth ?? 24}-bit/{SampleRate / 1000 ?? 192}kHz",
                _ => $"Unknown Format (ID: {FormatId})"
            };
        }

        /// <summary>
        /// Check if this is a lossless stream
        /// </summary>
        public bool IsLossless()
        {
            return FormatId != 5;
        }

        /// <summary>
        /// Check if this is Hi-Res quality
        /// </summary>
        public bool IsHiRes()
        {
            return FormatId is 7 or 27;
        }

        /// <summary>
        /// Check if there are any restrictions
        /// </summary>
        public bool HasRestrictions()
        {
            return Restrictions?.Any(r => r.HasRestrictions()) == true;
        }

        /// <summary>
        /// Get human-readable restriction message
        /// </summary>
        public string? GetRestrictionMessage()
        {
            var restriction = Restrictions?.FirstOrDefault(r => r.HasRestrictions());
            return restriction?.GetRestrictionMessage();
        }
    }

    public class QobuzStreamRestriction
    {
        [JsonProperty("code")]
        public string Code { get; set; } = string.Empty;

        [JsonProperty("reason")]
        public string Reason { get; set; } = string.Empty;

        [JsonProperty("reason_code")]
        public string ReasonCode { get; set; } = string.Empty;

        [JsonProperty("country_codes")]
        public string[] CountryCodes { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Check if there are any restrictions
        /// </summary>
        public bool HasRestrictions()
        {
            return Code.IsNotNullOrWhiteSpace() ||
                   Reason.IsNotNullOrWhiteSpace() || 
                   ReasonCode.IsNotNullOrWhiteSpace() ||
                   CountryCodes?.Length > 0;
        }

        /// <summary>
        /// Get human-readable restriction message
        /// </summary>
        public string? GetRestrictionMessage()
        {
            if (!HasRestrictions())
                return null;

            if (Reason.IsNotNullOrWhiteSpace())
                return Reason;

            if (Code.IsNotNullOrWhiteSpace())
            {
                return Code switch
                {
                    "FormatRestrictedByFormatAvailability" => "Requested format not available, using fallback quality",
                    "GeoRestricted" => "Content not available in your region",
                    "SubscriptionRestricted" => "Subscription tier insufficient for this quality",
                    _ => $"Content restricted ({Code})"
                };
            }

            return ReasonCode switch
            {
                "GEO" => "Content not available in your country",
                "SUB" => "Subscription tier insufficient for this quality",
                "TEMP" => "Temporarily unavailable",
                _ => $"Content restricted ({ReasonCode})"
            };
        }
    }
}