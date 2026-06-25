using System;
using System.Security.Cryptography;
using System.Text;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Authentication
{
    internal static class QobuzCredentialFactory
    {
        public static QobuzCredentials FromIndexerSettings(QobuzIndexerSettings settings)
        {
            return TryFromIndexerSettings(settings)
                ?? throw new InvalidOperationException(
                    "No valid authentication method configured. " +
                    "Either provide Email/Password or UserId/AuthToken.");
        }

        public static QobuzCredentials? TryFromIndexerSettings(QobuzIndexerSettings? settings)
        {
            if (settings == null)
            {
                return null;
            }

            var credentials = settings.AuthMethod switch
            {
                (int)AuthenticationMethod.Email => TryCreateEmailCredentials(settings),
                (int)AuthenticationMethod.Token => TryCreateTokenCredentials(settings),
                _ => null
            };

            if (credentials == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(settings.AppId) && !string.IsNullOrWhiteSpace(settings.AppSecret))
            {
                credentials.AppId = settings.AppId.Trim();
                credentials.AppSecret = settings.AppSecret.Trim();
            }

            return credentials;
        }

        public static bool SessionMatchesCredentials(QobuzSession? session, QobuzCredentials? credentials)
        {
            if (session == null || credentials == null || !credentials.IsValid())
            {
                return false;
            }

            if (!credentials.IsTokenAuth())
            {
                return false;
            }

            if (!string.Equals(session.UserId, credentials.UserId, StringComparison.Ordinal) ||
                !string.Equals(session.AuthToken, credentials.AuthToken, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(credentials.AppId) &&
                !string.Equals(session.AppId, credentials.AppId, StringComparison.Ordinal))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(credentials.AppSecret) ||
                   string.Equals(session.AppSecret, credentials.AppSecret, StringComparison.Ordinal);
        }

        public static string? CreateCredentialFingerprint(QobuzCredentials? credentials)
        {
            if (credentials == null || !credentials.IsValid())
            {
                return null;
            }

            var material = credentials.IsEmailAuth()
                ? string.Join("\0",
                    "email",
                    Normalize(credentials.Email).ToLowerInvariant(),
                    Normalize(credentials.MD5Password).ToLowerInvariant(),
                    Normalize(credentials.AppId),
                    Normalize(credentials.AppSecret))
                : string.Join("\0",
                    "token",
                    Normalize(credentials.UserId),
                    Normalize(credentials.AuthToken),
                    Normalize(credentials.AppId),
                    Normalize(credentials.AppSecret));

            return Hash(material);
        }

        public static string? CreateSessionFingerprint(QobuzSession? session)
        {
            if (session == null || !session.IsValid())
            {
                return null;
            }

            return Hash(string.Join("\0",
                "session",
                Normalize(session.UserId),
                Normalize(session.AuthToken),
                Normalize(session.AppId),
                Normalize(session.AppSecret)));
        }

        public static QobuzCredentials? Clone(QobuzCredentials? credentials)
        {
            if (credentials == null)
            {
                return null;
            }

            return new QobuzCredentials
            {
                Email = credentials.Email,
                MD5Password = credentials.MD5Password,
                UserId = credentials.UserId,
                AuthToken = credentials.AuthToken,
                AppId = credentials.AppId,
                AppSecret = credentials.AppSecret
            };
        }

        private static QobuzCredentials? TryCreateEmailCredentials(QobuzIndexerSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.Email) || string.IsNullOrWhiteSpace(settings.Password))
            {
                return null;
            }

            var password = settings.Password.Trim();
            return new QobuzCredentials
            {
                Email = settings.Email.Trim(),
                MD5Password = IsMd5Hash(password)
                    ? password
                    : HashingUtility.ComputePasswordMD5Hash(password)
            };
        }

        private static QobuzCredentials? TryCreateTokenCredentials(QobuzIndexerSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.UserId) || string.IsNullOrWhiteSpace(settings.AuthToken))
            {
                return null;
            }

            return new QobuzCredentials
            {
                UserId = settings.UserId.Trim(),
                AuthToken = settings.AuthToken.Trim()
            };
        }

        private static string Hash(string material)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
            return Convert.ToHexString(bytes);
        }

        private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

        private static bool IsMd5Hash(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 32)
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                var isHex = (c >= '0' && c <= '9') ||
                            (c >= 'a' && c <= 'f') ||
                            (c >= 'A' && c <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
