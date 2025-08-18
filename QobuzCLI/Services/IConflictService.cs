using QobuzCLI.Models;

namespace QobuzCLI.Services;

public interface IConflictService
{
    /// <summary>
    /// Analyzes search results to identify potential conflicts requiring user resolution
    /// </summary>
    SearchConflict? IdentifyConflict(string query, List<SearchResult> results, SearchType detectedType);
    
    /// <summary>
    /// Creates a new conflict resolution session
    /// </summary>
    ConflictSession CreateSession();
    
    /// <summary>
    /// Adds a conflict to the current session
    /// </summary>
    void AddConflict(ConflictSession session, SearchConflict conflict);
    
    /// <summary>
    /// Runs an interactive resolution session for all collected conflicts
    /// </summary>
    Task<ConflictSession> ResolveConflictsAsync(ConflictSession session);
    
    /// <summary>
    /// Applies resolved conflicts to get final search results
    /// </summary>
    List<SearchResult> ApplyResolutions(ConflictSession session);
    
    /// <summary>
    /// Saves conflict session for analysis and future improvements
    /// </summary>
    Task SaveSessionAsync(ConflictSession session);
    
    /// <summary>
    /// Loads previous sessions for pattern analysis
    /// </summary>
    Task<List<ConflictSession>> LoadRecentSessionsAsync(int count = 10);
}