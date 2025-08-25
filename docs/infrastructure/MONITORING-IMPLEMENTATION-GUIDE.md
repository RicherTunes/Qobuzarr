# Qobuzarr Monitoring & Observability Implementation Guide

## Overview

This guide provides step-by-step instructions for implementing comprehensive monitoring and observability for Qobuzarr, enabling real-time validation of performance claims and proactive issue detection.

## Architecture

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│  Qobuzarr       │────►│  OpenTelemetry   │────►│  Prometheus     │
│  Plugin         │     │  Collector       │     │                 │
│  - Metrics      │     │  - Aggregation   │     │  - Storage      │
│  - Traces       │     │  - Processing    │     │  - Queries      │
│  - Logs         │     │  - Export        │     │                 │
└─────────────────┘     └──────────────────┘     └─────────────────┘
                                                           │
                                                           ▼
                                                   ┌─────────────────┐
                                                   │  Grafana        │
                                                   │  - Dashboards   │
                                                   │  - Alerts       │
                                                   │  - Reports      │
                                                   └─────────────────┘
```

## Phase 1: Metrics Collection Implementation

### 1.1 Enhanced Telemetry Service

```csharp
// src/Services/Telemetry/MetricsCollector.cs
using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Lidarr.Plugin.Qobuzarr.Services.Telemetry
{
    public interface IMetricsCollector
    {
        void RecordApiCall(string endpoint, double latencyMs, bool cached);
        void RecordCacheOperation(string cacheType, bool hit);
        void RecordMLOptimization(string queryType, bool optimized, double confidenceScore);
        void RecordDownloadOperation(string itemType, long sizeBytes, double durationMs, bool success);
        void RecordError(string operation, string errorType);
    }

    public class MetricsCollector : IMetricsCollector, IDisposable
    {
        private readonly Meter _meter;
        private readonly MeterProvider _meterProvider;
        
        // Counters
        private readonly Counter<long> _apiCallCounter;
        private readonly Counter<long> _cacheHitCounter;
        private readonly Counter<long> _cacheMissCounter;
        private readonly Counter<long> _mlOptimizationCounter;
        private readonly Counter<long> _downloadCounter;
        private readonly Counter<long> _errorCounter;
        
        // Histograms
        private readonly Histogram<double> _apiLatencyHistogram;
        private readonly Histogram<double> _downloadSpeedHistogram;
        private readonly Histogram<double> _mlConfidenceHistogram;
        
        // Observable gauges
        private readonly ObservableGauge<double> _cacheHitRatioGauge;
        private readonly ObservableGauge<double> _apiReductionGauge;
        private readonly ObservableGauge<long> _activeDownloadsGauge;
        
        // Internal metrics storage
        private long _totalApiCalls = 0;
        private long _cachedApiCalls = 0;
        private long _totalCacheOperations = 0;
        private long _cacheHits = 0;
        private long _activeDownloads = 0;
        
        public MetricsCollector(IConfiguration configuration)
        {
            _meter = new Meter("Qobuzarr", "1.0.0");
            
            // Initialize counters
            _apiCallCounter = _meter.CreateCounter<long>(
                "qobuzarr_api_calls_total",
                description: "Total number of API calls made");
            
            _cacheHitCounter = _meter.CreateCounter<long>(
                "qobuzarr_cache_hits_total",
                description: "Total number of cache hits");
            
            _cacheMissCounter = _meter.CreateCounter<long>(
                "qobuzarr_cache_misses_total",
                description: "Total number of cache misses");
            
            _mlOptimizationCounter = _meter.CreateCounter<long>(
                "qobuzarr_ml_optimizations_total",
                description: "Total number of ML-optimized queries");
            
            _downloadCounter = _meter.CreateCounter<long>(
                "qobuzarr_downloads_total",
                description: "Total number of downloads");
            
            _errorCounter = _meter.CreateCounter<long>(
                "qobuzarr_errors_total",
                description: "Total number of errors");
            
            // Initialize histograms
            _apiLatencyHistogram = _meter.CreateHistogram<double>(
                "qobuzarr_api_latency_milliseconds",
                unit: "ms",
                description: "API call latency distribution");
            
            _downloadSpeedHistogram = _meter.CreateHistogram<double>(
                "qobuzarr_download_speed_mbps",
                unit: "Mbps",
                description: "Download speed distribution");
            
            _mlConfidenceHistogram = _meter.CreateHistogram<double>(
                "qobuzarr_ml_confidence_score",
                description: "ML model confidence score distribution");
            
            // Initialize observable gauges
            _cacheHitRatioGauge = _meter.CreateObservableGauge(
                "qobuzarr_cache_hit_ratio",
                () => CalculateCacheHitRatio(),
                description: "Current cache hit ratio");
            
            _apiReductionGauge = _meter.CreateObservableGauge(
                "qobuzarr_api_reduction_percentage",
                () => CalculateApiReduction(),
                description: "API call reduction percentage");
            
            _activeDownloadsGauge = _meter.CreateObservableGauge(
                "qobuzarr_active_downloads",
                () => Interlocked.Read(ref _activeDownloads),
                description: "Number of active downloads");
            
            // Configure OpenTelemetry exporter
            _meterProvider = ConfigureOpenTelemetry(configuration);
        }
        
        private MeterProvider ConfigureOpenTelemetry(IConfiguration configuration)
        {
            var builder = Sdk.CreateMeterProviderBuilder()
                .AddMeter("Qobuzarr")
                .SetResourceBuilder(
                    ResourceBuilder.CreateDefault()
                        .AddService("Qobuzarr", serviceVersion: "1.0.0")
                        .AddTelemetrySdk()
                        .AddAttributes(new Dictionary<string, object>
                        {
                            ["deployment.environment"] = configuration["Environment"] ?? "production",
                            ["host.name"] = Environment.MachineName
                        }));
            
            // Add exporters based on configuration
            var exporterType = configuration["Telemetry:Exporter"] ?? "otlp";
            
            switch (exporterType.ToLower())
            {
                case "otlp":
                    builder.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(configuration["Telemetry:OtlpEndpoint"] ?? "http://localhost:4317");
                        options.Protocol = OtlpExportProtocol.Grpc;
                        options.ExportProcessorType = ExportProcessorType.Batch;
                        options.BatchExportProcessorOptions = new BatchExportProcessorOptions<Activity>
                        {
                            MaxQueueSize = 2048,
                            MaxExportBatchSize = 512,
                            ExporterTimeoutMilliseconds = 30000
                        };
                    });
                    break;
                    
                case "prometheus":
                    builder.AddPrometheusHttpListener(
                        options => options.UriPrefixes = new[] { "http://localhost:9090/" });
                    break;
                    
                case "console":
                    builder.AddConsoleExporter();
                    break;
            }
            
            return builder.Build();
        }
        
        public void RecordApiCall(string endpoint, double latencyMs, bool cached)
        {
            Interlocked.Increment(ref _totalApiCalls);
            if (cached) Interlocked.Increment(ref _cachedApiCalls);
            
            var tags = new TagList
            {
                { "endpoint", endpoint },
                { "cached", cached }
            };
            
            _apiCallCounter.Add(1, tags);
            _apiLatencyHistogram.Record(latencyMs, tags);
        }
        
        public void RecordCacheOperation(string cacheType, bool hit)
        {
            Interlocked.Increment(ref _totalCacheOperations);
            if (hit) Interlocked.Increment(ref _cacheHits);
            
            var tags = new TagList { { "cache_type", cacheType } };
            
            if (hit)
                _cacheHitCounter.Add(1, tags);
            else
                _cacheMissCounter.Add(1, tags);
        }
        
        public void RecordMLOptimization(string queryType, bool optimized, double confidenceScore)
        {
            var tags = new TagList
            {
                { "query_type", queryType },
                { "optimized", optimized }
            };
            
            if (optimized)
            {
                _mlOptimizationCounter.Add(1, tags);
                _mlConfidenceHistogram.Record(confidenceScore, tags);
            }
        }
        
        public void RecordDownloadOperation(string itemType, long sizeBytes, double durationMs, bool success)
        {
            if (success)
            {
                var speedMbps = (sizeBytes * 8.0) / (durationMs * 1000.0);
                
                var tags = new TagList
                {
                    { "item_type", itemType },
                    { "success", success }
                };
                
                _downloadCounter.Add(1, tags);
                _downloadSpeedHistogram.Record(speedMbps, tags);
            }
        }
        
        public void RecordError(string operation, string errorType)
        {
            var tags = new TagList
            {
                { "operation", operation },
                { "error_type", errorType }
            };
            
            _errorCounter.Add(1, tags);
        }
        
        private double CalculateCacheHitRatio()
        {
            var total = Interlocked.Read(ref _totalCacheOperations);
            if (total == 0) return 0;
            
            var hits = Interlocked.Read(ref _cacheHits);
            return (double)hits / total;
        }
        
        private double CalculateApiReduction()
        {
            var total = Interlocked.Read(ref _totalApiCalls);
            if (total == 0) return 0;
            
            var cached = Interlocked.Read(ref _cachedApiCalls);
            return ((double)cached / total) * 100;
        }
        
        public void Dispose()
        {
            _meterProvider?.Dispose();
        }
    }
}
```

### 1.2 Integration with Existing Services

```csharp
// src/Services/QobuzApiClientWithMetrics.cs
public class QobuzApiClientWithMetrics : IQobuzApiClient
{
    private readonly IQobuzApiClient _innerClient;
    private readonly IMetricsCollector _metrics;
    private readonly ILogger<QobuzApiClientWithMetrics> _logger;
    
    public QobuzApiClientWithMetrics(
        IQobuzApiClient innerClient,
        IMetricsCollector metrics,
        ILogger<QobuzApiClientWithMetrics> logger)
    {
        _innerClient = innerClient;
        _metrics = metrics;
        _logger = logger;
    }
    
    public async Task<T> GetAsync<T>(string endpoint, Dictionary<string, string> parameters = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var cached = false;
        
        try
        {
            // Check if this is a cached response
            if (parameters?.ContainsKey("from_cache") == true)
            {
                cached = true;
                parameters.Remove("from_cache");
            }
            
            var result = await _innerClient.GetAsync<T>(endpoint, parameters);
            
            stopwatch.Stop();
            _metrics.RecordApiCall(endpoint, stopwatch.ElapsedMilliseconds, cached);
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metrics.RecordError("api_call", ex.GetType().Name);
            throw;
        }
    }
}
```

## Phase 2: Infrastructure Setup

### 2.1 Docker Compose Configuration

```yaml
# docker-compose.monitoring.yml
version: '3.8'

networks:
  monitoring:
    driver: bridge

services:
  # OpenTelemetry Collector
  otel-collector:
    image: otel/opentelemetry-collector-contrib:latest
    container_name: otel-collector
    networks:
      - monitoring
    volumes:
      - ./config/otel-collector.yaml:/etc/otel-collector.yaml
    command: ["--config=/etc/otel-collector.yaml"]
    ports:
      - "4317:4317"   # OTLP gRPC
      - "4318:4318"   # OTLP HTTP
      - "8888:8888"   # Prometheus metrics
      - "8889:8889"   # Prometheus exporter
    restart: unless-stopped
  
  # Prometheus
  prometheus:
    image: prom/prometheus:latest
    container_name: prometheus
    networks:
      - monitoring
    volumes:
      - ./config/prometheus.yml:/etc/prometheus/prometheus.yml
      - prometheus-data:/prometheus
    command:
      - '--config.file=/etc/prometheus/prometheus.yml'
      - '--storage.tsdb.retention.time=90d'
      - '--storage.tsdb.retention.size=10GB'
      - '--web.enable-lifecycle'
    ports:
      - "9090:9090"
    restart: unless-stopped
  
  # Grafana
  grafana:
    image: grafana/grafana:latest
    container_name: grafana
    networks:
      - monitoring
    volumes:
      - ./config/grafana/provisioning:/etc/grafana/provisioning
      - ./config/grafana/dashboards:/var/lib/grafana/dashboards
      - grafana-data:/var/lib/grafana
    environment:
      - GF_SECURITY_ADMIN_USER=admin
      - GF_SECURITY_ADMIN_PASSWORD=admin
      - GF_INSTALL_PLUGINS=redis-datasource,grafana-piechart-panel
      - GF_DASHBOARDS_DEFAULT_HOME_DASHBOARD_PATH=/var/lib/grafana/dashboards/qobuzarr-overview.json
    ports:
      - "3000:3000"
    restart: unless-stopped
    depends_on:
      - prometheus
  
  # Alertmanager (optional)
  alertmanager:
    image: prom/alertmanager:latest
    container_name: alertmanager
    networks:
      - monitoring
    volumes:
      - ./config/alertmanager.yml:/etc/alertmanager/alertmanager.yml
      - alertmanager-data:/alertmanager
    command:
      - '--config.file=/etc/alertmanager/alertmanager.yml'
      - '--storage.path=/alertmanager'
    ports:
      - "9093:9093"
    restart: unless-stopped

volumes:
  prometheus-data:
  grafana-data:
  alertmanager-data:
```

### 2.2 OpenTelemetry Collector Configuration

```yaml
# config/otel-collector.yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
      http:
        endpoint: 0.0.0.0:4318
  
  prometheus:
    config:
      scrape_configs:
        - job_name: 'qobuzarr'
          scrape_interval: 15s
          static_configs:
            - targets: ['host.docker.internal:9090']

processors:
  batch:
    timeout: 10s
    send_batch_size: 1024
  
  memory_limiter:
    check_interval: 1s
    limit_mib: 512
    spike_limit_mib: 128
  
  resource:
    attributes:
      - key: service.name
        value: qobuzarr
        action: upsert
      - key: deployment.environment
        from_attribute: env
        action: insert

exporters:
  prometheus:
    endpoint: "0.0.0.0:8889"
    namespace: qobuzarr
    const_labels:
      environment: production
  
  logging:
    loglevel: info
    sampling_initial: 10
    sampling_thereafter: 100

service:
  pipelines:
    metrics:
      receivers: [otlp, prometheus]
      processors: [memory_limiter, batch, resource]
      exporters: [prometheus, logging]
    
    traces:
      receivers: [otlp]
      processors: [memory_limiter, batch, resource]
      exporters: [logging]
    
    logs:
      receivers: [otlp]
      processors: [memory_limiter, batch]
      exporters: [logging]

  extensions: [health_check, pprof, zpages]
```

### 2.3 Prometheus Configuration

```yaml
# config/prometheus.yml
global:
  scrape_interval: 15s
  evaluation_interval: 15s
  external_labels:
    monitor: 'qobuzarr-monitor'

alerting:
  alertmanagers:
    - static_configs:
        - targets: ['alertmanager:9093']

rule_files:
  - '/etc/prometheus/alerts/*.yml'

scrape_configs:
  # OpenTelemetry Collector metrics
  - job_name: 'otel-collector'
    static_configs:
      - targets: ['otel-collector:8889']
  
  # Direct plugin metrics (if exposed)
  - job_name: 'qobuzarr-direct'
    static_configs:
      - targets: ['lidarr:9091']
    metric_relabel_configs:
      - source_labels: [__name__]
        regex: 'qobuzarr_.*'
        action: keep
  
  # Lidarr host metrics
  - job_name: 'lidarr'
    static_configs:
      - targets: ['lidarr:8686']
    metrics_path: '/api/v1/system/status'
    params:
      apikey: ['your-api-key-here']
```

## Phase 3: Grafana Dashboards

### 3.1 Main Performance Dashboard

```json
{
  "dashboard": {
    "id": null,
    "uid": "qobuzarr-main",
    "title": "Qobuzarr Performance Overview",
    "tags": ["qobuzarr", "performance"],
    "timezone": "browser",
    "refresh": "10s",
    "panels": [
      {
        "id": 1,
        "title": "API Call Reduction",
        "type": "stat",
        "gridPos": {"h": 6, "w": 8, "x": 0, "y": 0},
        "targets": [{
          "expr": "qobuzarr_api_reduction_percentage",
          "legendFormat": "API Reduction"
        }],
        "fieldConfig": {
          "defaults": {
            "mappings": [],
            "thresholds": {
              "mode": "absolute",
              "steps": [
                {"color": "red", "value": null},
                {"color": "yellow", "value": 50},
                {"color": "green", "value": 65.8}
              ]
            },
            "unit": "percent",
            "decimals": 1
          }
        },
        "options": {
          "reduceOptions": {
            "values": false,
            "fields": "",
            "calcs": ["lastNotNull"]
          },
          "orientation": "auto",
          "textMode": "auto",
          "colorMode": "background",
          "graphMode": "area",
          "justifyMode": "auto"
        }
      },
      {
        "id": 2,
        "title": "Cache Hit Rate",
        "type": "gauge",
        "gridPos": {"h": 6, "w": 8, "x": 8, "y": 0},
        "targets": [{
          "expr": "qobuzarr_cache_hit_ratio * 100",
          "legendFormat": "Hit Rate"
        }],
        "fieldConfig": {
          "defaults": {
            "mappings": [],
            "thresholds": {
              "mode": "absolute",
              "steps": [
                {"color": "red", "value": null},
                {"color": "yellow", "value": 80},
                {"color": "green", "value": 94.7}
              ]
            },
            "unit": "percent",
            "min": 0,
            "max": 100,
            "decimals": 1
          }
        }
      },
      {
        "id": 3,
        "title": "ML Optimization Rate",
        "type": "stat",
        "gridPos": {"h": 6, "w": 8, "x": 16, "y": 0},
        "targets": [{
          "expr": "sum(rate(qobuzarr_ml_optimizations_total[5m])) / sum(rate(qobuzarr_api_calls_total[5m])) * 100",
          "legendFormat": "ML Optimization"
        }],
        "fieldConfig": {
          "defaults": {
            "mappings": [],
            "thresholds": {
              "mode": "absolute",
              "steps": [
                {"color": "red", "value": null},
                {"color": "yellow", "value": 70},
                {"color": "green", "value": 87.3}
              ]
            },
            "unit": "percent",
            "decimals": 1
          }
        }
      },
      {
        "id": 4,
        "title": "API Latency (P50, P95, P99)",
        "type": "timeseries",
        "gridPos": {"h": 8, "w": 12, "x": 0, "y": 6},
        "targets": [
          {
            "expr": "histogram_quantile(0.5, rate(qobuzarr_api_latency_milliseconds_bucket[5m]))",
            "legendFormat": "P50"
          },
          {
            "expr": "histogram_quantile(0.95, rate(qobuzarr_api_latency_milliseconds_bucket[5m]))",
            "legendFormat": "P95"
          },
          {
            "expr": "histogram_quantile(0.99, rate(qobuzarr_api_latency_milliseconds_bucket[5m]))",
            "legendFormat": "P99"
          }
        ],
        "fieldConfig": {
          "defaults": {
            "custom": {
              "drawStyle": "line",
              "lineInterpolation": "smooth",
              "lineWidth": 2,
              "fillOpacity": 10,
              "showPoints": "never"
            },
            "unit": "ms",
            "decimals": 0
          }
        }
      },
      {
        "id": 5,
        "title": "Download Speed Distribution",
        "type": "heatmap",
        "gridPos": {"h": 8, "w": 12, "x": 12, "y": 6},
        "targets": [{
          "expr": "sum(increase(qobuzarr_download_speed_mbps_bucket[1m])) by (le)",
          "format": "heatmap",
          "legendFormat": "{{le}}"
        }],
        "options": {
          "calculate": false,
          "cellGap": 1,
          "color": {
            "scheme": "Spectral",
            "mode": "scheme"
          },
          "yAxis": {
            "unit": "Mbps",
            "decimals": 0
          }
        }
      },
      {
        "id": 6,
        "title": "Error Rate by Type",
        "type": "piechart",
        "gridPos": {"h": 8, "w": 8, "x": 0, "y": 14},
        "targets": [{
          "expr": "sum(rate(qobuzarr_errors_total[5m])) by (error_type)",
          "legendFormat": "{{error_type}}"
        }],
        "options": {
          "pieType": "donut",
          "displayLabels": ["name", "percent"],
          "legendDisplayMode": "table",
          "legendPlacement": "right"
        }
      },
      {
        "id": 7,
        "title": "Active Downloads",
        "type": "stat",
        "gridPos": {"h": 4, "w": 4, "x": 8, "y": 14},
        "targets": [{
          "expr": "qobuzarr_active_downloads",
          "legendFormat": "Active"
        }],
        "fieldConfig": {
          "defaults": {
            "mappings": [],
            "thresholds": {
              "mode": "absolute",
              "steps": [
                {"color": "green", "value": null},
                {"color": "yellow", "value": 10},
                {"color": "red", "value": 20}
              ]
            },
            "unit": "short"
          }
        }
      },
      {
        "id": 8,
        "title": "Total API Calls (24h)",
        "type": "stat",
        "gridPos": {"h": 4, "w": 4, "x": 12, "y": 14},
        "targets": [{
          "expr": "increase(qobuzarr_api_calls_total[24h])",
          "legendFormat": "Total"
        }],
        "fieldConfig": {
          "defaults": {
            "mappings": [],
            "unit": "short",
            "decimals": 0
          }
        }
      }
    ]
  }
}
```

## Phase 4: Alerting Rules

### 4.1 Prometheus Alert Rules

```yaml
# config/prometheus/alerts/qobuzarr.yml
groups:
  - name: qobuzarr_performance
    interval: 30s
    rules:
      - alert: LowApiReduction
        expr: qobuzarr_api_reduction_percentage < 50
        for: 5m
        labels:
          severity: warning
          component: qobuzarr
        annotations:
          summary: "API reduction below target (current: {{ $value }}%)"
          description: "API reduction is {{ $value }}%, target is 65.8%"
      
      - alert: LowCacheHitRate
        expr: qobuzarr_cache_hit_ratio < 0.8
        for: 5m
        labels:
          severity: warning
          component: qobuzarr
        annotations:
          summary: "Cache hit rate below threshold (current: {{ $value | humanizePercentage }})"
          description: "Cache hit rate is {{ $value | humanizePercentage }}, target is 94.7%"
      
      - alert: HighApiLatency
        expr: histogram_quantile(0.95, rate(qobuzarr_api_latency_milliseconds_bucket[5m])) > 1000
        for: 5m
        labels:
          severity: critical
          component: qobuzarr
        annotations:
          summary: "High API latency detected (P95: {{ $value }}ms)"
          description: "95th percentile API latency is {{ $value }}ms"
      
      - alert: HighErrorRate
        expr: sum(rate(qobuzarr_errors_total[5m])) > 0.1
        for: 2m
        labels:
          severity: critical
          component: qobuzarr
        annotations:
          summary: "High error rate detected ({{ $value | humanize }} errors/sec)"
          description: "Error rate is {{ $value | humanize }} errors per second"
```

## Phase 5: Production Deployment

### 5.1 Deployment Script

```bash
#!/bin/bash
# scripts/deploy-monitoring.sh

set -e

echo "🚀 Deploying Qobuzarr Monitoring Stack"

# Check prerequisites
command -v docker >/dev/null 2>&1 || { echo "Docker required but not installed. Aborting." >&2; exit 1; }
command -v docker-compose >/dev/null 2>&1 || { echo "Docker Compose required but not installed. Aborting." >&2; exit 1; }

# Create necessary directories
mkdir -p config/{prometheus,grafana/{provisioning/{dashboards,datasources},dashboards},alertmanager}
mkdir -p data/{prometheus,grafana,alertmanager}

# Set proper permissions
chmod 777 data/grafana  # Grafana needs write permissions

# Deploy monitoring stack
docker-compose -f docker-compose.monitoring.yml up -d

# Wait for services to be ready
echo "⏳ Waiting for services to start..."
sleep 10

# Check service health
for service in otel-collector prometheus grafana; do
    if docker-compose -f docker-compose.monitoring.yml ps | grep -q "$service.*Up"; then
        echo "✅ $service is running"
    else
        echo "❌ $service failed to start"
        exit 1
    fi
done

echo "✅ Monitoring stack deployed successfully!"
echo "📊 Grafana: http://localhost:3000 (admin/admin)"
echo "📈 Prometheus: http://localhost:9090"
echo "🔔 Alertmanager: http://localhost:9093"
```

### 5.2 Validation Script

```bash
#!/bin/bash
# scripts/validate-monitoring.sh

set -e

echo "🔍 Validating Monitoring Setup"

# Check if metrics are being collected
METRICS=$(curl -s http://localhost:9090/api/v1/label/__name__/values | jq -r '.data[]' | grep qobuzarr | wc -l)

if [ "$METRICS" -gt 0 ]; then
    echo "✅ Found $METRICS Qobuzarr metrics in Prometheus"
else
    echo "❌ No Qobuzarr metrics found"
    exit 1
fi

# Check specific key metrics
for metric in "qobuzarr_api_calls_total" "qobuzarr_cache_hit_ratio" "qobuzarr_api_reduction_percentage"; do
    VALUE=$(curl -s "http://localhost:9090/api/v1/query?query=$metric" | jq -r '.data.result[0].value[1]' 2>/dev/null)
    
    if [ -n "$VALUE" ] && [ "$VALUE" != "null" ]; then
        echo "✅ $metric: $VALUE"
    else
        echo "⚠️ $metric: No data available yet"
    fi
done

# Check Grafana dashboards
DASHBOARDS=$(curl -s -u admin:admin http://localhost:3000/api/search | jq -r '.[].title' | grep -i qobuzarr | wc -l)

if [ "$DASHBOARDS" -gt 0 ]; then
    echo "✅ Found $DASHBOARDS Qobuzarr dashboards in Grafana"
else
    echo "⚠️ No Qobuzarr dashboards found (import manually if needed)"
fi

echo "✅ Monitoring validation complete!"
```

## Conclusion

This comprehensive monitoring implementation provides:

1. **Real-time metrics collection** with minimal performance overhead
2. **Full observability stack** using industry-standard tools
3. **Automated alerting** for performance degradation
4. **Beautiful dashboards** for stakeholder visibility
5. **Production-ready deployment** with validation

The system validates all performance claims and provides continuous monitoring of:
- API call reduction (target: 65.8%)
- Cache hit rates (target: 94.7%)
- ML optimization effectiveness (target: 87.3%)
- Download performance and reliability
- Error rates and system health

Total implementation time: ~16 hours
Monthly operational cost: ~$50 (cloud hosting)
ROI: Immediate visibility into production performance