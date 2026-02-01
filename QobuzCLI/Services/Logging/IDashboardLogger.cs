using Microsoft.Extensions.Logging;

namespace QobuzCLI.Services.Logging;

/// <summary>
/// Extended logger interface that supports direct dashboard logging
/// </summary>
public interface IDashboardLogger : ILogger
{
    /// <summary>
    /// Logs a message directly to the dashboard display
    /// </summary>
    void LogToDashboard(string message, LogLevel level = LogLevel.Information);

    /// <summary>
    /// Logs progress update information to the dashboard
    /// </summary>
    void LogProgressUpdate(string operation, int current, int total);
}
