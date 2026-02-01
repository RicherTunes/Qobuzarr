using System;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Simple stub interface for performance monitoring to satisfy references.
    /// Replaced by shared library services in the refactoring.
    /// </summary>
    public interface IPerformanceMonitoringService
    {
        void RecordApiCall(string endpoint, TimeSpan duration, bool success);
        void RecordCacheHit(string key);
        void RecordCacheHit(string key, string source, TimeSpan duration, int size);
        void RecordCacheMiss(string key);
        void LogPerformanceWarning(string message);
        void RecordMLOptimization(string originalQuery, string optimizedQuery, bool assumedCorrect, double confidence);
    }

    /// <summary>
    /// No-op implementation to satisfy DI requirements.
    /// </summary>
    public class StubPerformanceMonitoringService : IPerformanceMonitoringService
    {
        public void RecordApiCall(string endpoint, TimeSpan duration, bool success) { }
        public void RecordCacheHit(string key) { }
        public void RecordCacheHit(string key, string source, TimeSpan duration, int size) { }
        public void RecordCacheMiss(string key) { }
        public void LogPerformanceWarning(string message) { }
        public void RecordMLOptimization(string originalQuery, string optimizedQuery, bool assumedCorrect, double confidence) { }
    }
}
