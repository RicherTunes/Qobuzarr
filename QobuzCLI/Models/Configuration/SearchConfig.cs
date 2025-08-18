using Newtonsoft.Json;

namespace QobuzCLI.Models.Configuration
{
    /// <summary>
    /// Configuration settings for search behavior and result filtering.
    /// </summary>
    public class SearchConfig
    {
        [JsonProperty("searchResultLimit")]
        public int SearchResultLimit { get; set; } = 20;
        
        [JsonProperty("autoResolveExactMatches")]
        public bool AutoResolveExactMatches { get; set; } = true;
        
        [JsonProperty("searchPreference")]
        public string SearchPreference { get; set; } = "smart"; // smart, albums, tracks

        /// <summary>
        /// Validates and clamps search result limit to reasonable bounds
        /// </summary>
        public void ValidateSearchLimit()
        {
            SearchResultLimit = Math.Max(1, Math.Min(100, SearchResultLimit));
        }

        /// <summary>
        /// Checks if search preference is valid
        /// </summary>
        public bool IsValidSearchPreference()
        {
            var validPreferences = new[] { "smart", "albums", "tracks" };
            return validPreferences.Contains(SearchPreference.ToLower());
        }
    }
}