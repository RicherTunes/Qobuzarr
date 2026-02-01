using QobuzCLI.Models;

namespace QobuzCLI.Services;

public interface ISearchService
{
    SearchType DetectSearchType(string query);
    List<SearchResult> ScoreResults(List<SearchResult> results, string query);
    bool IsExactMatch(SearchResult result, string query);
    double CalculateRelevanceScore(SearchResult result, string query);

    /// <summary>
    /// Score results with pagination support for large result sets
    /// </summary>
    IEnumerable<SearchResult> ScoreResultsPaged(IEnumerable<SearchResult> results, string query, int pageSize = 100);
}
