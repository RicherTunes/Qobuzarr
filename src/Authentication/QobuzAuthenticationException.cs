using System;

namespace Lidarr.Plugin.Qobuzarr.Authentication
{
    public class QobuzAuthenticationException : Exception
    {
        public int? ErrorCode { get; }
        public string ErrorType { get; }

        public QobuzAuthenticationException(string message) : base(message)
        {
            ErrorType = "Unknown";
        }

        public QobuzAuthenticationException(string message, Exception innerException) : base(message, innerException)
        {
            ErrorType = "Unknown";
        }

        public QobuzAuthenticationException(string message, int errorCode, string? errorType = null) : base(message)
        {
            ErrorCode = errorCode;
            ErrorType = errorType ?? "Unknown";
        }

        public static QobuzAuthenticationException InvalidCredentials()
        {
            return new QobuzAuthenticationException("Invalid email or password", 401, "InvalidCredentials");
        }

        public static QobuzAuthenticationException InvalidToken()
        {
            return new QobuzAuthenticationException("Invalid user ID or auth token", 401, "InvalidToken");
        }

        public static QobuzAuthenticationException SessionExpired()
        {
            return new QobuzAuthenticationException("Session has expired", 401, "SessionExpired");
        }

        public static QobuzAuthenticationException AppCredentialsInvalid()
        {
            return new QobuzAuthenticationException("Invalid app ID or secret", 403, "InvalidAppCredentials");
        }

        public static QobuzAuthenticationException RateLimited()
        {
            return new QobuzAuthenticationException("Too many authentication attempts", 429, "RateLimited");
        }
    }
}