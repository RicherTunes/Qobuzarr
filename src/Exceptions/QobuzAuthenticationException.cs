using System;

namespace Lidarr.Plugin.Qobuzarr.Exceptions
{
    /// <summary>
    /// Exception thrown when authentication with Qobuz API fails.
    /// </summary>
    public class QobuzAuthenticationException : Exception
    {
        /// <summary>
        /// The type of authentication failure.
        /// </summary>
        public AuthenticationFailureType FailureType { get; }

        /// <summary>
        /// Whether this failure might be temporary (e.g., network issue).
        /// </summary>
        public bool IsTemporary { get; }

        public QobuzAuthenticationException(string message, AuthenticationFailureType failureType, bool isTemporary = false)
            : base(message)
        {
            FailureType = failureType;
            IsTemporary = isTemporary;
        }

        public QobuzAuthenticationException(string message, AuthenticationFailureType failureType, Exception innerException, bool isTemporary = false)
            : base(message, innerException)
        {
            FailureType = failureType;
            IsTemporary = isTemporary;
        }

        /// <summary>
        /// Creates an exception for invalid credentials.
        /// </summary>
        public static QobuzAuthenticationException InvalidCredentials(string details = null)
        {
            var message = "Invalid Qobuz credentials";
            if (!string.IsNullOrWhiteSpace(details))
                message += $": {details}";
            return new QobuzAuthenticationException(message, AuthenticationFailureType.InvalidCredentials);
        }

        /// <summary>
        /// Creates an exception for missing credentials.
        /// </summary>
        public static QobuzAuthenticationException MissingCredentials()
        {
            return new QobuzAuthenticationException(
                "No valid authentication method configured. Please provide either username/password or user token.",
                AuthenticationFailureType.MissingCredentials);
        }

        /// <summary>
        /// Creates an exception for mismatched App ID and Secret.
        /// </summary>
        public static QobuzAuthenticationException MismatchedAppCredentials()
        {
            return new QobuzAuthenticationException(
                "App ID and App Secret must be provided as a matching pair. Either provide both or neither.",
                AuthenticationFailureType.MismatchedAppCredentials);
        }

        /// <summary>
        /// Creates an exception for expired session.
        /// </summary>
        public static QobuzAuthenticationException SessionExpired()
        {
            return new QobuzAuthenticationException(
                "Qobuz session has expired. Please re-authenticate.",
                AuthenticationFailureType.SessionExpired);
        }

        /// <summary>
        /// Creates an exception for dynamic credential retrieval failure.
        /// </summary>
        public static QobuzAuthenticationException DynamicCredentialFailure(Exception innerException)
        {
            return new QobuzAuthenticationException(
                "Failed to retrieve dynamic Qobuz credentials. Please provide custom App ID and Secret in settings.",
                AuthenticationFailureType.DynamicCredentialFailure,
                innerException,
                isTemporary: true);
        }
    }

    /// <summary>
    /// Types of authentication failures.
    /// </summary>
    public enum AuthenticationFailureType
    {
        /// <summary>
        /// Username/password or token is incorrect.
        /// </summary>
        InvalidCredentials,

        /// <summary>
        /// No credentials provided.
        /// </summary>
        MissingCredentials,

        /// <summary>
        /// App ID provided without App Secret or vice versa.
        /// </summary>
        MismatchedAppCredentials,

        /// <summary>
        /// Authentication session has expired.
        /// </summary>
        SessionExpired,

        /// <summary>
        /// Failed to retrieve dynamic credentials from Qobuz web player.
        /// </summary>
        DynamicCredentialFailure,

        /// <summary>
        /// Network or other temporary issue during authentication.
        /// </summary>
        TemporaryFailure
    }
}
