using Newtonsoft.Json;

namespace Lidarr.Plugin.Qobuzarr.Models
{
    public class QobuzMaximumQuality
    {
        [JsonProperty("format_id")]
        public int FormatId { get; set; }

        [JsonProperty("mime_type")]
        public string MimeType { get; set; }

        [JsonProperty("bit_depth")]
        public int? BitDepth { get; set; }

        [JsonProperty("sample_rate")]
        public double? SampleRate { get; set; }

        [JsonProperty("bitrate")]
        public int? Bitrate { get; set; }

        /// <summary>
        /// Get a human-readable quality description
        /// </summary>
        public string GetQualityDescription()
        {
            return FormatId switch
            {
                5 => "MP3 320kbps",
                6 => "FLAC 16-bit/44.1kHz (CD Quality)",
                7 => $"FLAC {BitDepth ?? 24}-bit/{SampleRate / 1000 ?? 96}kHz (Hi-Res)",
                27 => $"FLAC {BitDepth ?? 24}-bit/{SampleRate / 1000 ?? 192}kHz (Hi-Res)",
                _ => $"Unknown Format (ID: {FormatId})"
            };
        }

        /// <summary>
        /// Check if this is a lossless format
        /// </summary>
        public bool IsLossless()
        {
            return FormatId != 5; // Everything except MP3 is lossless
        }

        /// <summary>
        /// Check if this is Hi-Res quality
        /// </summary>
        public bool IsHiRes()
        {
            return FormatId is 7 or 27 || (SampleRate > 48000);
        }

        /// <summary>
        /// Get the approximate file size per minute in MB
        /// </summary>
        public double GetApproximateSizePerMinute()
        {
            return FormatId switch
            {
                5 => 2.4, // MP3 320kbps ≈ 2.4MB/min
                6 => 10.5, // FLAC CD ≈ 10.5MB/min
                7 => 24.0, // FLAC 24/96 ≈ 24MB/min
                27 => 36.0, // FLAC 24/192 ≈ 36MB/min
                _ => 10.0 // Default estimate
            };
        }

        /// <summary>
        /// Get the codec name
        /// </summary>
        public string GetCodec()
        {
            return FormatId switch
            {
                5 => "MP3",
                6 or 7 or 27 => "FLAC",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Map to Lidarr quality profile
        /// </summary>
        public string GetLidarrQuality()
        {
            return FormatId switch
            {
                5 => "MP3-320",
                6 => "FLAC",
                7 => "FLAC-HD",
                27 => "FLAC-HD",
                _ => "Unknown"
            };
        }
    }
}
