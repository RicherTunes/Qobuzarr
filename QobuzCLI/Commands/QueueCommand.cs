using System.CommandLine;
using Microsoft.Extensions.Logging;
using QobuzCLI.Models;
using QobuzCLI.Services;
using Spectre.Console;
using Microsoft.Extensions.Logging.Console;

namespace QobuzCLI.Commands;

public class QueueCommand
{
    private readonly IQueueService _queueService;
    private readonly IStateService _stateService;
    private readonly ILogger<QueueCommand> _logger;

    public Command Command { get; }

    public QueueCommand(IQueueService queueService, IStateService stateService, ILogger<QueueCommand> logger)
    {
        _queueService = queueService;
        _stateService = stateService;
        _logger = logger;
        Command = CreateCommand();
    }

    private Command CreateCommand()
    {
        var queueCommand = new Command("queue", "Manage download queues");

        // Subcommands
        queueCommand.AddCommand(CreateListCommand());
        queueCommand.AddCommand(CreateShowCommand());
        queueCommand.AddCommand(CreateAddCommand());
        queueCommand.AddCommand(CreateRemoveCommand());
        queueCommand.AddCommand(CreateClearCommand());
        queueCommand.AddCommand(CreateStartCommand());
        queueCommand.AddCommand(CreateStopCommand());
        queueCommand.AddCommand(CreatePauseCommand());
        queueCommand.AddCommand(CreateResumeCommand());
        queueCommand.AddCommand(CreateRetryCommand());
        queueCommand.AddCommand(CreatePriorityCommand());
        queueCommand.AddCommand(CreateExportCommand());
        queueCommand.AddCommand(CreateImportCommand());
        queueCommand.AddCommand(CreateCreateCommand());

        return queueCommand;
    }

    private Command CreateListCommand()
    {
        var cmd = new Command("list", "List all download queues");

        cmd.SetHandler(async () => await HandleListAsync());

        return cmd;
    }

    private Command CreateShowCommand()
    {
        var cmd = new Command("show", "Show queue details");

        var queueIdArg = new Argument<string?>("queue-id", () => null, "Queue ID (default: first queue)");
        cmd.AddArgument(queueIdArg);

        cmd.SetHandler(async (string? queueId) => await HandleShowAsync(queueId), queueIdArg);

        return cmd;
    }

    private Command CreateAddCommand()
    {
        var cmd = new Command("add", "Add items to download queue");

        var queriesArg = new Argument<string[]>("queries", "Search queries to add to queue");
        var queueIdOption = new Option<string?>("--queue", "Target queue ID");
        var priorityOption = new Option<int>("--priority", () => 0, "Download priority (higher = sooner)");
        var typeOption = new Option<string?>("--type", "Force search type: album, artist, track");

        cmd.AddArgument(queriesArg);
        cmd.AddOption(queueIdOption);
        cmd.AddOption(priorityOption);
        cmd.AddOption(typeOption);

        cmd.SetHandler(async (string[] queries, string? queueId, int priority, string? type) =>
            await HandleAddAsync(queries, queueId, priority, type),
            queriesArg, queueIdOption, priorityOption, typeOption);

        return cmd;
    }

    private Command CreateRemoveCommand()
    {
        var cmd = new Command("remove", "Remove item from queue");

        var itemIdArg = new Argument<string>("item-id", "Item ID to remove");
        var queueIdOption = new Option<string?>("--queue", "Queue ID");

        cmd.AddArgument(itemIdArg);
        cmd.AddOption(queueIdOption);

        cmd.SetHandler(async (string itemId, string? queueId) =>
            await HandleRemoveAsync(itemId, queueId),
            itemIdArg, queueIdOption);

        return cmd;
    }

    private Command CreateClearCommand()
    {
        var cmd = new Command("clear", "Clear completed items from queue");

        var queueIdOption = new Option<string?>("--queue", "Queue ID");
        var allOption = new Option<bool>("--all", "Clear all items, not just completed");

        cmd.AddOption(queueIdOption);
        cmd.AddOption(allOption);

        cmd.SetHandler(async (string? queueId, bool all) =>
            await HandleClearAsync(queueId, all),
            queueIdOption, allOption);

        return cmd;
    }

    private Command CreateStartCommand()
    {
        var cmd = new Command("start", "Start processing queue");

        var queueIdOption = new Option<string?>("--queue", "Queue ID");

        cmd.AddOption(queueIdOption);

        cmd.SetHandler(async (string? queueId) => await HandleStartAsync(queueId), queueIdOption);

        return cmd;
    }

    private Command CreateStopCommand()
    {
        var cmd = new Command("stop", "Stop processing queue");

        var queueIdOption = new Option<string?>("--queue", "Queue ID");

        cmd.AddOption(queueIdOption);

        cmd.SetHandler(async (string? queueId) => await HandleStopAsync(queueId), queueIdOption);

        return cmd;
    }

    private Command CreatePauseCommand()
    {
        var cmd = new Command("pause", "Pause queue processing");

        var queueIdOption = new Option<string?>("--queue", "Queue ID");

        cmd.AddOption(queueIdOption);

        cmd.SetHandler(async (string? queueId) => await HandlePauseAsync(queueId), queueIdOption);

        return cmd;
    }

    private Command CreateResumeCommand()
    {
        var cmd = new Command("resume", "Resume queue processing");

        var queueIdOption = new Option<string?>("--queue", "Queue ID");

        cmd.AddOption(queueIdOption);

        cmd.SetHandler(async (string? queueId) => await HandleResumeAsync(queueId), queueIdOption);

        return cmd;
    }

    private Command CreateRetryCommand()
    {
        var cmd = new Command("retry", "Retry failed downloads");

        var queueIdOption = new Option<string?>("--queue", "Queue ID");

        cmd.AddOption(queueIdOption);

        cmd.SetHandler(async (string? queueId) => await HandleRetryAsync(queueId), queueIdOption);

        return cmd;
    }

    private Command CreatePriorityCommand()
    {
        var cmd = new Command("priority", "Set item priority");

        var itemIdArg = new Argument<string>("item-id", "Item ID");
        var priorityArg = new Argument<int>("priority", "New priority (higher = sooner)");
        var queueIdOption = new Option<string?>("--queue", "Queue ID");

        cmd.AddArgument(itemIdArg);
        cmd.AddArgument(priorityArg);
        cmd.AddOption(queueIdOption);

        cmd.SetHandler(async (string itemId, int priority, string? queueId) =>
            await HandlePriorityAsync(itemId, priority, queueId),
            itemIdArg, priorityArg, queueIdOption);

        return cmd;
    }

    private Command CreateExportCommand()
    {
        var cmd = new Command("export", "Export queue to file");

        var filePathArg = new Argument<string>("file-path", "Output file path");
        var queueIdOption = new Option<string?>("--queue", "Queue ID");

        cmd.AddArgument(filePathArg);
        cmd.AddOption(queueIdOption);

        cmd.SetHandler(async (string filePath, string? queueId) =>
            await HandleExportAsync(filePath, queueId),
            filePathArg, queueIdOption);

        return cmd;
    }

    private Command CreateImportCommand()
    {
        var cmd = new Command("import", "Import queue from file");

        var filePathArg = new Argument<string>("file-path", "Queue file path");

        cmd.AddArgument(filePathArg);

        cmd.SetHandler(async (string filePath) => await HandleImportAsync(filePath), filePathArg);

        return cmd;
    }

    private Command CreateCreateCommand()
    {
        var cmd = new Command("create", "Create a new download queue");

        var nameArg = new Argument<string>("name", "Queue name");
        var maxConcurrentOption = new Option<int>("--max-concurrent", () => 4, "Maximum concurrent downloads");

        cmd.AddArgument(nameArg);
        cmd.AddOption(maxConcurrentOption);

        cmd.SetHandler(async (string name, int maxConcurrent) =>
            await HandleCreateAsync(name, maxConcurrent),
            nameArg, maxConcurrentOption);

        return cmd;
    }

    private Task HandleListAsync()
    {
        var queues = _queueService.GetQueues();

        if (!queues.Any())
        {
            AnsiConsole.MarkupLine("[yellow]No download queues found.[/]");
            return Task.CompletedTask;
        }

        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn("ID");
        table.AddColumn("Name");
        table.AddColumn("Status");
        table.AddColumn("Items");
        table.AddColumn("Active");
        table.AddColumn("Completed");
        table.AddColumn("Failed");

        foreach (var queue in queues)
        {
            var stats = _queueService.GetQueueStatistics(queue.Id);
            var status = queue.IsPaused ? "[yellow]Paused[/]" : "[green]Active[/]";

            table.AddRow(
                queue.Id.Substring(0, 8),
                queue.Name,
                status,
                stats.TotalItems.ToString(),
                stats.ActiveDownloads.ToString(),
                stats.CompletedItems.ToString(),
                stats.FailedItems.ToString()
            );
        }

        AnsiConsole.Write(table);
        return Task.CompletedTask;
    }

    private Task HandleShowAsync(string? queueId)
    {
        var queue = GetQueueOrDefault(queueId);
        if (queue == null) return Task.CompletedTask;

        var stats = _queueService.GetQueueStatistics(queue.Id);

        // Queue header
        AnsiConsole.MarkupLine($"[blue]📋 Queue: {queue.Name}[/]");
        AnsiConsole.MarkupLine($"[dim]ID: {queue.Id} | Created: {queue.CreatedAt:yyyy-MM-dd HH:mm:ss}[/]");
        AnsiConsole.WriteLine();

        // Statistics
        var statsTable = new Table();
        statsTable.Border = TableBorder.None;
        statsTable.HideHeaders();
        statsTable.AddColumn("");
        statsTable.AddColumn("");

        statsTable.AddRow("Status:", queue.IsPaused ? "[yellow]Paused[/]" : "[green]Active[/]");
        statsTable.AddRow("Max Concurrent:", queue.MaxConcurrentDownloads.ToString());
        statsTable.AddRow("Total Items:", stats.TotalItems.ToString());
        statsTable.AddRow("Pending:", $"[yellow]{stats.PendingItems}[/]");
        statsTable.AddRow("Downloading:", $"[blue]{stats.ActiveDownloads}[/]");
        statsTable.AddRow("Completed:", $"[green]{stats.CompletedItems}[/]");
        statsTable.AddRow("Failed:", $"[red]{stats.FailedItems}[/]");

        if (stats.EstimatedTimeRemaining.HasValue && stats.EstimatedTimeRemaining.Value.TotalSeconds > 0)
        {
            statsTable.AddRow("Est. Time:", FormatTimeSpan(stats.EstimatedTimeRemaining.Value));
        }

        AnsiConsole.Write(statsTable);
        AnsiConsole.WriteLine();

        // Queue items
        if (queue.Items.Any())
        {
            AnsiConsole.MarkupLine("[blue]Queue Items:[/]");

            var itemsTable = new Table();
            itemsTable.Border = TableBorder.Rounded;
            itemsTable.AddColumn("ID");
            itemsTable.AddColumn("Query");
            itemsTable.AddColumn("Type");
            itemsTable.AddColumn("Priority");
            itemsTable.AddColumn("Status");
            itemsTable.AddColumn("Added");

            foreach (var item in queue.Items.OrderByDescending(i => i.Priority).ThenBy(i => i.AddedAt))
            {
                var shortId = item.Id.Substring(0, 8);
                var query = item.SearchQuery.Length > 30
                    ? item.SearchQuery.Substring(0, 27) + "..."
                    : item.SearchQuery;
                var status = FormatQueueStatus(item.Status);

                itemsTable.AddRow(
                    shortId,
                    query,
                    item.SearchType.ToString(),
                    item.Priority.ToString(),
                    status,
                    item.AddedAt.ToString("MM/dd HH:mm")
                );
            }

            AnsiConsole.Write(itemsTable);
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]Queue is empty[/]");
        }
        return Task.CompletedTask;
    }

    private async Task HandleAddAsync(string[] queries, string? queueId, int priority, string? type)
    {
        var queue = GetQueueOrDefault(queueId);
        if (queue == null) return;

        var searchType = ParseSearchType(type);
        var items = new List<QueuedDownload>();

        foreach (var query in queries)
        {
            items.Add(new QueuedDownload
            {
                SearchQuery = query,
                SearchType = searchType,
                Priority = priority
            });
        }

        var addedIds = await _queueService.AddBatchToQueueAsync(queue.Id, items);

        AnsiConsole.MarkupLine($"[green]✅ Added {addedIds.Count} item{(addedIds.Count > 1 ? "s" : "")} to queue '{queue.Name}'[/]");

        // Start processing if not already running
        await _queueService.StartQueueProcessingAsync(queue.Id);
    }

    private async Task HandleRemoveAsync(string itemId, string? queueId)
    {
        var queue = GetQueueOrDefault(queueId);
        if (queue == null) return;

        var success = await _queueService.RemoveFromQueueAsync(queue.Id, itemId);

        if (success)
        {
            AnsiConsole.MarkupLine($"[green]✅ Removed item {itemId} from queue[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]❌ Failed to remove item {itemId} (not found or currently downloading)[/]");
        }
    }

    private async Task HandleClearAsync(string? queueId, bool all)
    {
        var queue = GetQueueOrDefault(queueId);
        if (queue == null) return;

        if (all)
        {
            // Clear all items
            var items = queue.Items.Where(i => i.Status != QueueStatus.Downloading).ToList();
            var count = 0;

            foreach (var item in items)
            {
                if (await _queueService.RemoveFromQueueAsync(queue.Id, item.Id))
                {
                    count++;
                }
            }

            AnsiConsole.MarkupLine($"[green]✅ Cleared {count} items from queue[/]");
        }
        else
        {
            // Clear only completed items
            var count = await _queueService.ClearCompletedAsync(queue.Id);
            AnsiConsole.MarkupLine($"[green]✅ Cleared {count} completed items from queue[/]");
        }
    }

    private async Task HandleStartAsync(string? queueId)
    {
        var queue = GetQueueOrDefault(queueId);
        if (queue == null) return;

        await _queueService.StartQueueProcessingAsync(queue.Id);

        AnsiConsole.MarkupLine($"[green]✅ Started processing queue '{queue.Name}'[/]");
        await MonitorQueueProgressAsync(queue.Id);
    }

    private async Task HandleStopAsync(string? queueId)
    {
        var queue = GetQueueOrDefault(queueId);
        if (queue == null) return;

        await _queueService.StopQueueProcessingAsync(queue.Id);
        AnsiConsole.MarkupLine($"[yellow]⏹️  Stopped processing queue '{queue.Name}'[/]");
    }

    private async Task HandlePauseAsync(string? queueId)
    {
        var queue = GetQueueOrDefault(queueId);
        if (queue == null) return;

        await _queueService.SetQueuePausedAsync(queue.Id, true);
        AnsiConsole.MarkupLine($"[yellow]⏸️  Paused queue '{queue.Name}'[/]");
    }

    private async Task HandleResumeAsync(string? queueId)
    {
        var queue = GetQueueOrDefault(queueId);
        if (queue == null) return;

        await _queueService.SetQueuePausedAsync(queue.Id, false);
        AnsiConsole.MarkupLine($"[green]▶️  Resumed queue '{queue.Name}'[/]");
    }

    private async Task HandleRetryAsync(string? queueId)
    {
        var queue = GetQueueOrDefault(queueId);
        if (queue == null) return;

        var count = await _queueService.RetryFailedItemsAsync(queue.Id);

        if (count > 0)
        {
            AnsiConsole.MarkupLine($"[green]🔄 Queued {count} failed item{(count > 1 ? "s" : "")} for retry[/]");
            await _queueService.StartQueueProcessingAsync(queue.Id);
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]No failed items to retry[/]");
        }
    }

    private async Task HandlePriorityAsync(string itemId, int priority, string? queueId)
    {
        var queue = GetQueueOrDefault(queueId);
        if (queue == null) return;

        var success = await _queueService.SetPriorityAsync(queue.Id, itemId, priority);

        if (success)
        {
            AnsiConsole.MarkupLine($"[green]✅ Set priority {priority} for item {itemId}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]❌ Failed to set priority (item not found or not queued)[/]");
        }
    }

    private async Task HandleExportAsync(string filePath, string? queueId)
    {
        var queue = GetQueueOrDefault(queueId);
        if (queue == null) return;

        try
        {
            await _queueService.ExportQueueAsync(queue.Id, filePath);
            AnsiConsole.MarkupLine($"[green]✅ Exported queue to {filePath}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Failed to export queue: {ex.Message}[/]");
        }
    }

    private async Task HandleImportAsync(string filePath)
    {
        try
        {
            var queue = await _queueService.ImportQueueAsync(filePath);
            AnsiConsole.MarkupLine($"[green]✅ Imported queue '{queue.Name}' (ID: {queue.Id.Substring(0, 8)})[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Failed to import queue: {ex.Message}[/]");
        }
    }

    private async Task HandleCreateAsync(string name, int maxConcurrent)
    {
        try
        {
            var queue = await _queueService.CreateQueueAsync(name, maxConcurrent);
            AnsiConsole.MarkupLine($"[green]✅ Created queue '{queue.Name}' (ID: {queue.Id.Substring(0, 8)})[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Failed to create queue: {ex.Message}[/]");
        }
    }

    private DownloadQueue? GetQueueOrDefault(string? queueId)
    {
        if (string.IsNullOrEmpty(queueId))
        {
            var queue = _queueService.GetQueues().FirstOrDefault();
            if (queue == null)
            {
                AnsiConsole.MarkupLine("[red]No download queues found. Create one with 'qobuz queue create <name>'[/]");
            }
            return queue;
        }

        var targetQueue = _queueService.GetQueue(queueId);
        if (targetQueue == null)
        {
            AnsiConsole.MarkupLine($"[red]Queue '{queueId}' not found[/]");
        }
        return targetQueue;
    }

    private SearchType ParseSearchType(string? type)
    {
        if (string.IsNullOrEmpty(type))
            return SearchType.Auto;

        return type.ToLower() switch
        {
            "album" => SearchType.Album,
            "artist" => SearchType.Artist,
            "track" => SearchType.Track,
            _ => SearchType.Auto
        };
    }

    private string FormatQueueStatus(QueueStatus status)
    {
        return status switch
        {
            QueueStatus.Pending => "[dim]Pending[/]",
            QueueStatus.Searching => "[blue]Searching[/]",
            QueueStatus.WaitingForSelection => "[yellow]Waiting[/]",
            QueueStatus.Queued => "[cyan]Queued[/]",
            QueueStatus.Downloading => "[blue]📥 Downloading[/]",
            QueueStatus.Completed => "[green]✅ Completed[/]",
            QueueStatus.Failed => "[red]❌ Failed[/]",
            QueueStatus.Cancelled => "[dim]Cancelled[/]",
            QueueStatus.Retrying => "[yellow]🔄 Retrying[/]",
            _ => status.ToString()
        };
    }

    private string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalDays >= 1)
            return $"{(int)timeSpan.TotalDays}d {timeSpan.Hours}h";
        if (timeSpan.TotalHours >= 1)
            return $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m";
        if (timeSpan.TotalMinutes >= 1)
            return $"{(int)timeSpan.TotalMinutes}m {timeSpan.Seconds}s";
        return $"{timeSpan.Seconds}s";
    }

    private async Task MonitorQueueProgressAsync(string queueId)
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
                await Task.Delay(2000);
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[yellow]⏹️ Monitoring stopped. Queue continues processing in background.[/]");
                break;
            }
        }
        return;
    }
}
