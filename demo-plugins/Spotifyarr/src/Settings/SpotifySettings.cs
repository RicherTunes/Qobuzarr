using System.ComponentModel;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Indexers;
using Lidarr.Plugin.Common.Base;

namespace Lidarr.Plugin.Spotifyarr.Settings
{
    /// <summary>
    /// Settings for Spotifyarr using shared library patterns.
    /// Only ~50 lines needed vs 200+ traditional implementation!
    /// </summary>
    public class SpotifySettings : BaseStreamingSettings, IIndexerSettings
    {
        public SpotifySettings()
        {
            BaseUrl = "https://api.spotify.com/v1";
            SearchLimit = 100;
            CountryCode = "US";
            // BaseStreamingSettings provides: Email, Password, ApiRateLimit, etc.
        }

        [FieldDefinition(50, Label = "Spotify API Key", Type = FieldType.Password)]
        public string SpotifyApiKey { get; set; }
        
        [FieldDefinition(51, Label = "Country Market")]  
        public string SpotifyMarket { get; set; } = "US";

        public override bool IsValid(out string errorMessage)
        {
            if (!base.IsValid(out errorMessage))
                return false;
                
            if (string.IsNullOrEmpty(SpotifyApiKey))
            {
                errorMessage = "Spotify API Key is required";
                return false;
            }
            
            return true;
        }
    }
}
