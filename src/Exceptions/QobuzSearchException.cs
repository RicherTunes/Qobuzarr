using System;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Exception thrown when Qobuz search operations fail
    /// </summary>
    public class QobuzSearchException : Exception
    {
        /// <summary>
        /// The type of search that failed
        /// </summary>
        public SearchType SearchType { get; }

        /// <summary>
        /// Whether this error is likely recoverable with a retry
        /// </summary>
        public bool IsRetryable { get; }

        /// <summary>
        /// The search query that failed (if applicable)
        /// </summary>
        public string Query { get; }

        public QobuzSearchException(string message, Exception innerException, SearchType searchType, bool isRetryable = false, string query = null)
            : base(message, innerException)
        {
            SearchType = searchType;
            IsRetryable = isRetryable;
            Query = query;
        }

        public QobuzSearchException(string message, SearchType searchType, bool isRetryable = false, string query = null)
            : base(message)
        {
            SearchType = searchType;
            IsRetryable = isRetryable;
            Query = query;
        }
    }

    /// <summary>
    /// Type of search operation
    /// </summary>
    public enum SearchType
    {
        Album,
        Artist,
        Track,
        Playlist,
        Label,
        General
    }
}
