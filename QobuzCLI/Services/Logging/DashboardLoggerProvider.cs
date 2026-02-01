using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;

namespace QobuzCLI.Services.Logging;

/// <summary>
/// Logger provider that wraps existing loggers with dashboard integration
/// </summary>
public class DashboardLoggerProvider : ILoggerProvider
{
    private readonly ILoggerProvider _innerProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, ILogger> _loggers = new();

    public DashboardLoggerProvider(ILoggerProvider innerProvider, IServiceProvider serviceProvider)
    {
        _innerProvider = innerProvider;
        _serviceProvider = serviceProvider;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name =>
        {
            var innerLogger = _innerProvider.CreateLogger(name);

            // Try to get dashboard from service provider
            // It might not be available during startup
            var dashboard = _serviceProvider.GetService<Dashboard>();
            if (dashboard == null)
            {
                // Return inner logger if dashboard not available
                return innerLogger;
            }

            // Return wrapped logger with dashboard integration
            return new DashboardLogger(innerLogger, dashboard, name);
        });
    }

    public void Dispose()
    {
        _loggers.Clear();
        _innerProvider?.Dispose();
        GC.SuppressFinalize(this);
    }
}
