using System;

namespace Lidarr.Plugin.Qobuzarr.Exceptions
{
    /// <summary>
    /// Thrown by <c>QobuzParser.ParseResponse</c> when an HTTP 200 search response cannot be
    /// interpreted as a valid Qobuz search response: a non-JSON body, an empty body, JSON that
    /// matches no known search shape (DTO drift), or an explicit error status.
    ///
    /// Why a typed throw instead of the historical log-and-return-empty: QobuzIndexer's bespoke
    /// AccumulateAll loop counts any non-throwing parse as a SUCCESSFUL request (succeeded++),
    /// so an all-malformed wave never tripped <c>SearchPlanExecutor.ThrowAllFailed</c> and
    /// surfaced as a misleading "no results" instead of an indexer failure (P0-04, live audit
    /// 2026-07). Throwing routes the request into the loop's per-request failure accounting;
    /// genuine zero-result responses (valid shape, empty items) still return an empty list.
    /// </summary>
    public class QobuzInvalidSearchResponseException : Exception
    {
        public QobuzInvalidSearchResponseException(string message)
            : base(message)
        {
        }

        public QobuzInvalidSearchResponseException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
