using System;
using System.Collections.Generic;
using Lidarr.Plugin.Qobuzarr.Services.Consolidated;

namespace Lidarr.Plugin.Qobuzarr.Models
{
    /// <summary>
    /// Represents quality detection result for a track.
    /// </summary>
    public class QualityDetectionResult
    {
        public string TrackId { get; set; }
        public List<QualityFormat> AvailableQualities { get; set; } = new();
        public DateTime CheckedAt { get; set; }
        public bool Success => AvailableQualities?.Count > 0;
        public string Error { get; set; }
    }

    /// <summary>
    /// Represents quality detection result for an entire album.
    /// </summary>
    public class AlbumQualityResult
    {
        public string AlbumId { get; set; }
        public QualityFormat UniformQuality { get; set; }
        public bool IsUniform { get; set; }
        public Dictionary<string, QualityFormat> TrackQualities { get; set; } = new();
        public int SamplesTaken { get; set; }
        public int TracksChecked { get; set; }
        public double OptimizationRate => TracksChecked > 0 ? 1.0 - ((double)TracksChecked / SamplesTaken) : 0;
        public DateTime CheckedAt { get; set; }
        public TimeSpan TimeSaved { get; set; }
    }

    /// <summary>
    /// Represents a Qobuz quality format.
    /// </summary>
    public class QualityFormat
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public int BitRate { get; set; }
        public bool IsLossless { get; set; }
        public int Priority { get; set; }
        public int? BitDepth { get; set; }
        public int? SampleRate { get; set; }
    }


    /// <summary>
    /// Result of a quality selection operation.
    /// </summary>
    public class QualitySelectionResult
    {
        public bool Success { get; set; }
        public QobuzQuality SelectedQuality { get; set; }
        public StreamInfo StreamInfo { get; set; }
        public bool FallbackUsed { get; set; }
        public int AttemptsCount { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// Stream information for a track.
    /// </summary>
    public class StreamInfo
    {
        public string TrackId { get; set; }
        public string Url { get; set; }
        public int QualityId { get; set; }
        public QobuzQuality Quality { get; set; }
        public DateTime ExpiresAt { get; set; }
        public long? FileSize { get; set; }
        public string MimeType { get; set; }
    }

    /// <summary>
    /// Result of batch stream information retrieval.
    /// </summary>
    public class BatchStreamResult
    {
        public QobuzQuality RequestedQuality { get; set; }
        public Dictionary<string, StreamInfo> TrackResults { get; set; } = new();
        public Dictionary<string, StreamInfo> StreamInfos { get; set; } = new();
        public List<string> FailedTracks { get; set; } = new();
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public double SuccessRate { get; set; }
        public DateTime ProcessedAt { get; set; }
    }


    /// <summary>
    /// Album quality cache entry.
    /// </summary>
    internal class AlbumQualityCache
    {
        public string AlbumId { get; set; }
        public QualityFormat UniformQuality { get; set; }
        public bool IsUniform { get; set; }
        public DateTime CachedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}