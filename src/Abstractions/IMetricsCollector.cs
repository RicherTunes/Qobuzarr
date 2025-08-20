using System;
using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.Abstractions
{
    /// <summary>
    /// Interface for metrics collection and observability.
    /// Addresses architectural debt identified in PR #4 assessment.
    /// </summary>
    public interface IMetricsCollector
    {
        /// <summary>
        /// Record a counter metric (monotonically increasing value).
        /// </summary>
        /// <param name="name">Metric name</param>
        /// <param name="value">Value to add to counter</param>
        /// <param name="tags">Optional tags for metric categorization</param>
        void RecordCounter(string name, double value = 1.0, Dictionary<string, string> tags = null);
        
        /// <summary>
        /// Record a gauge metric (current state value).
        /// </summary>
        /// <param name="name">Metric name</param>
        /// <param name="value">Current value</param>
        /// <param name="tags">Optional tags for metric categorization</param>
        void RecordGauge(string name, double value, Dictionary<string, string> tags = null);
        
        /// <summary>
        /// Record a histogram metric (distribution of values).
        /// </summary>
        /// <param name="name">Metric name</param>
        /// <param name="value">Value to record</param>
        /// <param name="tags">Optional tags for metric categorization</param>
        void RecordHistogram(string name, double value, Dictionary<string, string> tags = null);
        
        /// <summary>
        /// Record operation timing with automatic histogram.
        /// </summary>
        /// <param name="operationName">Name of operation being timed</param>
        /// <param name="duration">Duration of operation</param>
        /// <param name="tags">Optional tags for metric categorization</param>
        void RecordTiming(string operationName, TimeSpan duration, Dictionary<string, string> tags = null);
        
        /// <summary>
        /// Start timing an operation (returns disposable for using pattern).
        /// </summary>
        /// <param name="operationName">Name of operation to time</param>
        /// <param name="tags">Optional tags for metric categorization</param>
        /// <returns>Disposable timing scope</returns>
        IDisposable StartTiming(string operationName, Dictionary<string, string> tags = null);
        
        /// <summary>
        /// Get current metrics summary for monitoring.
        /// </summary>
        /// <returns>Current metrics state</returns>
        MetricsSummary GetMetricsSummary();
    }
    
    /// <summary>
    /// Summary of current metrics state
    /// </summary>
    public class MetricsSummary
    {
        public Dictionary<string, double> Counters { get; set; } = new();
        public Dictionary<string, double> Gauges { get; set; } = new();
        public Dictionary<string, HistogramData> Histograms { get; set; } = new();
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Histogram data for distribution metrics
    /// </summary>
    public class HistogramData
    {
        public double Count { get; set; }
        public double Sum { get; set; }
        public double Average => Count > 0 ? Sum / Count : 0;
        public double Min { get; set; } = double.MaxValue;
        public double Max { get; set; } = double.MinValue;
    }
}