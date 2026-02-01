using System;

namespace Lidarr.Plugin.Qobuzarr.Abstractions
{
    /// <summary>
    /// Simple logging interface that both Lidarr and CLI can implement
    /// </summary>
    public interface IQobuzLogger
    {
        void Debug(string message, params object[] args);
        void Info(string message, params object[] args);
        void Warn(string message, params object[] args);
        void Warn(Exception ex, string message, params object[] args);
        void Error(string message, params object[] args);
        void Error(Exception ex, string message, params object[] args);
    }
}
