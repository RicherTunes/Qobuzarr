using Microsoft.Extensions.Logging;
using Lidarr.Plugin.Qobuzarr.Abstractions;

namespace QobuzCLI.Services;

/// <summary>
/// CLI implementation of IQobuzLogger using Microsoft.Extensions.Logging
/// </summary>
public class CliLogger : IQobuzLogger
{
    private readonly ILogger _logger;

    public CliLogger(ILogger logger)
    {
        _logger = logger;
    }

    public void Debug(string message, params object[] args)
    {
        _logger.LogDebug(message, args);
    }

    public void Info(string message, params object[] args)
    {
        _logger.LogInformation(message, args);
    }

    public void Warn(string message, params object[] args)
    {
        _logger.LogWarning(message, args);
    }

    public void Warn(Exception ex, string message, params object[] args)
    {
        _logger.LogWarning(ex, message, args);
    }

    public void Error(string message, params object[] args)
    {
        _logger.LogError(message, args);
    }

    public void Error(Exception ex, string message, params object[] args)
    {
        _logger.LogError(ex, message, args);
    }
}