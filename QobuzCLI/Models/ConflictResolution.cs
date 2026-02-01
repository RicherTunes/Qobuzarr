using System.Text.Json.Serialization;

namespace QobuzCLI.Models;

public class ConflictSession
{
    public List<SearchConflict> Conflicts { get; set; } = new();
    public List<ConflictResolution> Resolutions { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsComplete => Conflicts.Count == Resolutions.Count;
}

public class SearchConflict
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Query { get; set; } = string.Empty;
    public SearchType DetectedType { get; set; }
    public List<SearchResult> CandidateResults { get; set; } = new();
    public ConflictReason Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ConflictResolution
{
    public string ConflictId { get; set; } = string.Empty;
    public string? SelectedResultId { get; set; }
    public ResolutionAction Action { get; set; }
    public string? UserNote { get; set; }
    public DateTime ResolvedAt { get; set; } = DateTime.UtcNow;
}

public enum ConflictReason
{
    MultipleExactMatches,
    AmbiguousSearchType,
    NoHighConfidenceMatch,
    QualityConflict,
    ArtistDisambiguation
}

public enum ResolutionAction
{
    SelectResult,
    SkipQuery,
    RefineSearch,
    DownloadAll
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SearchType
{
    Auto,
    Album,
    Artist,
    Track,
    Playlist,
    Label
}

public class SearchResult
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string Quality { get; set; } = string.Empty;
    public int TrackCount { get; set; }
    public double Score { get; set; }
    public string? Label { get; set; }
    public string? Country { get; set; }
    public long? Duration { get; set; }

    // Quality information for duplicate detection
    public string? AlbumId { get; set; }
    public string? Format { get; set; }
    public int? BitDepth { get; set; }
    public int? SampleRate { get; set; }
    public int? Bitrate { get; set; }
    public int? TrackNumber { get; set; }
}

public class DownloadResult
{
    public bool Success { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public int TracksDownloaded { get; set; }
    public string? ErrorMessage { get; set; }
}
