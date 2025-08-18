using QobuzCLI.Models;

namespace QobuzCLI.Services;

/// <summary>
/// Service for handling batch download operations from files
/// </summary>
public interface IBatchDownloadService
{
    /// <summary>
    /// Process downloads from a file containing queries or album lists
    /// </summary>
    Task ProcessBatchDownloadAsync(BatchDownloadOptions options, CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for batch download operations
/// </summary>
public class BatchDownloadOptions
{
    public string FilePath { get; set; } = "";
    public bool Immediate { get; set; }
    public string? OutputDirectory { get; set; }
    public string? Quality { get; set; }
    public int Priority { get; set; }
    public string? QueueId { get; set; }
    public string? ReportFormat { get; set; }
    public string? ReportOutput { get; set; }
    public int? Concurrency { get; set; }
}