using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Abstractions.Contracts;
using Lidarr.Plugin.Abstractions.Manifest;
using Lidarr.Plugin.Common.Hosting;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net;
using Lidarr.Plugin.Qobuzarr.Configuration;

namespace Lidarr.Plugin.Qobuzarr.Plugin
{
    // Proper entrypoint built on Lidarr.Plugin.Common StreamingPlugin base
    public sealed class QobuzPlugin : StreamingPlugin<QobuzModule, QobuzSettings>
    {
        protected override IEnumerable<SettingDefinition> DescribeSettings()
        {
            return new[]
            {
                new SettingDefinition { Key = nameof(QobuzSettings.DownloadPath), DisplayName = "Download Path", Description = "Root folder where album folders are created", DataType = SettingDataType.String, IsRequired = true },
                new SettingDefinition { Key = nameof(QobuzSettings.CreateAlbumFolders), DisplayName = "Create Album Folders", Description = "Organize downloads into Artist/Album", DataType = SettingDataType.Boolean, DefaultValue = true },
                new SettingDefinition { Key = nameof(QobuzSettings.PreferredQuality), DisplayName = "Quality", Description = "5=MP3 320, 6=FLAC CD, 7=FLAC 96k", DataType = SettingDataType.Integer, DefaultValue = 6 },
                new SettingDefinition { Key = nameof(QobuzSettings.MinimumSuccessRatePercent), DisplayName = "Minimum Success %", Description = "Album considered successful above this threshold", DataType = SettingDataType.Integer, DefaultValue = 80 },
                new SettingDefinition { Key = nameof(QobuzSettings.SkipPreviewTracks), DisplayName = "Skip Previews", Description = "Skip 30-second preview tracks", DataType = SettingDataType.Boolean, DefaultValue = true },
                new SettingDefinition { Key = nameof(QobuzSettings.UserId), DisplayName = "Qobuz User ID", Description = "Your Qobuz numeric user ID (token auth)", DataType = SettingDataType.String, IsRequired = true },
                new SettingDefinition { Key = nameof(QobuzSettings.AuthToken), DisplayName = "Qobuz Auth Token", Description = "Your Qobuz user_auth_token", DataType = SettingDataType.Password, IsRequired = true },
                new SettingDefinition { Key = nameof(QobuzSettings.AppId), DisplayName = "App ID (optional)", Description = "Override Qobuz app_id; leave empty to use default", DataType = SettingDataType.String, DefaultValue = null },
                new SettingDefinition { Key = nameof(QobuzSettings.CountryCode), DisplayName = "Country Code", Description = "Market to use for catalog (e.g. US, GB, FR)", DataType = SettingDataType.String, DefaultValue = "US" },
                new SettingDefinition { Key = nameof(QobuzSettings.Locale), DisplayName = "Locale (optional)", Description = "Locale for localized fields, e.g. en_US", DataType = SettingDataType.String, DefaultValue = null }
            };
        }

        protected override void ConfigureDefaults(QobuzSettings settings)
        {
            // Defaults already set on the type; nothing else to do here
        }

        protected override PluginValidationResult ValidateSettings(QobuzSettings s)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(s.DownloadPath)) errors.Add("DownloadPath is required");
            if (string.IsNullOrWhiteSpace(s.UserId)) errors.Add("UserId is required");
            if (string.IsNullOrWhiteSpace(s.AuthToken)) errors.Add("AuthToken is required");
            if (errors.Count > 0) return PluginValidationResult.Failure(errors);

            // Tiny online token check with a short timeout (best-effort, non-blocking UX)
            var warnings = new List<string>();
            try
            {
                var appId = string.IsNullOrWhiteSpace(s.AppId) ? QobuzConstants.Api.DefaultAppId : s.AppId;
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
                var handler = new SocketsHttpHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(3)
                };
                using var http = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(2)
                };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Qobuzarr/1.0 (+https://github.com/richertunes/qobuzarr)");
                var locale = string.IsNullOrWhiteSpace(s.Locale) ? string.Empty : $"&locale={Uri.EscapeDataString(s.Locale)}";
                var url = $"{QobuzConstants.Api.BaseUrl}/album/search?query=a&limit=1&app_id={appId}&user_auth_token={s.AuthToken}&user_id={s.UserId}&country_code={s.CountryCode}{locale}";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                var resp = http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).GetAwaiter().GetResult();

                if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
                {
                    return PluginValidationResult.Failure(new[] { "Qobuz authentication failed: check UserId/AuthToken (and optional AppId)." });
                }

                if (!resp.IsSuccessStatusCode)
                {
                    warnings.Add($"Credential probe returned {((int)resp.StatusCode)} {resp.StatusCode}; saved settings anyway.");
                }
            }
            catch (TaskCanceledException)
            {
                warnings.Add("Credential probe timed out; saved settings without verification.");
            }
            catch (HttpRequestException ex)
            {
                warnings.Add($"Network error during credential probe: {ex.Message}");
            }
            catch (Exception ex)
            {
                warnings.Add($"Credential probe error: {ex.Message}");
            }

            return PluginValidationResult.Success(warnings.ToArray());
        }

        protected override ValueTask<IIndexer?> CreateIndexerAsync(QobuzSettings s, IServiceProvider services, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IIndexer?>(new QobuzIndexerAdapter(s));
        }

        protected override ValueTask<IDownloadClient?> CreateDownloadClientAsync(QobuzSettings s, IServiceProvider services, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IDownloadClient?>(new QobuzDownloadClientAdapter(s));
        }
    }
}
