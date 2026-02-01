using Spectre.Console;
using System.Collections.Concurrent;

namespace QobuzCLI.Utilities;

/// <summary>
/// Robust, cross-platform batch progress logger with error handling
/// </summary>
public class BatchProgressLogger : IDisposable
{
    private readonly int _totalItems;
    private readonly object _lock = new();
    private int _processedCount = 0;
    private int _successCount = 0;
    private int _failedCount = 0;
    private int _queuedCount = 0;
    private DateTime _startTime;
    private string? _currentItem;
    private readonly ConcurrentQueue<string> _recentResults = new();
    private readonly Timer _updateTimer;
    private volatile bool _disposed = false;
    private readonly bool _useConsolePositioning;
    // Removed unused field _lastDisplayLines

    public BatchProgressLogger(int totalItems)
    {
        _totalItems = Math.Max(1, totalItems); // Ensure at least 1 to prevent division by zero
        _startTime = DateTime.Now;

        // Check if console positioning is supported
        _useConsolePositioning = CheckConsolePositioningSupport();

        if (_useConsolePositioning)
        {
            Console.Clear();
        }
        else
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[blue]🚀 Starting batch processing...[/]");
            AnsiConsole.WriteLine();
        }

        DisplayStatus();

        // Update display every 1 second (less aggressive than 500ms)
        _updateTimer = new Timer(UpdateDisplay, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private static bool CheckConsolePositioningSupport()
    {
        try
        {
            // Test if we can get console dimensions and set cursor position
            var width = Console.WindowWidth;
            var height = Console.WindowHeight;
            Console.SetCursorPosition(0, Console.CursorTop);
            return width > 0 && height > 0;
        }
        catch
        {
            return false;
        }
    }

    public void UpdateProgress(string item, string status, string? error = null)
    {
        if (_disposed) return;

        // Sanitize inputs
        item = item ?? "";
        status = status ?? "unknown";

        lock (_lock)
        {
            _processedCount++;
            _currentItem = item;

            switch (status.ToLower())
            {
                case "success":
                case "queued":
                    _successCount++;
                    _queuedCount++;
                    _recentResults.Enqueue($"✅ {TruncateString(item, 60)}");
                    break;
                case "failed":
                case "error":
                    _failedCount++;
                    var errorMsg = error != null ? $" - {TruncateString(error, 30)}" : "";
                    _recentResults.Enqueue($"❌ {TruncateString(item, 60 - errorMsg.Length)}{errorMsg}");
                    break;
                case "no results":
                case "not found":
                    _failedCount++;
                    _recentResults.Enqueue($"⚠️  {TruncateString(item, 55)} - No results found");
                    break;
            }

            // Keep only last 5 results
            while (_recentResults.Count > 5)
            {
                _recentResults.TryDequeue(out _);
            }
        }
    }

    public void SetCurrentItem(string item)
    {
        if (_disposed) return;

        lock (_lock)
        {
            _currentItem = item ?? "";
        }
    }

    private void UpdateDisplay(object? state)
    {
        if (_disposed) return;

        try
        {
            DisplayStatus();
        }
        catch (Exception)
        {
            // Fallback to simple progress reporting if display fails
            try
            {
                lock (_lock)
                {
                    if (_processedCount % 10 == 0) // Only log every 10th item to avoid spam
                    {
                        AnsiConsole.MarkupLine($"[dim]Progress: {_processedCount}/{_totalItems} ({_successCount} success, {_failedCount} failed)[/]");
                    }
                }
            }
            catch
            {
                // Last resort - do nothing if even simple logging fails
            }
        }
    }

    private void DisplayStatus()
    {
        if (_disposed) return;

        lock (_lock)
        {
            var elapsed = DateTime.Now - _startTime;
            var remaining = _processedCount > 0 ?
                TimeSpan.FromMilliseconds(elapsed.TotalMilliseconds * (_totalItems - _processedCount) / _processedCount) :
                TimeSpan.Zero;

            if (_useConsolePositioning)
            {
                DisplayWithPositioning(elapsed, remaining);
            }
            else
            {
                DisplayWithoutPositioning(elapsed, remaining);
            }
        }
    }

    private void DisplayWithPositioning(TimeSpan elapsed, TimeSpan remaining)
    {
        try
        {
            var consoleWidth = Math.Max(80, Math.Min(Console.WindowWidth, 120)); // Safe width between 80-120

            // Move cursor to top
            Console.SetCursorPosition(0, 0);

            // Progress line
            var progressPercent = _totalItems > 0 ? (double)_processedCount / _totalItems * 100 : 0;
            var progressBar = CreateProgressBar(progressPercent, Math.Min(40, consoleWidth - 30));

            Console.WriteLine($"Progress: {progressBar} {_processedCount}/{_totalItems} ({progressPercent:F1}%)".PadRight(consoleWidth));

            // Stats line
            Console.WriteLine($"✅ Success: {_successCount}  ❌ Failed: {_failedCount}  🕐 {elapsed:hh\\:mm\\:ss}  ⏱️ ETA: {remaining:hh\\:mm\\:ss}".PadRight(consoleWidth));

            // Current item line
            var currentDisplay = _currentItem != null ?
                $"Current: {TruncateString(_currentItem, consoleWidth - 20)}" :
                "Waiting...";
            Console.WriteLine(currentDisplay.PadRight(consoleWidth));

            // Queue status
            Console.WriteLine($"📥 Items added to queue: {_queuedCount}".PadRight(consoleWidth));

            // Separator
            Console.WriteLine(new string('─', Math.Min(consoleWidth, 80)));

            // Recent results (last 5)
            Console.WriteLine("Recent Results:".PadRight(consoleWidth));
            var results = _recentResults.ToArray();

            // Show up to 5 recent results, handling array bounds safely
            for (int i = 0; i < 5; i++)
            {
                string line;
                if (results.Length > 0 && i < results.Length)
                {
                    // Get results from most recent backwards
                    var resultIndex = Math.Max(0, results.Length - 5 + i);
                    if (resultIndex < results.Length)
                    {
                        line = $"  {TruncateString(results[resultIndex], consoleWidth - 3)}";
                    }
                    else
                    {
                        line = "";
                    }
                }
                else
                {
                    line = "";
                }
                Console.WriteLine(line.PadRight(consoleWidth));
            }

            // Clear extra lines that might be left over
            for (int i = 0; i < 3; i++)
            {
                Console.WriteLine("".PadRight(consoleWidth));
            }
        }
        catch
        {
            // Fall back to non-positioning display if console manipulation fails
            DisplayWithoutPositioning(elapsed, remaining);
        }
    }

    private void DisplayWithoutPositioning(TimeSpan elapsed, TimeSpan remaining)
    {
        // Only update every 5th call to reduce spam when not using positioning
        if (_processedCount % 5 != 0) return;

        var progressPercent = _totalItems > 0 ? (double)_processedCount / _totalItems * 100 : 0;
        var progressBar = CreateProgressBar(progressPercent, 30);

        AnsiConsole.WriteLine($"Progress: {progressBar} {_processedCount}/{_totalItems} ({progressPercent:F1}%)");
        AnsiConsole.MarkupLine($"[green]✅ Success: {_successCount}[/]  [red]❌ Failed: {_failedCount}[/]  [dim]🕐 {elapsed:hh\\:mm\\:ss}[/]");

        if (_currentItem != null)
        {
            AnsiConsole.MarkupLine($"[dim]Current: {TruncateString(_currentItem, 60)}[/]");
        }

        // Show last result
        if (_recentResults.TryPeek(out var lastResult))
        {
            AnsiConsole.MarkupLine($"[dim]Latest: {TruncateString(lastResult, 60)}[/]");
        }

        AnsiConsole.WriteLine();
    }

    private static string CreateProgressBar(double percent, int width)
    {
        if (width <= 0) return "[]";

        // Ensure percent is within valid range
        percent = Math.Max(0, Math.Min(100, percent));

        var filled = (int)Math.Round(percent / 100.0 * width);
        filled = Math.Max(0, Math.Min(width, filled));
        var empty = width - filled;

        var filledStr = filled > 0 ? new string('=', filled) : "";
        var emptyStr = empty > 0 ? new string('-', empty) : "";

        return $"[{filledStr}{emptyStr}]";
    }

    private static string TruncateString(string str, int maxLength)
    {
        if (string.IsNullOrEmpty(str)) return "";
        if (maxLength <= 3) return "...";
        if (str.Length <= maxLength) return str;
        return str.Substring(0, maxLength - 3) + "...";
    }

    public void Complete()
    {
        if (_disposed) return;

        lock (_lock)
        {
            _disposed = true;
        }

        // Dispose timer outside of lock to prevent deadlock
        try
        {
            _updateTimer?.Dispose();
            // Small delay to let any in-flight timer callbacks complete
            Thread.Sleep(10);
        }
        catch
        {
            // Ignore timer disposal errors
        }

        lock (_lock)
        {
            var elapsed = DateTime.Now - _startTime;

            if (_useConsolePositioning)
            {
                Console.Clear();
            }
            else
            {
                AnsiConsole.WriteLine();
            }

            AnsiConsole.MarkupLine("[green]🎉 Batch Processing Complete![/]");
            AnsiConsole.MarkupLine("[blue]📊 Final Results:[/]");
            AnsiConsole.MarkupLine($"   [green]✅ Successful: {_successCount}[/]");
            AnsiConsole.MarkupLine($"   [red]❌ Failed: {_failedCount}[/]");
            AnsiConsole.MarkupLine($"   [yellow]📥 Added to queue: {_queuedCount}[/]");
            AnsiConsole.MarkupLine($"   [dim]🕐 Total time: {elapsed:hh\\:mm\\:ss}[/]");

            if (_totalItems > 0)
            {
                AnsiConsole.MarkupLine($"   [dim]⚡ Average: {elapsed.TotalSeconds / _totalItems:F1}s per item[/]");
            }

            AnsiConsole.WriteLine();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            try
            {
                _updateTimer?.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }
}
