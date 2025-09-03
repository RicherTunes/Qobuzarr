using System.Collections.Generic;
using Lidarr.Plugin.Common.Services.Http;

namespace Lidarr.Plugin.Qobuzarr.API.Signing
{
    /// <summary>
    /// Adapter to bridge Qobuz-specific signer to the common IRequestSigner abstraction.
    /// </summary>
    public class QobuzRequestSignerAdapter : IRequestSigner
    {
        private readonly IQobuzRequestSigner _inner;

        public QobuzRequestSignerAdapter(IQobuzRequestSigner inner)
        {
            _inner = inner;
        }

        public bool RequiresSigning(string endpoint) => _inner.RequiresSigning(endpoint);

        public void Sign(string endpoint, IDictionary<string, string> parameters, string appId, string appSecret)
        {
            // Adapt IDictionary to Dictionary
            if (parameters is Dictionary<string, string> dict)
            {
                _inner.SignRequest(endpoint, dict, appId, appSecret);
            }
            else
            {
                var copy = new Dictionary<string, string>(parameters);
                _inner.SignRequest(endpoint, copy, appId, appSecret);
                foreach (var kv in copy)
                {
                    parameters[kv.Key] = kv.Value;
                }
            }
        }
    }
}

