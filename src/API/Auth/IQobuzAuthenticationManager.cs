using System;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Lidarr.Plugin.Qobuzarr.API.Auth
{
    /// <summary>
    /// Manages Qobuz authentication sessions including validation, renewal, and storage.
    /// This interface is responsible for all session-related operations.
    /// </summary>
    public interface IQobuzAuthenticationManager
    {
        /// <summary>
        /// Gets the current authentication session.
        /// </summary>
        QobuzSession? CurrentSession { get; }

        /// <summary>
        /// Sets the authentication session.
        /// </summary>
        /// <param name="session">The session to set.</param>
        void SetSession(QobuzSession session);

        /// <summary>
        /// Clears the current authentication session.
        /// </summary>
        void ClearSession();

        /// <summary>
        /// Checks if the current session is valid.
        /// </summary>
        /// <returns>True if the session is valid; false otherwise.</returns>
        bool HasValidSession();

        /// <summary>
        /// Checks if the session needs renewal (e.g., approaching expiration).
        /// </summary>
        /// <returns>True if the session should be renewed; false otherwise.</returns>
        bool NeedsRenewal();

        /// <summary>
        /// Validates and potentially renews the session if needed.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>Task representing the asynchronous operation.</returns>
        Task ValidateAndRenewIfNeededAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Event raised when the session is about to expire.
        /// </summary>
        event EventHandler<SessionExpiringEventArgs>? SessionExpiring;

        /// <summary>
        /// Event raised when the session has expired.
        /// </summary>
        event EventHandler? SessionExpired;
    }

    /// <summary>
    /// Event arguments for session expiration warnings.
    /// </summary>
    public class SessionExpiringEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the time remaining until the session expires.
        /// </summary>
        public TimeSpan TimeRemaining { get; }

        /// <summary>
        /// Gets the session that is expiring.
        /// </summary>
        public QobuzSession Session { get; }

        public SessionExpiringEventArgs(QobuzSession session, TimeSpan timeRemaining)
        {
            Session = session;
            TimeRemaining = timeRemaining;
        }
    }
}