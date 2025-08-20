using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Core;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Service responsible for generating and retrieving stream URLs from Qobuz API
    /// </summary>
    public class QobuzStreamUrlService
    {
        private readonly IQobuzHttpClient _httpClient;
        private readonly IQobuzLogger _logger;
        private readonly QobuzAuthService _authService;
        private const string API_BASE = "https://www.qobuz.com/api.json/0.2";

        public QobuzStreamUrlService(
            IQobuzHttpClient httpClient,
            IQobuzLogger logger,
            QobuzAuthService authService)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        }

        /// <summary>
        /// Gets stream information for a track with specified quality
        /// </summary>
        public async Task<QobuzStreamInfo?> GetStreamInfoAsync(string trackId, int formatId)
        {
            var session = _authService.GetCurrentSession();
            if (session == null)
            {
                throw new InvalidOperationException("Not authenticated");
            }

            var requestTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var signatureString = GenerateFileUrlSignature(formatId.ToString(), trackId, requestTimestamp.ToString(), session.AppSecret);
            var signature = ComputeMD5Hash(signatureString);
            
            _logger.Debug("Getting stream URL for track {0} with format {1}", trackId, formatId);
            
            var url = $"{API_BASE}/track/getFileUrl?track_id={trackId}&format_id={formatId}" +
                     $"&intent=stream&app_id={session.AppId}&user_auth_token={session.AuthToken}" +
                     $"&request_ts={requestTimestamp}&request_sig={signature}";

            try
            {
                var response = await _httpClient.GetJsonAsync<QobuzStreamResponse>(url);
                return new QobuzStreamInfo
                {
                    Url = response.Url,
                    FormatId = response.FormatId,
                    MimeType = response.MimeType
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get stream URL for track {0}", trackId);
                return null;
            }
        }

        /// <summary>
        /// Generate signature string for track/getFileUrl using TrevTV's exact format
        /// </summary>
        private string GenerateFileUrlSignature(string formatId, string trackId, string timestamp, string appSecret)
        {
            return $"trackgetFileUrlformat_id{formatId}intentstreamtrack_id{trackId}{timestamp}{appSecret}";
        }

        /// <summary>
        /// Compute MD5 hash as required by Qobuz API
        /// </summary>
        private string ComputeMD5Hash(string input)
        {
            using (var md5 = MD5.Create())
            {
                var inputBytes = Encoding.UTF8.GetBytes(input);
                var hashBytes = md5.ComputeHash(inputBytes);
                
                var sb = new StringBuilder();
                foreach (var b in hashBytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                
                return sb.ToString();
            }
        }
    }
}