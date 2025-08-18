using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace QobuzCLI.Services;

/// <summary>
/// Wrapper to convert IOptions to IOptionsMonitor
/// </summary>
public class OptionsMonitorWrapper : IOptionsMonitor<ConsoleLoggerOptions>
{
    private readonly IOptions<ConsoleLoggerOptions> _options;
    
    public OptionsMonitorWrapper(IOptions<ConsoleLoggerOptions> options)
    {
        _options = options;
    }
    
    public ConsoleLoggerOptions CurrentValue => _options.Value;
    
    public ConsoleLoggerOptions Get(string name) => _options.Value;
    
    public IDisposable OnChange(Action<ConsoleLoggerOptions, string> listener) => new EmptyDisposable();
    
    private class EmptyDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

/// <summary>
/// Console logging provider that can be dynamically disabled during dashboard mode
/// </summary>
public class DashboardAwareConsoleLoggerProvider : ILoggerProvider
{
    private readonly ConsoleLoggerProvider _consoleProvider;
    private readonly IDisposable _optionsReloadToken;
    private readonly IDashboardStateProvider _dashboardState;
    
    public DashboardAwareConsoleLoggerProvider(IOptions<ConsoleLoggerOptions> options, IDashboardStateProvider dashboardState)
    {
        var monitor = new OptionsMonitorWrapper(options);
        _consoleProvider = new ConsoleLoggerProvider(monitor);
        _optionsReloadToken = monitor.OnChange((_, _) => { });
        _dashboardState = dashboardState;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new DashboardAwareConsoleLogger(_consoleProvider.CreateLogger(categoryName), _dashboardState);
    }

    public void Dispose()
    {
        _optionsReloadToken?.Dispose();
        _consoleProvider?.Dispose();
    }
}

/// <summary>
/// Console logger that respects dashboard state
/// </summary>
public class DashboardAwareConsoleLogger : ILogger
{
    private readonly ILogger _baseLogger;
    private readonly IDashboardStateProvider _dashboardState;

    public DashboardAwareConsoleLogger(ILogger baseLogger, IDashboardStateProvider dashboardState)
    {
        _baseLogger = baseLogger;
        _dashboardState = dashboardState;
    }

    public IDisposable BeginScope<TState>(TState state) => _baseLogger.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => _baseLogger.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        // Only log to console if dashboard is not active
        if (!_dashboardState.IsDashboardActive)
        {
            _baseLogger.Log(logLevel, eventId, state, exception, formatter);
        }
        // If dashboard is active, suppress all console output
    }
}

/// <summary>
/// Static dashboard state for simple global access
/// </summary>
public static class DashboardState
{
    public static bool IsActive { get; private set; }
    public static event EventHandler<bool> StateChanged;

    public static void SetActive(bool active)
    {
        if (IsActive != active)
        {
            IsActive = active;
            StateChanged?.Invoke(null, active);
        }
    }
}

/// <summary>
/// Interface for tracking dashboard state
/// </summary>
public interface IDashboardStateProvider
{
    bool IsDashboardActive { get; }
    event EventHandler<bool> DashboardStateChanged;
    void SetDashboardActive(bool active);
}

/// <summary>
/// Default implementation of dashboard state provider
/// </summary>
public class DashboardStateProvider : IDashboardStateProvider
{
    public bool IsDashboardActive => DashboardState.IsActive;
    public event EventHandler<bool> DashboardStateChanged
    {
        add => DashboardState.StateChanged += value;
        remove => DashboardState.StateChanged -= value;
    }

    public void SetDashboardActive(bool active)
    {
        DashboardState.SetActive(active);
    }
}