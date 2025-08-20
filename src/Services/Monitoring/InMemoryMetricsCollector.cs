using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NLog;
using Lidarr.Plugin.Qobuzarr.Abstractions;

namespace Lidarr.Plugin.Qobuzarr.Services.Monitoring
{
    /// <summary>
    /// In-memory metrics collector for observability.
    /// Provides missing metrics abstraction identified in technical debt assessment.
    /// </summary>
    public class InMemoryMetricsCollector : IMetricsCollector
    {
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, double> _counters;
        private readonly ConcurrentDictionary<string, double> _gauges;
        private readonly ConcurrentDictionary<string, HistogramData> _histograms;

        public InMemoryMetricsCollector(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _counters = new ConcurrentDictionary<string, double>();
            _gauges = new ConcurrentDictionary<string, double>();
            _histograms = new ConcurrentDictionary<string, HistogramData>();
        }

        public void RecordCounter(string name, double value = 1.0, Dictionary<string, string>? tags = null)
        {
            _counters.AddOrUpdate(name, value, (key, existing) => existing + value);
            _logger.Trace("Counter {0} incremented by {1}, total: {2}", name, value, _counters[name]);
        }

        public void RecordGauge(string name, double value, Dictionary<string, string>? tags = null)
        {
            _gauges.AddOrUpdate(name, value, (key, existing) => value);
            _logger.Trace("Gauge {0} set to {1}", name, value);
        }

        public void RecordHistogram(string name, double value, Dictionary<string, string>? tags = null)
        {
            _histograms.AddOrUpdate(name, 
                new HistogramData { Count = 1, Sum = value, Min = value, Max = value },
                (key, existing) =>
                {
                    existing.Count++;
                    existing.Sum += value;
                    if (value < existing.Min) existing.Min = value;
                    if (value > existing.Max) existing.Max = value;
                    return existing;
                });
            _logger.Trace("Histogram {0} recorded value {1}", name, value);
        }

        public void RecordTiming(string operationName, TimeSpan duration, Dictionary<string, string>? tags = null)
        {
            RecordHistogram($"timing.{operationName}", duration.TotalMilliseconds, tags);
        }

        public IDisposable StartTiming(string operationName, Dictionary<string, string>? tags = null)
        {
            return new TimingScope(this, operationName, tags);
        }

        public MetricsSummary GetMetricsSummary()
        {
            return new MetricsSummary
            {
                Counters = new Dictionary<string, double>(_counters),
                Gauges = new Dictionary<string, double>(_gauges),
                Histograms = new Dictionary<string, HistogramData>(_histograms),
                LastUpdated = DateTime.UtcNow
            };
        }

        private class TimingScope : IDisposable
        {
            private readonly InMemoryMetricsCollector _collector;
            private readonly string _operationName;
            private readonly Dictionary<string, string>? _tags;
            private readonly Stopwatch _stopwatch;
            private bool _disposed = false;

            public TimingScope(InMemoryMetricsCollector collector, string operationName, Dictionary<string, string>? tags)
            {
                _collector = collector;
                _operationName = operationName;
                _tags = tags;
                _stopwatch = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _stopwatch.Stop();
                    _collector.RecordTiming(_operationName, _stopwatch.Elapsed, _tags);
                    _disposed = true;
                }
            }
        }
    }
}