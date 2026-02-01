using QobuzCLI.Models;

namespace QobuzCLI.Services;

/// <summary>
/// Service for handling interactive user selection and UI display logic.
/// Abstracts the complex UI logic from command classes for better testability and separation of concerns.
/// </summary>
public interface IInteractiveSelectionService
{
    /// <summary>
    /// Handles the selection of download targets from search results.
    /// Includes automatic selection logic for exact matches and single results.
    /// </summary>
    /// <param name="results">Search results to select from</param>
    /// <param name="query">Original search query for context</param>
    /// <param name="forceSelect">If true, always show selection UI even for exact matches</param>
    /// <param name="downloadAll">If true, select all results without prompting</param>
    /// <returns>List of selected search results</returns>
    Task<List<SearchResult>> SelectDownloadTargetsAsync(
        List<SearchResult> results,
        string query,
        bool forceSelect,
        bool downloadAll);

    /// <summary>
    /// Shows the interactive selection UI for choosing from multiple search results.
    /// Displays a formatted table and selection prompt.
    /// </summary>
    /// <param name="results">Search results to display</param>
    /// <param name="query">Original search query for context</param>
    /// <param name="exactMatches">List of exact matches for highlighting</param>
    /// <returns>List of selected search results</returns>
    Task<List<SearchResult>> ShowSelectionUIAsync(
        List<SearchResult> results,
        string query,
        List<SearchResult> exactMatches);

    /// <summary>
    /// Formats search result details for display in the selection table.
    /// </summary>
    /// <param name="result">Search result to format</param>
    /// <returns>Formatted details string</returns>
    string FormatDetails(SearchResult result);

    /// <summary>
    /// Formats quality information for display in the selection table.
    /// </summary>
    /// <param name="result">Search result to format</param>
    /// <returns>Formatted quality string</returns>
    string FormatQuality(SearchResult result);

    /// <summary>
    /// Formats match score for display in the selection table.
    /// </summary>
    /// <param name="result">Search result to format</param>
    /// <returns>Formatted match score string</returns>
    string FormatMatchScore(SearchResult result);
}
