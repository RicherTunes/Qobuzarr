using System.Threading.Tasks;
using QobuzCLI.Models;

namespace QobuzCLI.Services
{
    /// <summary>
    /// Service for intelligent duplicate detection with quality comparison and partial download detection
    /// </summary>
    public interface ISmartDuplicateChecker
    {
        /// <summary>
        /// Checks if a track has already been downloaded and determines if it should be replaced
        /// </summary>
        Task<DuplicateCheckResult> CheckForDuplicateAsync(SearchResult track, string proposedPath);

        /// <summary>
        /// Determines if an existing file should be replaced with a new version
        /// </summary>
        Task<bool> ShouldReplaceFileAsync(QobuzMetadata existingMetadata, SearchResult newTrack);

        /// <summary>
        /// Validates file integrity using size and optional header checks
        /// </summary>
        Task<FileValidationResult> ValidateFileIntegrityAsync(string filePath, long? expectedSize = null);

        /// <summary>
        /// Records a successful download in the metadata system
        /// </summary>
        Task RecordDownloadAsync(string filePath, SearchResult track, long fileSize);
    }

    /// <summary>
    /// Result of duplicate checking operation
    /// </summary>
    public class DuplicateCheckResult
    {
        public bool IsDuplicate { get; set; }
        public bool ShouldReplace { get; set; }
        public bool IsCorrupted { get; set; }
        public bool IsPartial { get; set; }
        public string ExistingFilePath { get; set; } = "";
        public QualityComparison QualityComparison { get; set; }
        public string Reason { get; set; } = "";

        // Helper properties
        public bool ShouldDownload => !IsDuplicate || ShouldReplace;
        public bool IsComplete => IsDuplicate && !IsPartial && !IsCorrupted && !ShouldReplace;
    }

    /// <summary>
    /// Comparison between existing and new quality
    /// </summary>
    public class QualityComparison
    {
        public QualityInfo ExistingQuality { get; set; }
        public QualityInfo NewQuality { get; set; }
        public int QualityDifference { get; set; }
        public double PercentImprovement { get; set; }
        public string UpgradeReason { get; set; }

        public bool IsSignificantUpgrade(double minImprovementPercent = 20)
        {
            return PercentImprovement >= minImprovementPercent;
        }
    }

    /// <summary>
    /// Result of file validation
    /// </summary>
    public class FileValidationResult
    {
        public bool IsValid { get; set; }
        public bool FileExists { get; set; }
        public bool SizeValid { get; set; }
        public bool HeaderValid { get; set; }
        public string Reason { get; set; }
        public long ActualSize { get; set; }
        public long? ExpectedSize { get; set; }
    }
}