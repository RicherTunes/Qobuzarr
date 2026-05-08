using System;
using System.IO;
using System.Text.Json;
using NLog;
using Lidarr.Plugin.Common.Interfaces;
using Lidarr.Plugin.Common.Services.Authentication;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Lidarr.Plugin.Qobuzarr.Authentication
{
    /// <summary>
    /// Idempotent migration from any legacy on-disk session envelopes to the
    /// common library's protected <see cref="FileTokenStore{TSession}"/> format.
    /// </summary>
    /// <remarks>
    /// Historically, <c>SecureCredentialManager</c> + <c>SecureSessionManager</c>
    /// stored sessions in <c>SecureString</c> only - never to disk - and
    /// <c>Authentication.SessionManager</c> used Lidarr's in-memory <c>ICached</c>.
    /// As a result, no plaintext or DPAPI files are expected on user machines.
    ///
    /// However, third-party tooling (<c>tools/SessionMigrator/*</c>) and earlier
    /// experimental builds may have produced plaintext <c>session.json</c> files
    /// at the new path. This migrator reads any unprotected envelope and writes
    /// it back through <see cref="FileTokenStore{TSession}"/>, which transparently
    /// upgrades to the host-encrypted format on save. A sentinel file
    /// (<c>.migrated</c>) prevents repeat work.
    /// </remarks>
    internal static class LegacySessionMigrator
    {
        private const string SentinelFileName = ".migrated";

        /// <summary>
        /// Run the migration if the sentinel is absent. Best-effort: failures are logged
        /// and swallowed so that startup is never blocked by migration issues.
        /// </summary>
        public static void MigrateIfNeeded(string sessionFilePath, Logger logger)
        {
            if (string.IsNullOrWhiteSpace(sessionFilePath)) return;

            try
            {
                var dir = Path.GetDirectoryName(sessionFilePath);
                if (string.IsNullOrWhiteSpace(dir)) return;

                var sentinelPath = Path.Combine(dir, SentinelFileName);
                if (File.Exists(sentinelPath))
                {
                    return; // Already migrated.
                }

                Directory.CreateDirectory(dir);

                // FileTokenStore.LoadAsync transparently upgrades legacy plaintext to
                // protected format, but only when the legacy file already exists.
                if (File.Exists(sessionFilePath))
                {
                    if (TryReadLegacyPlaintext(sessionFilePath, out var session) && session != null)
                    {
                        var store = new FileTokenStore<QobuzSession>(sessionFilePath);
                        // Async-over-sync is acceptable here: this runs once at startup.
                        store.SaveAsync(new TokenEnvelope<QobuzSession>(session, session.ExpiresAt))
                             .GetAwaiter().GetResult();
                        logger?.Info("Migrated legacy plaintext session envelope to protected format at {0}", sessionFilePath);
                    }
                    // If the file isn't a recognizable legacy plaintext envelope, leave it
                    // alone - FileTokenStore handles its own protected v2 format on next load.
                }

                // Drop sentinel to mark migration complete, even if there was nothing to migrate.
                try
                {
                    File.WriteAllText(sentinelPath, DateTime.UtcNow.ToString("O"));
                }
                catch
                {
                    // Sentinel write is best-effort; missing sentinel just means we'll re-run a no-op.
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "Legacy session migration failed (continuing without migrated session)");
            }
        }

        private static bool TryReadLegacyPlaintext(string path, out QobuzSession? session)
        {
            session = null;
            try
            {
                var text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) return false;

                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;

                // The protected envelope uses {"v":2,"alg":...,"payload":...} and is opaque
                // to this migrator. Skip it - FileTokenStore handles its own format.
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("v", out var vProp) &&
                    vProp.ValueKind == JsonValueKind.Number &&
                    vProp.GetInt32() >= 2)
                {
                    return false;
                }

                // Legacy plaintext PersistedEnvelope: {"session":{...},"expiresAt":"..."}
                // or older raw QobuzSession at root.
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("session", out var sessionProp))
                {
                    session = JsonSerializer.Deserialize<QobuzSession>(sessionProp.GetRawText(), s_jsonOptions);
                }
                else
                {
                    session = JsonSerializer.Deserialize<QobuzSession>(text, s_jsonOptions);
                }

                return session != null && !string.IsNullOrEmpty(session.UserId);
            }
            catch
            {
                return false;
            }
        }

        private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.General)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };
    }
}
