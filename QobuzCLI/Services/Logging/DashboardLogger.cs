using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using Spectre.Console;

namespace QobuzCLI.Services.Logging;

/// <summary>
/// Logger implementation that sends logs to both file and dashboard
/// </summary>
public class DashboardLogger<T> : ILogger<T>, IDashboardLogger
{
    private readonly ILogger<T> _innerLogger;
    private readonly IDashboard? _dashboard;
    private readonly string _categoryName;
    
    public DashboardLogger(ILogger<T> innerLogger, IDashboard? dashboard)
    {
        _innerLogger = innerLogger;
        _dashboard = dashboard;
        _categoryName = typeof(T).Name;
    }
    
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, 
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        // Always log to file via inner logger
        _innerLogger.Log(logLevel, eventId, state, exception, formatter);
        
        // Send important messages to dashboard if active
        if (_dashboard != null && _dashboard.IsActive)
        {
            if (ShouldLogToDashboard(logLevel))
            {
                var message = formatter(state, exception);
                var formattedMessage = FormatForDashboard(logLevel, message, exception);
                _dashboard.AddLogMessage(formattedMessage);
            }
        }
    }
    
    private bool ShouldLogToDashboard(LogLevel logLevel)
    {
        // Show warnings, errors, and critical messages
        // Also show info messages from specific categories
        return logLevel >= LogLevel.Warning || 
               (_categoryName.Contains("Download") && logLevel >= LogLevel.Information);
    }
    
    private string FormatForDashboard(LogLevel logLevel, string message, Exception exception)
    {
        var icon = logLevel switch
        {
            LogLevel.Critical => "🔴",
            LogLevel.Error => "❌",
            LogLevel.Warning => "⚠️",
            LogLevel.Information => "ℹ️",
            _ => "📝"
        };
        
        var color = logLevel switch
        {
            LogLevel.Critical => "red",
            LogLevel.Error => "red",
            LogLevel.Warning => "yellow",
            LogLevel.Information => "blue",
            _ => "dim"
        };
        
        var baseMessage = $"{icon} [{color}]{message}[/]";
        
        if (exception != null)
        {
            baseMessage += $" [dim]({exception.GetType().Name})[/]";
        }
        
        return baseMessage;
    }
    
    public void LogToDashboard(string message, LogLevel level = LogLevel.Information)
    {
        if (_dashboard != null && _dashboard.IsActive)
        {
            var formattedMessage = FormatForDashboard(level, message, null);
            _dashboard.AddLogMessage(formattedMessage);
        }
        else
        {
            // If dashboard not active, show important messages in console
            if (level >= LogLevel.Information)
            {
                var formattedMessage = FormatForConsole(level, message);
                AnsiConsole.MarkupLine(formattedMessage);
            }
        }
    }
    
    public void LogProgressUpdate(string operation, int current, int total)
    {
        var message = $"Progress: {operation} ({current}/{total})";
        LogToDashboard(message, LogLevel.Information);
    }
    
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _innerLogger.BeginScope(state);
    public bool IsEnabled(LogLevel logLevel) => _innerLogger.IsEnabled(logLevel);
    
    private string FormatForConsole(LogLevel logLevel, string message)
    {
        var icon = logLevel switch
        {
            LogLevel.Critical => "🔴",
            LogLevel.Error => "❌",
            LogLevel.Warning => "⚠️",
            LogLevel.Information => "ℹ️",
            _ => "📝"
        };
        
        var color = logLevel switch
        {
            LogLevel.Critical => "red",
            LogLevel.Error => "red",
            LogLevel.Warning => "yellow",
            LogLevel.Information => "cyan",
            _ => "dim"
        };
        
        return $"{icon} [{color}]{message}[/]";
    }
}

/// <summary>
/// Non-generic version for cases where we don't have a category type
/// </summary>
public class DashboardLogger : ILogger, IDashboardLogger
{
    private readonly ILogger _innerLogger;
    private readonly Dashboard _dashboard;
    private readonly string _categoryName;
    
    public DashboardLogger(ILogger innerLogger, Dashboard dashboard, string categoryName)
    {
        _innerLogger = innerLogger;
        _dashboard = dashboard;
        _categoryName = categoryName;
    }
    
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, 
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        // Always log to file via inner logger
        _innerLogger.Log(logLevel, eventId, state, exception, formatter);
        
        // Send important messages to dashboard if active
        if (_dashboard != null && _dashboard.IsActive)
        {
            if (ShouldLogToDashboard(logLevel))
            {
                var message = formatter(state, exception);
                var formattedMessage = FormatForDashboard(logLevel, message, exception);
                _dashboard.AddLogMessage(formattedMessage);
            }
        }
    }
    
    private bool ShouldLogToDashboard(LogLevel logLevel)
    {
        // Show warnings, errors, and critical messages
        // Also show info messages from download-related categories
        return logLevel >= LogLevel.Warning || 
               (_categoryName.Contains("Download") && logLevel >= LogLevel.Information) ||
               (_categoryName.Contains("Qobuz") && logLevel >= LogLevel.Information);
    }
    
    private string FormatForDashboard(LogLevel logLevel, string message, Exception exception)
    {
        var icon = logLevel switch
        {
            LogLevel.Critical => "🔴",
            LogLevel.Error => "❌",
            LogLevel.Warning => "⚠️",
            LogLevel.Information => "ℹ️",
            _ => "📝"
        };
        
        var color = logLevel switch
        {
            LogLevel.Critical => "red",
            LogLevel.Error => "red",
            LogLevel.Warning => "yellow",
            LogLevel.Information => "blue",
            _ => "dim"
        };
        
        var baseMessage = $"{icon} [{color}]{message}[/]";
        
        if (exception != null)
        {
            baseMessage += $" [dim]({exception.GetType().Name})[/]";
        }
        
        return baseMessage;
    }
    
    public void LogToDashboard(string message, LogLevel level = LogLevel.Information)
    {
        if (_dashboard != null && _dashboard.IsActive)
        {
            var formattedMessage = FormatForDashboard(level, message, null);
            _dashboard.AddLogMessage(formattedMessage);
        }
        else
        {
            // If dashboard not active, show important messages in console
            if (level >= LogLevel.Information)
            {
                var formattedMessage = FormatForConsole(level, message);
                AnsiConsole.MarkupLine(formattedMessage);
            }
        }
    }
    
    public void LogProgressUpdate(string operation, int current, int total)
    {
        var message = $"Progress: {operation} ({current}/{total})";
        LogToDashboard(message, LogLevel.Information);
    }
    
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _innerLogger.BeginScope(state);
    public bool IsEnabled(LogLevel logLevel) => _innerLogger.IsEnabled(logLevel);
    
    private string FormatForConsole(LogLevel logLevel, string message)
    {
        var icon = logLevel switch
        {
            LogLevel.Critical => "🔴",
            LogLevel.Error => "❌",
            LogLevel.Warning => "⚠️",
            LogLevel.Information => "ℹ️",
            _ => "📝"
        };
        
        var color = logLevel switch
        {
            LogLevel.Critical => "red",
            LogLevel.Error => "red",
            LogLevel.Warning => "yellow",
            LogLevel.Information => "cyan",
            _ => "dim"
        };
        
        return $"{icon} [{color}]{message}[/]";
    }
}