namespace Lidarr.Plugin.Qobuzarr.Models
{
    /// <summary>
    /// Result of quality selection with fallback logic.
    /// </summary>
    public class QualitySelectionResult
    {
        public bool Success { get; set; }
        public QobuzQuality SelectedQuality { get; set; }
        public StreamInfo StreamInfo { get; set; }
        public bool FallbackUsed { get; set; }
        public int AttemptsCount { get; set; }
        public string Error { get; set; }
        
        /// <summary>
        /// Gets a description of the selection result.
        /// </summary>
        public string GetDescription()
        {
            if (Success)
            {
                var fallbackMsg = FallbackUsed ? " (fallback)" : "";
                return $"{SelectedQuality?.Name}{fallbackMsg} after {AttemptsCount} attempts";
            }
            else
            {
                return $"Failed after {AttemptsCount} attempts: {Error}";
            }
        }
        
        /// <summary>
        /// Creates a successful result.
        /// </summary>
        public static QualitySelectionResult Successful(QobuzQuality quality, StreamInfo streamInfo, bool fallbackUsed, int attempts)
        {
            return new QualitySelectionResult
            {
                Success = true,
                SelectedQuality = quality,
                StreamInfo = streamInfo,
                FallbackUsed = fallbackUsed,
                AttemptsCount = attempts
            };
        }
        
        /// <summary>
        /// Creates a failed result.
        /// </summary>
        public static QualitySelectionResult Failed(string error, int attempts)
        {
            return new QualitySelectionResult
            {
                Success = false,
                Error = error,
                AttemptsCount = attempts
            };
        }
        
        public override string ToString()
        {
            return GetDescription();
        }
    }
}