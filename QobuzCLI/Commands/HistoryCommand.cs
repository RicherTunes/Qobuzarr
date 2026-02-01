using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QobuzCLI.Models;
using QobuzCLI.Services;
using Spectre.Console;

namespace QobuzCLI.Commands;

public class HistoryCommand
{
    private readonly IStateService _stateService;
    private readonly ILogger<HistoryCommand> _logger;

    public HistoryCommand(IStateService stateService, ILogger<HistoryCommand> logger)
    {
        _stateService = stateService;
        _logger = logger;
    }

    public Command CreateCommand()
    {
        var historyCommand = new Command("history", "View and manage download history");

        // Main history command with options
        var limitOption = new Option<int>("--limit", () => 20, "Number of entries to show");
        var statusOption = new Option<string?>("--status", "Filter by status (completed, failed, cancelled)");
        var searchOption = new Option<string?>("--search", "Search in album/artist names");
        var exportOption = new Option<string?>("--export", "Export history to file (json, csv, html)");
        var cleanupOption = new Option<bool>("--cleanup", "Remove old history entries");
        var keepDaysOption = new Option<int>("--keep-days", () => 30, "Days to keep when cleaning up");
        var statsOption = new Option<bool>("--stats", "Show detailed statistics");

        historyCommand.AddOption(limitOption);
        historyCommand.AddOption(statusOption);
        historyCommand.AddOption(searchOption);
        historyCommand.AddOption(exportOption);
        historyCommand.AddOption(cleanupOption);
        historyCommand.AddOption(keepDaysOption);
        historyCommand.AddOption(statsOption);

        historyCommand.SetHandler(async (int limit, string? status, string? search, string? export,
            bool cleanup, int keepDays, bool stats) =>
            await HandleHistoryAsync(limit, status, search, export, cleanup, keepDays, stats),
            limitOption, statusOption, searchOption, exportOption, cleanupOption, keepDaysOption, statsOption);

        // Subcommands
        historyCommand.AddCommand(CreateStatsCommand());
        historyCommand.AddCommand(CreateExportCommand());
        historyCommand.AddCommand(CreateCleanupCommand());
        historyCommand.AddCommand(CreateSearchCommand());

        return historyCommand;
    }

    private async Task HandleHistoryAsync(int limit, string? status, string? search, string? export,
        bool cleanup, int keepDays, bool stats)
    {
        if (cleanup)
        {
            await HandleCleanupAsync(keepDays);
            return;
        }

        if (stats)
        {
            await HandleStatsAsync();
            return;
        }

        if (!string.IsNullOrEmpty(export))
        {
            await HandleExportAsync(export, limit, status, search);
            return;
        }

        // Default: show history
        await ShowHistoryAsync(limit, status, search);
    }

    private async Task ShowHistoryAsync(int limit, string? statusFilter, string? searchQuery)
    {
        await Task.Yield();
        try
        {
            var history = _stateService.GetDownloadHistory(limit * 2); // Get more for filtering

            // Apply filters
            if (!string.IsNullOrEmpty(statusFilter))
            {
                var targetStatus = Enum.Parse<DownloadStatus>(statusFilter, true);
                history = history.Where(h => h.FinalStatus == targetStatus).ToList();
            }

            if (!string.IsNullOrEmpty(searchQuery))
            {
                var query = searchQuery.ToLowerInvariant();
                history = history.Where(h =>
                    h.Title.ToLowerInvariant().Contains(query) ||
                    h.Artist.ToLowerInvariant().Contains(query)).ToList();
            }

            // Take final limit after filtering
            history = history.Take(limit).ToList();

            if (!history.Any())
            {
                AnsiConsole.MarkupLine("[yellow]No download history found[/]");
                return;
            }

            // Create beautiful table
            var table = new Table()
                .Title($"[bold cyan]Download History[/] [grey]({history.Count} entries)[/]")
                .AddColumn(new TableColumn("[bold]Started[/]").Centered())
                .AddColumn(new TableColumn("[bold]Album[/]").NoWrap())
                .AddColumn(new TableColumn("[bold]Artist[/]").NoWrap())
                .AddColumn(new TableColumn("[bold]Status[/]").Centered())
                .AddColumn(new TableColumn("[bold]Tracks[/]").Centered())
                .AddColumn(new TableColumn("[bold]Duration[/]").Centered())
                .AddColumn(new TableColumn("[bold]Quality[/]").Centered());

            table.Border = TableBorder.Rounded;

            foreach (var item in history)
            {
                var statusMarkup = GetStatusMarkup(item.FinalStatus);
                var duration = item.CompletedAt.HasValue
                    ? (item.CompletedAt.Value - item.DownloadedAt).ToString(@"mm\:ss")
                    : "-";

                table.AddRow(
                    item.DownloadedAt.ToString("MMM d HH:mm"),
                    $"[cyan]{item.Title.EscapeMarkup()}[/]",
                    $"[yellow]{item.Artist.EscapeMarkup()}[/]",
                    statusMarkup,
                    item.TracksDownloaded.ToString(),
                    duration,
                    item.Quality ?? "-"
                );
            }

            AnsiConsole.Write(table);

            // Show summary
            var stats = _stateService.GetStatistics();
            var successRate = stats.TotalDownloads > 0 ? (double)stats.CompletedDownloads / stats.TotalDownloads : 0;
            var summary = new Panel($"[bold]Summary Statistics[/]\n" +
                $"Total Downloads: [cyan]{stats.TotalDownloads}[/]\n" +
                $"Success Rate: [green]{successRate:P1}[/]\n" +
                $"Data Downloaded: [yellow]{FormatBytes(stats.TotalBytes)}[/]")
                .Header("[bold green]📊 Quick Stats[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Green);

            AnsiConsole.Write(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error displaying history");
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message.EscapeMarkup()}[/]");
        }
    }

    private Command CreateStatsCommand()
    {
        var cmd = new Command("stats", "Show detailed download statistics");
        cmd.SetHandler(async () => await HandleStatsAsync());
        return cmd;
    }

    private async Task HandleStatsAsync()
    {
        await Task.Yield();
        try
        {
            var stats = _stateService.GetStatistics();
            var history = _stateService.GetDownloadHistory(1000); // Get more for analysis

            // Create comprehensive stats display
            var layout = new Layout("Root")
                .SplitColumns(
                    new Layout("Left"),
                    new Layout("Right"));

            // Left panel - Overall stats
            var successRate = stats.TotalDownloads > 0 ? (double)stats.CompletedDownloads / stats.TotalDownloads : 0;
            var overallStats = new Panel($"[bold]Overall Statistics[/]\n\n" +
                $"📈 [bold cyan]Downloads[/]\n" +
                $"Total: [white]{stats.TotalDownloads:N0}[/]\n" +
                $"Completed: [green]{stats.CompletedDownloads:N0}[/]\n" +
                $"Failed: [red]{stats.FailedDownloads:N0}[/]\n" +
                $"Success Rate: [green]{successRate:P1}[/]\n\n" +
                $"💾 [bold cyan]Data Transfer[/]\n" +
                $"Total Downloaded: [yellow]{FormatBytes(stats.TotalBytes)}[/]\n" +
                $"Average per Album: [yellow]{(stats.CompletedDownloads > 0 ? FormatBytes(stats.TotalBytes / stats.CompletedDownloads) : "0 B")}[/]\n\n" +
                $"⏱️ [bold cyan]Performance[/]\n" +
                $"Active Downloads: [blue]{stats.ActiveDownloads:N0}[/]")
                .Header("[bold green]📊 Overall Statistics[/]")
                .Border(BoxBorder.Rounded);

            // Right panel - Recent activity & trends
            var recentHistory = history.Take(10).ToList();
            var qualityBreakdown = history
                .Where(h => !string.IsNullOrEmpty(h.Quality))
                .GroupBy(h => h.Quality)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .ToList();

            var recentActivityText = string.Join("\n", recentHistory.Select(h =>
                $"[grey]{h.DownloadedAt:MMM d HH:mm}[/] {GetStatusMarkup(h.FinalStatus)} {h.Artist.EscapeMarkup()} - {h.Title.EscapeMarkup()}"));
            var qualityText = string.Join("\n", qualityBreakdown.Select(q =>
                $"[cyan]{q.Key}[/]: [white]{q.Count():N0}[/] downloads"));

            var recentActivity = new Panel($"[bold]Recent Activity (Last 10)[/]\n\n" +
                $"{recentActivityText}\n\n" +
                $"[bold]Quality Preferences[/]\n\n" +
                $"{qualityText}")
                .Header("[bold blue]📈 Activity & Trends[/]")
                .Border(BoxBorder.Rounded);

            layout["Left"].Update(overallStats);
            layout["Right"].Update(recentActivity);

            AnsiConsole.Write(layout);

            // Show failure analysis if there are failures
            if (stats.FailedDownloads > 0)
            {
                var failedItems = history.Where(h => h.FinalStatus == DownloadStatus.Failed).Take(5);
                if (failedItems.Any())
                {
                    AnsiConsole.Write(new Rule("[red]Recent Failures[/]") { Justification = Justify.Left });

                    foreach (var failure in failedItems)
                    {
                        AnsiConsole.MarkupLine($"[red]❌[/] [grey]{failure.DownloadedAt:MMM d HH:mm}[/] {failure.Artist.EscapeMarkup()} - {failure.Title.EscapeMarkup()}");
                        if (!string.IsNullOrEmpty(failure.ErrorMessage))
                        {
                            AnsiConsole.MarkupLine($"    [dim]{failure.ErrorMessage.EscapeMarkup()}[/]");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing statistics");
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message.EscapeMarkup()}[/]");
        }
    }

    private Command CreateExportCommand()
    {
        var cmd = new Command("export", "Export download history to file");
        var fileOption = new Option<string>("--file", "Output file path") { IsRequired = true };
        var formatOption = new Option<string>("--format", () => "json", "Export format (json, csv, html)");
        var limitOption = new Option<int>("--limit", () => 100, "Number of entries to export");

        cmd.AddOption(fileOption);
        cmd.AddOption(formatOption);
        cmd.AddOption(limitOption);

        cmd.SetHandler(async (string file, string format, int limit) =>
            await HandleExportAsync(file, limit, null, null, format), fileOption, formatOption, limitOption);

        return cmd;
    }

    private async Task HandleExportAsync(string filePath, int limit, string? statusFilter, string? searchQuery, string format = "json")
    {
        try
        {
            // Determine format from file extension if not specified
            if (format == "json" && filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                format = "csv";
            else if (format == "json" && filePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                format = "html";

            // Get and filter history
            var history = _stateService.GetDownloadHistory(limit * 2);

            if (!string.IsNullOrEmpty(statusFilter))
            {
                var targetStatus = Enum.Parse<DownloadStatus>(statusFilter, true);
                history = history.Where(h => h.FinalStatus == targetStatus).ToList();
            }

            if (!string.IsNullOrEmpty(searchQuery))
            {
                var query = searchQuery.ToLowerInvariant();
                history = history.Where(h =>
                    h.Title.ToLowerInvariant().Contains(query) ||
                    h.Artist.ToLowerInvariant().Contains(query)).ToList();
            }

            history = history.Take(limit).ToList();

            // Export in requested format
            switch (format.ToLowerInvariant())
            {
                case "json":
                    await ExportJsonAsync(filePath, history);
                    break;

                case "csv":
                    await ExportCsvAsync(filePath, history);
                    break;

                case "html":
                    await ExportHtmlAsync(filePath, history);
                    break;

                default:
                    throw new ArgumentException($"Unsupported export format: {format}");
            }

            AnsiConsole.MarkupLine($"[green]✅ Exported {history.Count} entries to {filePath}[/]");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting history");
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message.EscapeMarkup()}[/]");
        }
    }

    private Command CreateCleanupCommand()
    {
        var cmd = new Command("cleanup", "Remove old download history entries");
        var keepDaysOption = new Option<int>("--keep-days", () => 30, "Days to keep");
        var confirmOption = new Option<bool>("--yes", "Skip confirmation prompt");

        cmd.AddOption(keepDaysOption);
        cmd.AddOption(confirmOption);

        cmd.SetHandler(async (int keepDays, bool confirm) =>
            await HandleCleanupAsync(keepDays, confirm), keepDaysOption, confirmOption);

        return cmd;
    }

    private async Task HandleCleanupAsync(int keepDays, bool skipConfirm = false)
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-keepDays);
            var history = _stateService.GetDownloadHistory(10000); // Get all for counting
            var oldEntries = history.Where(h => h.DownloadedAt < cutoffDate).Count();

            if (oldEntries == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No old entries to clean up[/]");
                return;
            }

            if (!skipConfirm)
            {
                var confirm = AnsiConsole.Confirm($"Remove {oldEntries} entries older than {keepDays} days?");
                if (!confirm)
                {
                    AnsiConsole.MarkupLine("[yellow]Cleanup cancelled[/]");
                    return;
                }
            }

            await _stateService.CleanupHistoryAsync(keepDays);
            AnsiConsole.MarkupLine($"[green]✅ Cleaned up {oldEntries} old history entries[/]");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up history");
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message.EscapeMarkup()}[/]");
        }
    }

    private Command CreateSearchCommand()
    {
        var cmd = new Command("search", "Search download history");
        var queryOption = new Option<string>("--query", "Search query") { IsRequired = true };
        var limitOption = new Option<int>("--limit", () => 10, "Number of results");

        cmd.AddOption(queryOption);
        cmd.AddOption(limitOption);

        cmd.SetHandler(async (string query, int limit) =>
            await ShowHistoryAsync(limit, null, query), queryOption, limitOption);

        return cmd;
    }

    // Helper methods for export formats
    private async Task ExportJsonAsync(string filePath, List<DownloadHistoryItem> history)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(new
        {
            ExportedAt = DateTime.UtcNow,
            Count = history.Count,
            History = history
        }, options);

        await File.WriteAllTextAsync(filePath, json);
    }

    private async Task ExportCsvAsync(string filePath, List<DownloadHistoryItem> history)
    {
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("DownloadedAt,CompletedAt,Artist,Title,Status,TracksDownloaded,Quality,ErrorMessage");

        foreach (var item in history)
        {
            csv.AppendLine($"{item.DownloadedAt:yyyy-MM-dd HH:mm:ss}," +
                          $"{item.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss")}," +
                          $"\"{item.Artist.Replace("\"", "\"\"")}\"," +
                          $"\"{item.Title.Replace("\"", "\"\"")}\"," +
                          $"{item.FinalStatus}," +
                          $"{item.TracksDownloaded}," +
                          $"{item.Quality}," +
                          $"\"{item.ErrorMessage?.Replace("\"", "\"\"")}\"");
        }

        await File.WriteAllTextAsync(filePath, csv.ToString());
    }

    private async Task ExportHtmlAsync(string filePath, List<DownloadHistoryItem> history)
    {
        var stats = _stateService.GetStatistics();

        var html = "<!DOCTYPE html>\n<html>\n<head>\n    <title>Qobuz Download History</title>\n    <style>\n" +
                   "        body { font-family: Arial, sans-serif; margin: 20px; }\n" +
                   "        .stats { background: #f5f5f5; padding: 15px; border-radius: 5px; margin-bottom: 20px; }\n" +
                   "        table { border-collapse: collapse; width: 100%; }\n" +
                   "        th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }\n" +
                   "        th { background-color: #4CAF50; color: white; }\n" +
                   "        tr:nth-child(even) { background-color: #f2f2f2; }\n" +
                   "        .status-completed { color: green; font-weight: bold; }\n" +
                   "        .status-failed { color: red; font-weight: bold; }\n" +
                   "        .status-cancelled { color: orange; font-weight: bold; }\n" +
                   "    </style>\n</head>\n<body>\n" +
                   "    <h1>Qobuz Download History</h1>\n" +
                   "    <div class=\"stats\">\n        <h2>Statistics</h2>\n" +
                   $"        <p><strong>Total Downloads:</strong> {stats.TotalDownloads}</p>\n" +
                   $"        <p><strong>Success Rate:</strong> {(stats.TotalDownloads > 0 ? (double)stats.CompletedDownloads / stats.TotalDownloads : 0):P1}</p>\n" +
                   $"        <p><strong>Data Downloaded:</strong> {FormatBytes(stats.TotalBytes)}</p>\n" +
                   $"        <p><strong>Export Date:</strong> {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>\n" +
                   "    </div>\n    <table>\n        <tr>\n" +
                   "            <th>Date/Time</th>\n            <th>Artist</th>\n            <th>Album</th>\n" +
                   "            <th>Status</th>\n            <th>Tracks</th>\n            <th>Quality</th>\n" +
                   "            <th>Duration</th>\n        </tr>\n";

        foreach (var item in history)
        {
            var duration = item.CompletedAt.HasValue
                ? (item.CompletedAt.Value - item.DownloadedAt).ToString(@"mm\:ss")
                : "-";

            html += "        <tr>\n" +
                    $"            <td>{item.DownloadedAt:yyyy-MM-dd HH:mm:ss}</td>\n" +
                    $"            <td>{System.Web.HttpUtility.HtmlEncode(item.Artist)}</td>\n" +
                    $"            <td>{System.Web.HttpUtility.HtmlEncode(item.Title)}</td>\n" +
                    $"            <td class=\"status-{item.FinalStatus.ToString().ToLowerInvariant()}\">{item.FinalStatus}</td>\n" +
                    $"            <td>{item.TracksDownloaded}</td>\n" +
                    $"            <td>{item.Quality ?? "-"}</td>\n" +
                    $"            <td>{duration}</td>\n" +
                    "        </tr>\n";
        }

        html += "    </table>\n</body>\n</html>";

        await File.WriteAllTextAsync(filePath, html);
    }

    private static string GetStatusMarkup(DownloadStatus status)
    {
        return status switch
        {
            DownloadStatus.Completed => "[green]✅ Completed[/]",
            DownloadStatus.Failed => "[red]❌ Failed[/]",
            DownloadStatus.Cancelled => "[yellow]⏸️ Cancelled[/]",
            DownloadStatus.Downloading => "[blue]⬇️ Active[/]",
            DownloadStatus.Pending => "[grey]⏳ Pending[/]",
            _ => $"[grey]{status}[/]"
        };
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            >= 1_024 => $"{bytes / 1_024.0:F0} KB",
            _ => $"{bytes} B"
        };
    }
}
