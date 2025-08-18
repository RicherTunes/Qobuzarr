using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;
using NzbDrone.Common.Http;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Manages quality fallback strategies and error analysis for track downloads
    /// </summary>
    public class QualityFallbackProvider : IQualityFallbackProvider
    {
        public List<int> GetFallbackQualities(int preferredQuality)
        {
            // Define fallback order based on preferred quality
            // NOTE: format_id 27 (192kHz) is BROKEN in Qobuz API - always skip it
            return preferredQuality switch
            {
                27 => new List<int> { 7, 6, 5 }, // 24/192 (broken) -> 24/96 -> CD -> MP3
                7 => new List<int> { 6, 5 },     // 24/96 -> CD -> MP3  
                6 => new List<int> { 5 },        // CD -> MP3
                5 => new List<int>(),            // MP3 (no fallback)
                _ => new List<int> { 7, 6, 5 }   // Default: try all working qualities
            };
        }

        public TrackUnavailableReason DetermineUnavailableReason(string restrictionMessage)
        {
            if (string.IsNullOrWhiteSpace(restrictionMessage))
                return TrackUnavailableReason.NoQualityAvailable;

            var message = restrictionMessage.ToLower();

            if (message.Contains("preview") || message.Contains("sample"))
                return TrackUnavailableReason.PreviewOnly;
            
            if (message.Contains("geo") || message.Contains("region") || message.Contains("country"))
                return TrackUnavailableReason.RegionalRestriction;
            
            if (message.Contains("subscription") || message.Contains("tier"))
                return TrackUnavailableReason.SubscriptionRestriction;
            
            if (message.Contains("format") && message.Contains("not available"))
                return TrackUnavailableReason.NoQualityAvailable;

            return TrackUnavailableReason.Unknown;
        }

        public bool IsRetryableException(Exception ex)
        {
            return ex switch
            {
                HttpRequestException => true,
                HttpException httpEx => (int)httpEx.Response.StatusCode >= 500, // Server errors are retryable
                TimeoutException => true,
                TaskCanceledException => false, // User cancellation should not be retried
                OperationCanceledException => false, // User cancellation should not be retried
                _ => false
            };
        }
    }
}