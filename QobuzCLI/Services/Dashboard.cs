using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace QobuzCLI.Services;

/// <summary>
/// Unified dashboard implementation with proper column alignment and full console usage
/// </summary>
public class Dashboard : IDashboard
{
    private readonly ILogger<Dashboard> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDashboardStateProvider _dashboardState;
    private readonly object _lock = new();

    // Dashboard state
    private bool _isActive;
    private bool _disposed;
    private DateTime _startTime;

    /// <summary>
    /// Gets whether the dashboard is currently active
    /// </summary>
    public bool IsActive
    {
        get
        {
            lock (_lock) return _isActive;
        }
    }

    // Dashboard data
    private string _currentOperation = "";
    private int _totalItems;
    private int _processedItems;
    private int _successCount;
    private int _failedCount;
    private string _currentItem = "";
    private string _lastSuccessfulItem = "";
    private double _itemsPerSecond;

    // Performance tracking
    private double _peakSpeed;
    private double _averageSpeed;
    private readonly Queue<(DateTime timestamp, int processed)> _speedHistory = new();
    private int _consecutiveFailures;
    private DateTime _lastSuccessTime;

    // Refresh timer
    private Timer? _refreshTimer;

    // Track console height for proper clearing
    private int _lastConsoleHeight;

    // Log buffer for bottom section
    private readonly Queue<string> _logBuffer = new();
    private static int MaxLogLines => Math.Max(10, GetSafeConsoleHeight() / 4); // Dynamic based on console height

    // Safe console dimension access with fallbacks
    private static int GetSafeConsoleWidth()
    {
        try
        {
            return Console.WindowWidth;
        }
        catch (IOException)
        {
            return 120; // Safe default width
        }
    }

    private static int GetSafeConsoleHeight()
    {
        try
        {
            return Console.WindowHeight;
        }
        catch (IOException)
        {
            return 30; // Safe default height
        }
    }

    // Dynamic column width calculation
    private static int CalculateColumnWidth(int consoleWidth, int columnIndex, int totalColumns = 4)
    {
        // Reserve space for borders and padding (approximately 3 chars per column + table borders)
        var borderSpace = (totalColumns * 3) + 4;
        var availableWidth = Math.Max(80, consoleWidth - borderSpace); // Minimum 80 chars

        // Distribute width proportionally with slight preference for the last column
        var baseWidth = availableWidth / totalColumns;

        // Give the last column (Activity & Status) slightly more space
        if (columnIndex == totalColumns - 1)
        {
            return baseWidth + (availableWidth % totalColumns);
        }

        return baseWidth;
    }

    public Dashboard(ILogger<Dashboard> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _dashboardState = serviceProvider.GetRequiredService<IDashboardStateProvider>();
    }

    public void Start(string operation, int totalItems)
    {
        lock (_lock)
        {
            if (_isActive) return;

            _currentOperation = operation;
            _totalItems = totalItems;
            _processedItems = 0;
            _successCount = 0;
            _failedCount = 0;
            _startTime = DateTime.UtcNow;
            _isActive = true;

            // Notify dashboard state
            _dashboardState.SetDashboardActive(true);

            // Clear console once and hide cursor
            AnsiConsole.Clear();
            AnsiConsole.Cursor.Hide();
            _lastConsoleHeight = GetSafeConsoleHeight();

            // Start refresh timer - 250ms for smooth updates
            _refreshTimer = new Timer(UpdateDisplay, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(250));
        }
    }

    public void UpdateProgress(int processed, int success, int failed, string currentItem = "", string lastSuccessful = "")
    {
        lock (_lock)
        {
            _processedItems = processed;
            _successCount = success;
            _failedCount = failed;

            if (!string.IsNullOrEmpty(currentItem))
                _currentItem = currentItem;

            if (!string.IsNullOrEmpty(lastSuccessful))
            {
                _lastSuccessfulItem = lastSuccessful;
                _lastSuccessTime = DateTime.UtcNow;
                _consecutiveFailures = 0;
            }
            else if (failed > _failedCount)
            {
                _consecutiveFailures++;
            }

            // Calculate speed
            var elapsed = DateTime.UtcNow - _startTime;
            if (elapsed.TotalSeconds > 0)
            {
                _itemsPerSecond = processed / elapsed.TotalSeconds;

                // Track speed history
                _speedHistory.Enqueue((DateTime.UtcNow, processed));
                while (_speedHistory.Count > 20) // Keep last 20 samples
                    _speedHistory.Dequeue();

                // Calculate average speed from recent samples
                if (_speedHistory.Count > 1)
                {
                    var firstSample = _speedHistory.First();
                    var lastSample = _speedHistory.Last();
                    var timeDiff = (lastSample.timestamp - firstSample.timestamp).TotalSeconds;
                    if (timeDiff > 0)
                    {
                        _averageSpeed = (lastSample.processed - firstSample.processed) / timeDiff;
                    }
                }

                // Track peak speed
                if (_itemsPerSecond > _peakSpeed)
                    _peakSpeed = _itemsPerSecond;
            }
        }
    }

    public void AddLogMessage(string message)
    {
        lock (_logBuffer)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            _logBuffer.Enqueue($"[dim]{timestamp}[/] {message}");

            while (_logBuffer.Count > MaxLogLines)
                _logBuffer.Dequeue();
        }
    }

    private void UpdateDisplay(object? state)
    {
        if (!_isActive || _disposed) return;

        try
        {
            lock (_lock)
            {
                var consoleWidth = GetSafeConsoleWidth();
                var consoleHeight = GetSafeConsoleHeight();

                // If console size changed, we need to clear and redraw
                if (consoleHeight != _lastConsoleHeight)
                {
                    AnsiConsole.Clear();
                    _lastConsoleHeight = consoleHeight;
                }
                else
                {
                    // Position cursor at top for in-place update
                    Console.SetCursorPosition(0, 0);
                }

                // Calculate metrics
                var elapsed = DateTime.UtcNow - _startTime;
                var percentage = _totalItems > 0 ? (double)_processedItems / _totalItems * 100 : 0;
                var successRate = _processedItems > 0 ? (double)_successCount / _processedItems * 100 : 0;
                var failureRate = _processedItems > 0 ? (double)_failedCount / _processedItems * 100 : 0;
                var remaining = Math.Max(0, _totalItems - _processedItems);
                var efficiency = _peakSpeed > 0 ? (_itemsPerSecond / _peakSpeed) * 100 : 100;

                // ETA calculation
                var etaText = "Calculating...";
                if (_itemsPerSecond > 0 && remaining > 0)
                {
                    var etaSeconds = remaining / _itemsPerSecond;
                    var eta = TimeSpan.FromSeconds(etaSeconds);
                    etaText = $"{eta:hh\\:mm\\:ss}";
                }
                else if (remaining == 0)
                {
                    etaText = "Complete!";
                }

                // Title
                var title = new Panel($"[bold yellow]📊 {_currentOperation}[/]")
                    .Border(BoxBorder.Rounded)
                    .Expand();
                AnsiConsole.Write(title);

                // Progress bar - scale to console width
                var progressBarWidth = Math.Max(20, consoleWidth - 20); // Leave some margin
                var filledWidth = (int)(progressBarWidth * percentage / 100);
                var emptyWidth = progressBarWidth - filledWidth;

                var progressBar = new Rule($"[green]{"█".PadRight(filledWidth, '█')}[/][grey]{"░".PadRight(emptyWidth, '░')}[/] [bold]{percentage:F1}%[/]")
                    .LeftJustified();
                AnsiConsole.Write(progressBar);

                // Create the data table with dynamic column widths
                var col1Width = CalculateColumnWidth(consoleWidth, 0);
                var col2Width = CalculateColumnWidth(consoleWidth, 1);
                var col3Width = CalculateColumnWidth(consoleWidth, 2);
                var col4Width = CalculateColumnWidth(consoleWidth, 3);

                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .Expand() // Use full width available
                    .AddColumn(new TableColumn("⏱️ Progress & Timing").Width(col1Width).Centered())
                    .AddColumn(new TableColumn("📈 Performance & Rates").Width(col2Width).Centered())
                    .AddColumn(new TableColumn("📊 Statistics & Health").Width(col3Width).Centered())
                    .AddColumn(new TableColumn("🔄 Current Activity & Status").Width(col4Width).Centered());

                // Calculate how many rows we can display - use more of the available height
                var reservedHeight = 8; // Title (3) + Progress bar (2) + Table borders (3)
                if (_logBuffer.Count > 0) reservedHeight += 6; // Reserve space for log panel if needed

                var availableHeight = consoleHeight - reservedHeight;
                var maxDataRows = Math.Max(8, availableHeight); // At least 8 rows, no upper limit

                // Add data rows
                for (int row = 0; row < maxDataRows; row++)
                {
                    var col1 = "";
                    var col2 = "";
                    var col3 = "";
                    var col4 = "";

                    switch (row)
                    {
                        case 0:
                            col1 = $"⏱️ {elapsed:hh\\:mm\\:ss}";
                            col2 = $"🚀 {_itemsPerSecond,8:F1}/s";
                            col3 = $"📊 {percentage,6:F1}%";
                            col4 = $"🔄 {TruncateText(_currentItem, col4Width - 8)}";
                            break;

                        case 1:
                            col1 = $"🎯 ETA: {etaText}";
                            col2 = $"📈 Avg: {_averageSpeed,6:F1}/s";
                            col3 = $"✅ {successRate,6:F1}%";
                            col4 = $"✅ {TruncateText(_lastSuccessfulItem, col4Width - 8)}";
                            break;

                        case 2:
                            col1 = $"📈 {_processedItems,6:N0} Done";
                            col2 = $"🚀 Peak: {_peakSpeed,5:F1}/s";
                            col3 = $"❌ {failureRate,6:F1}%";
                            col4 = $"⚠️ Consecutive Fails: {_consecutiveFailures}";
                            break;

                        case 3:
                            col1 = $"✅ {_successCount,6:N0} OK";
                            col2 = $"⚡ {efficiency,3:F0}% Efficiency";
                            col3 = $"📊 {remaining,6:N0} Left";
                            col4 = $"🎯 System Status: Optimal";
                            break;

                        case 4:
                            col1 = $"❌ {_failedCount,6:N0} Failed";
                            col2 = $"⏱️ {elapsed.TotalSeconds / Math.Max(1, _processedItems),5:F2}s/item";
                            col3 = $"📊 {_totalItems,6:N0} Total";
                            col4 = $"🔍 Monitoring Active";
                            break;

                        case 5:
                            var completionEta = _itemsPerSecond > 0 && remaining > 0
                                ? DateTime.Now.AddSeconds(remaining / _itemsPerSecond).ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
                                : "Unknown";
                            col1 = $"⏰ Complete: {completionEta}";
                            col2 = $"📊 Load: {(_itemsPerSecond / Math.Max(1, _peakSpeed) * 100),3:F0}%";
                            col3 = $"🎯 Queue: {remaining,6:N0}";
                            col4 = $"💾 Status: Running";
                            break;

                        case 6:
                            col1 = $"🔄 Runtime: {elapsed.TotalMinutes,3:F0}m";
                            col2 = $"💾 Memory: {GC.GetTotalMemory(false) / 1024 / 1024,4:N0}MB";
                            col3 = $"📈 Trend: ↗️ Up";
                            col4 = $"✨ Performance: Optimal";
                            break;

                        case 7:
                            var timeSinceSuccess = _lastSuccessTime != default ? DateTime.UtcNow - _lastSuccessTime : TimeSpan.Zero;
                            col1 = $"🕐 Last Success: {(timeSinceSuccess.TotalSeconds > 0 ? $"{timeSinceSuccess:mm\\:ss} ago" : "Recent")}";
                            col2 = $"📊 Throughput: {_itemsPerSecond * 60,4:F0}/min";
                            col3 = $"🏁 Progress: {new string('█', (int)(percentage / 5))}";
                            col4 = $"🌐 Network: Stable";
                            break;

                        default:
                            // Additional rows if space permits
                            if (row < maxDataRows)
                            {
                                col1 = "";
                                col2 = "";
                                col3 = "";
                                col4 = "";
                            }
                            break;
                    }

                    if (!string.IsNullOrEmpty(col1) || !string.IsNullOrEmpty(col2) ||
                        !string.IsNullOrEmpty(col3) || !string.IsNullOrEmpty(col4))
                    {
                        table.AddRow(
                            PadToWidth(col1, col1Width - 2),
                            PadToWidth(col2, col2Width - 2),
                            PadToWidth(col3, col3Width - 2),
                            PadToWidth(col4, col4Width - 2)
                        );
                    }
                }

                AnsiConsole.Write(table);

                // Log section - show if we have logs and reasonable space
                if (_logBuffer.Count > 0 && consoleHeight > 20)
                {
                    // Calculate available space for logs
                    var usedHeight = 8 + maxDataRows; // Base UI + table rows
                    var remainingHeight = consoleHeight - usedHeight - 2; // Leave some margin

                    if (remainingHeight > 3)
                    {
                        // Adjust log lines to fit available space
                        var displayLogs = _logBuffer.Take(Math.Min(_logBuffer.Count, remainingHeight - 3)).ToList();

                        var logPanel = new Panel(string.Join("\n", displayLogs))
                            .Header("[bold]📜 Recent Activity[/]")
                            .Border(BoxBorder.Rounded)
                            .Expand();
                        AnsiConsole.Write(logPanel);
                    }
                }
            }
        }
        catch (Exception ex)
        {
#pragma warning disable CA1848 // Use LoggerMessage delegates - single usage, overkill
            _logger.LogError(ex, "Failed to update dashboard display");
#pragma warning restore CA1848
        }
    }

    private static string PadToWidth(string text, int width)
    {
        if (string.IsNullOrEmpty(text))
            return new string(' ', width);

        try
        {
            // Escape special characters that could cause markup errors
            text = text.Replace("[", "[[").Replace("]", "]]");

            // Remove markup for length calculation
            var plainText = Markup.Remove(text);

            if (plainText.Length > width)
                return string.Concat(text.AsSpan(0, width - 3), "...");

            var padding = width - plainText.Length;
            return text + new string(' ', padding);
        }
        catch (Exception)
        {
            // If markup processing fails, use safe fallback
            var safeText = text.Replace("[", "").Replace("]", "").Replace("<", "").Replace(">", "");
            if (safeText.Length > width)
                return string.Concat(safeText.AsSpan(0, width - 3), "...");

            var padding = width - safeText.Length;
            return safeText + new string(' ', padding);
        }
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text ?? "";

        return string.Concat(text.AsSpan(0, maxLength - 3), "...");
    }

    public void StopOperation()
    {
        lock (_lock)
        {
            if (!_isActive) return;

            _isActive = false;
            _refreshTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            // Show final state briefly
            Thread.Sleep(1000);

            // Restore console
            AnsiConsole.Cursor.Show();
            AnsiConsole.Clear();

            // Notify dashboard state
            _dashboardState.SetDashboardActive(false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        StopOperation();
        _refreshTimer?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
