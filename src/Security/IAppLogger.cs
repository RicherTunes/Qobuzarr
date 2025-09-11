using System;

namespace Lidarr.Plugin.Qobuzarr.Security
{
    // Thin logging abstraction to make logging testable without mocking vendor classes
    public interface IAppLogger
    {
        void Info(string message, params object[] args);
        void Debug(string message, params object[] args);
        void Debug(Exception ex, string message, params object[] args);
        void Warn(string message, params object[] args);
        void Warn(Exception ex, string message, params object[] args);
        void Error(Exception ex, string message, params object[] args);
    }
}

