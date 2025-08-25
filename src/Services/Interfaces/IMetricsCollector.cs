using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.Services.Interfaces
{
    /// <summary>
    /// Interface for collecting and exposing application metrics.
    /// </summary>
    /// <remarks>
    /// This interface provides metrics collection capabilities compatible with
    /// monitoring systems like Prometheus, Grafana, and other observability tools.
    /// 
    /// Key Features:
    /// - Counter metrics for event counting
    /// - Gauge metrics for current values
    /// - Histogram metrics for distribution tracking
    /// - Custom metrics with labels
    /// - Prometheus-compatible export format
    /// 
    /// Metrics help monitor plugin performance, API usage, error rates,
    /// and other operational characteristics.
    /// </remarks>
    public interface IMetricsCollector
    {
        /// <summary>
        /// Increments a counter metric.
        /// </summary>
        /// <param name="name">The metric name</param>
        /// <param name="value">The value to add (default 1)</param>
        /// <param name="labels">Optional labels for the metric</param>
        void IncrementCounter(string name, double value = 1, Dictionary<string, string>? labels = null);

        /// <summary>
        /// Sets a gauge metric value.
        /// </summary>
        /// <param name="name">The metric name</param>
        /// <param name="value">The current value</param>
        /// <param name="labels">Optional labels for the metric</param>
        void SetGauge(string name, double value, Dictionary<string, string>? labels = null);

        /// <summary>
        /// Records a value in a histogram metric.
        /// </summary>
        /// <param name="name">The metric name</param>
        /// <param name="value">The value to record</param>
        /// <param name="labels">Optional labels for the metric</param>
        void RecordHistogram(string name, double value, Dictionary<string, string>? labels = null);

        /// <summary>
        /// Records an operation duration.
        /// </summary>
        /// <param name="name">The operation name</param>
        /// <param name="duration">The operation duration</param>
        /// <param name="labels">Optional labels for the metric</param>
        void RecordDuration(string name, System.TimeSpan duration, Dictionary<string, string>? labels = null);

        /// <summary>
        /// Records an operation result (success/failure).
        /// </summary>
        /// <param name="operation">The operation name</param>
        /// <param name="success">Whether the operation succeeded</param>
        /// <param name="duration">The operation duration</param>
        /// <param name="labels">Optional labels for the metric</param>
        void RecordOperation(string operation, bool success, System.TimeSpan duration, Dictionary<string, string>? labels = null);

        /// <summary>
        /// Gets all collected metrics in Prometheus format.
        /// </summary>
        /// <returns>Metrics in Prometheus exposition format</returns>
        string GetPrometheusMetrics();

        /// <summary>
        /// Gets metrics summary as key-value pairs.
        /// </summary>
        /// <returns>Dictionary of metric names to current values</returns>
        Dictionary<string, object> GetMetricsSummary();

        /// <summary>
        /// Resets all metrics to zero/empty.
        /// </summary>
        void ResetMetrics();

        /// <summary>
        /// Gets the metrics collection start time.
        /// </summary>
        /// <returns>When metrics collection started</returns>
        System.DateTime GetMetricsStartTime();

        /// <summary>
        /// Gets legacy metrics summary for backward compatibility.
        /// </summary>
        /// <returns>Legacy format metrics summary</returns>
        Dictionary<string, string> GetLegacyMetricsSummary();
    }
}