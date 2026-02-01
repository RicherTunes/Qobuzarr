using QobuzCLI.Models;
using Spectre.Console;

namespace QobuzCLI.Services;

/// <summary>
/// Handles queue progress monitoring and display for CLI.
/// Extracted from DownloadCommand to improve separation of concerns.
/// </summary>
public class QueueMonitoringService
{
    private readonly IQueueService _queueService;

    public QueueMonitoringService(IQueueService queueService)
    {
        _queueService = queueService;
    }

    public async Task MonitorQueueProgressSimpleAsync(string queueId, List<string> itemIds)
    {
        AnsiConsole.MarkupLine("[cyan]🔄 Monitoring queue progress...[/]");
        AnsiConsole.MarkupLine("[dim]Press Ctrl+C to stop monitoring (queue will continue in background)[/]");

        var queue = _queueService.GetQueue(queueId);
        if (queue == null) return;

        // Simple progress monitoring without complex dashboard
        while (true)
        {
            var stats = _queueService.GetQueueStatistics(queueId);
            var remaining = stats.PendingItems + stats.ActiveDownloads;

            if (remaining == 0)
            {
                AnsiConsole.MarkupLine("[green]✅ All downloads completed[/]");
                break;
            }

            AnsiConsole.MarkupLine($"[blue]Progress:[/] Active: {stats.ActiveDownloads}, Completed: {stats.CompletedItems}, Failed: {stats.FailedItems}, Remaining: {remaining}");

            try
            {
                await Task.Delay(2000).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[yellow]⏹️ Monitoring stopped. Queue continues processing in background.[/]");
                break;
            }
        }
    }
}
