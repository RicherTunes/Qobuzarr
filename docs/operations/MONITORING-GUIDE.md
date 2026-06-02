> ⚠️ Historical (flagged 2026-05-31): describes a past state; some details below no longer match the current code.

# Monitoring Guide

**Version:** 0.0.12+  
**Last Updated:** August 2024

## Table of Contents
- [Overview](#overview)
- [Metrics Collection](#metrics-collection)
- [Health Checks](#health-checks)
- [Performance Monitoring](#performance-monitoring)
- [ML System Monitoring](#ml-system-monitoring)
- [Quality Management Monitoring](#quality-management-monitoring)
- [Security Monitoring](#security-monitoring)
- [Alerting](#alerting)
- [Dashboards](#dashboards)
- [Troubleshooting](#troubleshooting)

## Overview

Comprehensive monitoring is essential for maintaining Qobuzarr's performance, especially given its ML-powered optimization features. This guide covers all aspects of monitoring, from basic health checks to advanced ML performance analytics.

### Monitoring Architecture
```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   Application   │───▶│    Prometheus    │───▶│    Grafana      │
│     Metrics     │    │    (Collection)  │    │  (Visualization)│
└─────────────────┘    └──────────────────┘    └─────────────────┘
                              │                          │
                              ▼                          ▼
                    ┌──────────────────┐    ┌─────────────────┐
                    │   AlertManager   │    │   External      │
                    │   (Alerting)     │    │   Monitoring    │
                    └──────────────────┘    └─────────────────┘
```

### Key Monitoring Areas
- **Application Performance**: Response times, throughput, error rates
- **ML Optimization**: Query optimization rates, model accuracy, API call reduction
- **Quality Management**: Cache hit rates, sampling efficiency, quality detection accuracy
- **Resource Utilization**: CPU, memory, disk, network usage
- **Security**: Authentication failures, rate limiting, suspicious activities
- **Business Metrics**: User engagement, successful downloads, quality distribution

## Metrics Collection

### Core Application Metrics
```csharp
public static class QobuzarrMetrics
{
    // Request metrics
    public static readonly Counter RequestsTotal = Metrics
        .CreateCounter("qobuzarr_requests_total", "Total requests processed",
            new[] { "endpoint", "method", "status" });
    
    public static readonly Histogram RequestDuration = Metrics
        .CreateHistogram("qobuzarr_request_duration_seconds", "Request duration",
            new HistogramConfiguration
            {
                Buckets = Histogram.ExponentialBuckets(0.001, 2, 15)
            });
    
    // API metrics
    public static readonly Counter ApiCallsTotal = Metrics
        .CreateCounter("qobuzarr_api_calls_total", "Total API calls made",
            new[] { "endpoint", "status", "cached" });
    
    public static readonly Gauge ApiCallReductionPercentage = Metrics
        .CreateGauge("qobuzarr_api_call_reduction_percentage", "API call reduction percentage");
    
    // ML metrics
    public static readonly Counter OptimizedQueriesTotal = Metrics
        .CreateCounter("qobuzarr_optimized_queries_total", "Queries optimized by ML",
            new[] { "optimization_type", "confidence_range" });
    
    public static readonly Gauge MlModelAccuracy = Metrics
        .CreateGauge("qobuzarr_ml_model_accuracy", "ML model accuracy",
            new[] { "model_type" });
    
    // Quality metrics
    public static readonly Counter QualityRequestsTotal = Metrics
        .CreateCounter("qobuzarr_quality_requests_total", "Quality detection requests",
            new[] { "source", "sampling_type" });
    
    public static readonly Gauge QualityCacheHitRate = Metrics
        .CreateGauge("qobuzarr_quality_cache_hit_rate", "Quality cache hit rate");
    
    // Resource metrics
    public static readonly Gauge MemoryUsageBytes = Metrics
        .CreateGauge("qobuzarr_memory_usage_bytes", "Memory usage in bytes",
            new[] { "type" });
    
    public static readonly Counter ExceptionsTotal = Metrics
        .CreateCounter("qobuzarr_exceptions_total", "Total exceptions occurred",
            new[] { "exception_type", "component" });
}
```

### Custom Metrics Service
```csharp
public class MetricsService : IMetricsService
{
    private readonly ILogger<MetricsService> _logger;
    private readonly Timer _metricsTimer;
    private readonly ConcurrentDictionary<string, MetricValue> _customMetrics;
    
    public MetricsService(ILogger<MetricsService> logger)
    {
        _logger = logger;
        _customMetrics = new ConcurrentDictionary<string, MetricValue>();
        
        // Update metrics every 30 seconds
        _metricsTimer = new Timer(UpdateMetrics, null, 
            TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }
    
    private void UpdateMetrics(object state)
    {
        try
        {
            // Memory metrics
            var gcMemory = GC.GetTotalMemory(false);
            var workingSet = Environment.WorkingSet;
            
            QobuzarrMetrics.MemoryUsageBytes.WithLabels("gc").Set(gcMemory);
            QobuzarrMetrics.MemoryUsageBytes.WithLabels("working_set").Set(workingSet);
            
            // Process metrics
            using var process = Process.GetCurrentProcess();
            QobuzarrMetrics.MemoryUsageBytes.WithLabels("private").Set(process.PrivateMemorySize64);
            
            // Custom business metrics
            UpdateBusinessMetrics();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update metrics");
        }
    }
    
    private void UpdateBusinessMetrics()
    {
        // These would be populated by your services
        var mlStats = GetMLOptimizationStats();
        var qualityStats = GetQualityManagementStats();
        var apiStats = GetApiUsageStats();
        
        QobuzarrMetrics.ApiCallReductionPercentage.Set(mlStats.ApiCallReductionPercentage);
        QobuzarrMetrics.MlModelAccuracy.WithLabels("query_optimizer").Set(mlStats.QueryOptimizerAccuracy);
        QobuzarrMetrics.QualityCacheHitRate.Set(qualityStats.CacheHitRate);
    }
    
    public void RecordRequest(string endpoint, string method, int statusCode, TimeSpan duration)
    {
        QobuzarrMetrics.RequestsTotal.WithLabels(endpoint, method, statusCode.ToString()).Inc();
        QobuzarrMetrics.RequestDuration.Observe(duration.TotalSeconds);
    }
    
    public void RecordApiCall(string endpoint, bool wasCached, bool wasSuccessful)
    {
        var status = wasSuccessful ? "success" : "error";
        var cached = wasCached ? "cached" : "api";
        
        QobuzarrMetrics.ApiCallsTotal.WithLabels(endpoint, status, cached).Inc();
    }
    
    public void RecordOptimizedQuery(OptimizationType type, double confidence)
    {
        var confidenceRange = confidence switch
        {
            >= 0.95 => "high",
            >= 0.8 => "medium",
            _ => "low"
        };
        
        QobuzarrMetrics.OptimizedQueriesTotal.WithLabels(type.ToString(), confidenceRange).Inc();
    }
}
```

### Prometheus Configuration
```yaml
# prometheus.yml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

rule_files:
  - "qobuzarr-rules.yml"

scrape_configs:
  - job_name: 'qobuzarr-application'
    static_configs:
      - targets: ['lidarr-service:9090']
    scrape_interval: 30s
    metrics_path: /metrics
    scrape_timeout: 10s

  - job_name: 'qobuzarr-ml-models'
    static_configs:
      - targets: ['lidarr-service:9091']
    scrape_interval: 60s
    metrics_path: /ml/metrics

  - job_name: 'qobuzarr-quality-service'
    static_configs:
      - targets: ['lidarr-service:9092']
    scrape_interval: 45s
    metrics_path: /quality/metrics

  - job_name: 'qobuzarr-redis'
    static_configs:
      - targets: ['redis-exporter:9121']
    scrape_interval: 30s

  - job_name: 'qobuzarr-postgres'
    static_configs:
      - targets: ['postgres-exporter:9187']
    scrape_interval: 30s

alerting:
  alertmanagers:
    - static_configs:
        - targets:
          - alertmanager:9093

# Alerting rules
# qobuzarr-rules.yml
groups:
  - name: qobuzarr-alerts
    rules:
      - alert: QobuzarrHighErrorRate
        expr: rate(qobuzarr_requests_total{status=~"5.."}[5m]) > 0.1
        for: 2m
        labels:
          severity: warning
        annotations:
          summary: "High error rate in Qobuzarr"
          description: "Error rate is {{ $value }} requests per second"

      - alert: QobuzarrMLAccuracyLow
        expr: qobuzarr_ml_model_accuracy < 0.8
        for: 5m
        labels:
          severity: critical
        annotations:
          summary: "ML model accuracy degraded"
          description: "Model accuracy is {{ $value }}, below 80% threshold"

      - alert: QobuzarrApiCallReductionLow
        expr: qobuzarr_api_call_reduction_percentage < 30
        for: 10m
        labels:
          severity: warning
        annotations:
          summary: "API call reduction below target"
          description: "Only {{ $value }}% reduction, expected >49%"
```

## Health Checks

### Application Health Endpoint
```csharp
public class HealthCheckService : IHealthCheck
{
    private readonly IQobuzApiClient _apiClient;
    private readonly IQualityCache _qualityCache;
    private readonly IMLQueryOptimizer _mlOptimizer;
    private readonly IDistributedCache _distributedCache;
    
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        var checks = new Dictionary<string, HealthCheckResult>();
        
        // API connectivity
        try
        {
            await _apiClient.TestConnectionAsync(cancellationToken);
            checks["qobuz_api"] = HealthCheckResult.Healthy("API connection successful");
        }
        catch (Exception ex)
        {
            checks["qobuz_api"] = HealthCheckResult.Unhealthy("API connection failed", ex);
        }
        
        // Cache health
        try
        {
            await _distributedCache.SetStringAsync("health_check", "ok", cancellationToken);
            var value = await _distributedCache.GetStringAsync("health_check", cancellationToken);
            
            checks["cache"] = value == "ok" 
                ? HealthCheckResult.Healthy("Cache operational")
                : HealthCheckResult.Degraded("Cache connectivity issues");
        }
        catch (Exception ex)
        {
            checks["cache"] = HealthCheckResult.Unhealthy("Cache failed", ex);
        }
        
        // ML system health
        try
        {
            var mlStats = await _mlOptimizer.GetHealthStatsAsync(cancellationToken);
            checks["ml_system"] = mlStats.IsHealthy 
                ? HealthCheckResult.Healthy($"ML accuracy: {mlStats.Accuracy:P}")
                : HealthCheckResult.Degraded($"ML accuracy low: {mlStats.Accuracy:P}");
        }
        catch (Exception ex)
        {
            checks["ml_system"] = HealthCheckResult.Unhealthy("ML system failed", ex);
        }
        
        // Overall health determination
        var hasUnhealthy = checks.Values.Any(c => c.Status == HealthStatus.Unhealthy);
        var hasDegraded = checks.Values.Any(c => c.Status == HealthStatus.Degraded);
        
        var overallStatus = hasUnhealthy ? HealthStatus.Unhealthy : 
                           hasDegraded ? HealthStatus.Degraded : 
                           HealthStatus.Healthy;
        
        var data = checks.ToDictionary(
            kvp => kvp.Key, 
            kvp => (object)new { 
                status = kvp.Value.Status.ToString(), 
                description = kvp.Value.Description 
            });
        
        return new HealthCheckResult(overallStatus, "Overall system health", data: data);
    }
}

// Health check registration
public void ConfigureServices(IServiceCollection services)
{
    services.AddHealthChecks()
        .AddCheck<HealthCheckService>("qobuzarr")
        .AddCheck("memory", () =>
        {
            var allocated = GC.GetTotalMemory(false);
            var threshold = 500 * 1024 * 1024; // 500MB
            
            return allocated < threshold 
                ? HealthCheckResult.Healthy($"Memory usage: {allocated / 1024 / 1024}MB")
                : HealthCheckResult.Degraded($"High memory usage: {allocated / 1024 / 1024}MB");
        })
        .AddCheck("disk_space", () =>
        {
            var drive = new DriveInfo(Path.GetTempPath());
            var freeSpaceGB = drive.AvailableFreeSpace / 1024 / 1024 / 1024;
            
            return freeSpaceGB > 1 
                ? HealthCheckResult.Healthy($"Free space: {freeSpaceGB}GB")
                : HealthCheckResult.Unhealthy($"Low disk space: {freeSpaceGB}GB");
        });
}
```

### Kubernetes Health Probes
```yaml
# lidarr-deployment.yaml
spec:
  template:
    spec:
      containers:
      - name: lidarr
        image: ghcr.io/hotio/lidarr:pr-plugins
        ports:
        - containerPort: 8686
        - containerPort: 9090  # Metrics port
        
        # Startup probe - gives more time for ML models to load
        startupProbe:
          httpGet:
            path: /health/startup
            port: 8686
          initialDelaySeconds: 30
          periodSeconds: 10
          timeoutSeconds: 5
          failureThreshold: 12  # 2 minutes total
          successThreshold: 1
        
        # Liveness probe - restarts container if unhealthy
        livenessProbe:
          httpGet:
            path: /health/live
            port: 8686
          initialDelaySeconds: 60
          periodSeconds: 30
          timeoutSeconds: 10
          failureThreshold: 3
          successThreshold: 1
        
        # Readiness probe - removes from service if not ready
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 8686
          initialDelaySeconds: 15
          periodSeconds: 10
          timeoutSeconds: 5
          failureThreshold: 3
          successThreshold: 1
```

## Performance Monitoring

### Application Performance Monitoring (APM)
```csharp
public class PerformanceMonitoringService : IPerformanceMonitoringService
{
    private readonly ActivitySource _activitySource;
    private readonly IMetricsLogger _metricsLogger;
    
    public PerformanceMonitoringService()
    {
        _activitySource = new ActivitySource("Qobuzarr");
    }
    
    public async Task<T> TraceOperationAsync<T>(string operationName, 
        Func<Activity, Task<T>> operation, Dictionary<string, object> tags = null)
    {
        using var activity = _activitySource.StartActivity(operationName);
        
        if (tags != null)
        {
            foreach (var tag in tags)
            {
                activity?.SetTag(tag.Key, tag.Value?.ToString());
            }
        }
        
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await operation(activity);
            
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().Name);
            activity?.SetTag("exception.message", ex.Message);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);
            
            _metricsLogger.LogOperation(operationName, stopwatch.Elapsed, 
                activity?.Status ?? ActivityStatusCode.Error);
        }
    }
}

// Usage in services
public async Task<QualityInfo> GetQualityAsync(string albumId)
{
    return await _performanceMonitor.TraceOperationAsync(
        "quality.get", 
        async activity =>
        {
            activity?.SetTag("album_id", albumId);
            
            var result = await GetQualityInternal(albumId);
            
            activity?.SetTag("source", result.Source.ToString());
            activity?.SetTag("confidence", result.Confidence);
            
            return result;
        });
}
```

### Database Performance Monitoring
```sql
-- PostgreSQL monitoring queries
-- Slow queries
SELECT 
    query,
    calls,
    total_time,
    mean_time,
    rows,
    100.0 * shared_blks_hit / nullif(shared_blks_hit + shared_blks_read, 0) AS hit_percent
FROM pg_stat_statements 
ORDER BY mean_time DESC 
LIMIT 10;

-- Database connections
SELECT 
    datname,
    numbackends,
    xact_commit,
    xact_rollback,
    blks_read,
    blks_hit,
    100.0 * blks_hit / (blks_hit + blks_read) as cache_hit_ratio
FROM pg_stat_database 
WHERE datname = 'qobuzarr';

-- Index usage
SELECT 
    schemaname,
    tablename,
    indexname,
    idx_tup_read,
    idx_tup_fetch,
    idx_scan
FROM pg_stat_user_indexes 
ORDER BY idx_scan DESC;
```

### Redis Performance Monitoring
```bash
# Redis monitoring commands
redis-cli info stats
redis-cli info memory
redis-cli info replication
redis-cli slowlog get 10

# Key analysis
redis-cli --bigkeys
redis-cli --memkeys

# Monitor operations
redis-cli monitor | grep -i qobuzarr
```

## ML System Monitoring

### ML Model Performance Tracking
```csharp
public class MLMonitoringService : IMLMonitoringService
{
    public class MLModelMetrics
    {
        public string ModelName { get; set; }
        public double Accuracy { get; set; }
        public double Precision { get; set; }
        public double Recall { get; set; }
        public double F1Score { get; set; }
        public int PredictionsToday { get; set; }
        public TimeSpan AveragePredictionTime { get; set; }
        public DateTime LastTrainingDate { get; set; }
        public Dictionary<string, double> FeatureImportance { get; set; }
    }
    
    public async Task<MLModelMetrics> GetModelMetricsAsync(string modelName)
    {
        var model = await _modelRepository.GetModelAsync(modelName);
        var recentPredictions = await _predictionRepository.GetRecentPredictionsAsync(
            modelName, TimeSpan.FromDays(1));
        
        return new MLModelMetrics
        {
            ModelName = modelName,
            Accuracy = CalculateAccuracy(recentPredictions),
            Precision = CalculatePrecision(recentPredictions),
            Recall = CalculateRecall(recentPredictions),
            F1Score = CalculateF1Score(recentPredictions),
            PredictionsToday = recentPredictions.Count,
            AveragePredictionTime = CalculateAveragePredictionTime(recentPredictions),
            LastTrainingDate = model.LastTrainingDate,
            FeatureImportance = await GetFeatureImportanceAsync(model)
        };
    }
    
    public async Task MonitorModelDriftAsync()
    {
        var models = await _modelRepository.GetAllModelsAsync();
        
        foreach (var model in models)
        {
            var recentPredictions = await _predictionRepository.GetRecentPredictionsAsync(
                model.Name, TimeSpan.FromDays(7));
            
            var currentAccuracy = CalculateAccuracy(recentPredictions);
            var baselineAccuracy = model.BaselineAccuracy;
            
            var accuracyDrift = Math.Abs(currentAccuracy - baselineAccuracy);
            
            if (accuracyDrift > 0.1) // 10% drift threshold
            {
                await _alertService.SendAlertAsync(new ModelDriftAlert
                {
                    ModelName = model.Name,
                    CurrentAccuracy = currentAccuracy,
                    BaselineAccuracy = baselineAccuracy,
                    DriftPercentage = accuracyDrift * 100,
                    RecommendedAction = "Consider retraining the model"
                });
            }
        }
    }
}
```

### ML Feature Monitoring
```csharp
public class FeatureMonitoringService : IFeatureMonitoringService
{
    public class FeatureDriftReport
    {
        public string FeatureName { get; set; }
        public double DriftScore { get; set; }
        public Dictionary<string, double> StatisticalChanges { get; set; }
        public List<string> AnomalousValues { get; set; }
    }
    
    public async Task<List<FeatureDriftReport>> DetectFeatureDriftAsync()
    {
        var features = await _featureRepository.GetActiveFeatureNamesAsync();
        var reports = new List<FeatureDriftReport>();
        
        foreach (var featureName in features)
        {
            var baselineStats = await _featureRepository.GetBaselineStatsAsync(featureName);
            var recentStats = await CalculateRecentStatsAsync(featureName, TimeSpan.FromDays(7));
            
            var driftScore = CalculateDriftScore(baselineStats, recentStats);
            
            if (driftScore > 0.3) // Drift threshold
            {
                reports.Add(new FeatureDriftReport
                {
                    FeatureName = featureName,
                    DriftScore = driftScore,
                    StatisticalChanges = CompareStatistics(baselineStats, recentStats),
                    AnomalousValues = await DetectAnomalousValuesAsync(featureName)
                });
            }
        }
        
        return reports;
    }
    
    private double CalculateDriftScore(FeatureStats baseline, FeatureStats current)
    {
        // Calculate KL divergence or similar statistical measure
        var meanDrift = Math.Abs(current.Mean - baseline.Mean) / baseline.StandardDeviation;
        var stdDrift = Math.Abs(current.StandardDeviation - baseline.StandardDeviation) / baseline.StandardDeviation;
        
        return (meanDrift + stdDrift) / 2;
    }
}
```

## Quality Management Monitoring

### Quality System Performance
```csharp
public class QualityMonitoringService : IQualityMonitoringService
{
    public class QualitySystemHealth
    {
        public double CacheHitRate { get; set; }
        public double PredictionAccuracy { get; set; }
        public double ApiCallReduction { get; set; }
        public TimeSpan AverageResponseTime { get; set; }
        public int ActiveProfiles { get; set; }
        public Dictionary<SamplingType, int> SamplingDistribution { get; set; }
        public List<QualityAlert> Alerts { get; set; }
    }
    
    public async Task<QualitySystemHealth> GetSystemHealthAsync()
    {
        var stats = await _qualityManager.GetStatsAsync();
        var profiles = await _profileRepository.GetActiveProfileCountAsync();
        var alerts = await DetectQualityAlertsAsync();
        
        return new QualitySystemHealth
        {
            CacheHitRate = stats.CacheHitRate,
            PredictionAccuracy = stats.PredictionAccuracy,
            ApiCallReduction = stats.ApiCallReduction,
            AverageResponseTime = stats.AverageResponseTime,
            ActiveProfiles = profiles,
            SamplingDistribution = stats.SamplingDistribution,
            Alerts = alerts
        };
    }
    
    private async Task<List<QualityAlert>> DetectQualityAlertsAsync()
    {
        var alerts = new List<QualityAlert>();
        var stats = await _qualityManager.GetStatsAsync();
        
        // Cache performance alerts
        if (stats.CacheHitRate < 0.8)
        {
            alerts.Add(new QualityAlert
            {
                Type = QualityAlertType.LowCacheHitRate,
                Severity = AlertSeverity.Warning,
                Message = $"Cache hit rate: {stats.CacheHitRate:P}",
                Recommendation = "Check cache configuration and TTL settings"
            });
        }
        
        // Sampling efficiency alerts
        if (stats.ApiCallReduction < 0.9)
        {
            alerts.Add(new QualityAlert
            {
                Type = QualityAlertType.LowApiReduction,
                Severity = AlertSeverity.Medium,
                Message = $"API call reduction: {stats.ApiCallReduction:P}",
                Recommendation = "Review sampling strategies and prediction thresholds"
            });
        }
        
        return alerts;
    }
}
```

### Artist and Label Profile Analytics
```csharp
public class ProfileAnalyticsService : IProfileAnalyticsService
{
    public class ProfileInsights
    {
        public int TotalArtistProfiles { get; set; }
        public int HighConfidenceProfiles { get; set; }
        public double AverageProfileAccuracy { get; set; }
        public List<string> TopConsistentArtists { get; set; }
        public List<string> ProblematicProfiles { get; set; }
        public Dictionary<string, int> QualityDistribution { get; set; }
    }
    
    public async Task<ProfileInsights> GetProfileInsightsAsync()
    {
        var artistProfiles = await _artistProfileRepository.GetAllProfilesAsync();
        var labelProfiles = await _labelProfileRepository.GetAllProfilesAsync();
        
        return new ProfileInsights
        {
            TotalArtistProfiles = artistProfiles.Count,
            HighConfidenceProfiles = artistProfiles.Count(p => p.Confidence > 0.9),
            AverageProfileAccuracy = artistProfiles.Average(p => p.Confidence),
            TopConsistentArtists = artistProfiles
                .Where(p => p.IsConsistentQuality)
                .OrderByDescending(p => p.Confidence)
                .Take(10)
                .Select(p => p.ArtistName)
                .ToList(),
            ProblematicProfiles = artistProfiles
                .Where(p => p.Confidence < 0.5 || !p.IsConsistentQuality)
                .OrderBy(p => p.Confidence)
                .Take(10)
                .Select(p => p.ArtistName)
                .ToList(),
            QualityDistribution = artistProfiles
                .GroupBy(p => p.TypicalQuality.ToString())
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }
}
```

## Security Monitoring

### Security Event Monitoring
```csharp
public class SecurityMonitoringService : ISecurityMonitoringService
{
    public class SecurityMetrics
    {
        public int FailedAuthenticationsToday { get; set; }
        public int RateLimitViolationsToday { get; set; }
        public List<string> SuspiciousIPs { get; set; }
        public int UnauthorizedAccessAttempts { get; set; }
        public double CredentialValidationSuccessRate { get; set; }
    }
    
    public async Task<SecurityMetrics> GetSecurityMetricsAsync()
    {
        var today = DateTime.UtcNow.Date;
        var securityEvents = await _securityEventRepository.GetEventsAsync(
            today, DateTime.UtcNow);
        
        return new SecurityMetrics
        {
            FailedAuthenticationsToday = securityEvents
                .Count(e => e.EventType == SecurityEventType.AuthenticationFailure),
            RateLimitViolationsToday = securityEvents
                .Count(e => e.EventType == SecurityEventType.RateLimitExceeded),
            SuspiciousIPs = DetectSuspiciousIPs(securityEvents),
            UnauthorizedAccessAttempts = securityEvents
                .Count(e => e.EventType == SecurityEventType.UnauthorizedAccess),
            CredentialValidationSuccessRate = CalculateCredentialSuccessRate(securityEvents)
        };
    }
    
    private List<string> DetectSuspiciousIPs(List<SecurityEvent> events)
    {
        return events
            .Where(e => e.EventType == SecurityEventType.AuthenticationFailure)
            .GroupBy(e => e.SourceIP)
            .Where(g => g.Count() > 10) // More than 10 failures
            .Select(g => g.Key)
            .ToList();
    }
    
    public async Task MonitorAnomalousActivityAsync()
    {
        var recentEvents = await _securityEventRepository.GetRecentEventsAsync(TimeSpan.FromHours(1));
        
        // Detect brute force attacks
        var suspiciousIPs = recentEvents
            .Where(e => e.EventType == SecurityEventType.AuthenticationFailure)
            .GroupBy(e => e.SourceIP)
            .Where(g => g.Count() > 5) // 5 failures in 1 hour
            .Select(g => g.Key);
        
        foreach (var ip in suspiciousIPs)
        {
            await _alertService.SendSecurityAlertAsync(new SecurityAlert
            {
                Type = SecurityAlertType.BruteForceAttempt,
                SourceIP = ip,
                EventCount = recentEvents.Count(e => e.SourceIP == ip),
                Recommendation = $"Consider blocking IP {ip}"
            });
        }
        
        // Detect credential stuffing
        var credentialAttempts = recentEvents
            .Where(e => e.EventType == SecurityEventType.AuthenticationFailure)
            .GroupBy(e => new { e.SourceIP, e.Username })
            .Where(g => g.Count() > 3);
        
        foreach (var attempt in credentialAttempts)
        {
            await _alertService.SendSecurityAlertAsync(new SecurityAlert
            {
                Type = SecurityAlertType.CredentialStuffing,
                SourceIP = attempt.Key.SourceIP,
                Username = attempt.Key.Username,
                EventCount = attempt.Count(),
                Recommendation = "Investigate potential credential stuffing attack"
            });
        }
    }
}
```

## Alerting

### AlertManager Configuration
```yaml
# alertmanager.yml
global:
  smtp_smarthost: 'smtp.gmail.com:587'
  smtp_from: 'alerts@yourdomain.com'
  smtp_auth_username: 'alerts@yourdomain.com'
  smtp_auth_password: 'your_password'

route:
  group_by: ['alertname', 'cluster', 'service']
  group_wait: 30s
  group_interval: 5m
  repeat_interval: 4h
  receiver: 'default'
  routes:
  - match:
      severity: critical
    receiver: 'critical-alerts'
    continue: true
  - match:
      alertname: QobuzarrMLAccuracyLow
    receiver: 'ml-team'
  - match:
      alertname: QobuzarrSecurityThreat
    receiver: 'security-team'

receivers:
- name: 'default'
  slack_configs:
  - api_url: 'https://hooks.slack.com/services/YOUR/SLACK/WEBHOOK'
    channel: '#qobuzarr-alerts'
    title: 'Qobuzarr Alert'
    text: '{{ range .Alerts }}{{ .Annotations.summary }}{{ end }}'

- name: 'critical-alerts'
  email_configs:
  - to: 'oncall@yourdomain.com'
    subject: 'CRITICAL: Qobuzarr Alert'
    body: |
      {{ range .Alerts }}
      Alert: {{ .Annotations.summary }}
      Description: {{ .Annotations.description }}
      Severity: {{ .Labels.severity }}
      {{ end }}
  pagerduty_configs:
  - service_key: 'your-pagerduty-service-key'

- name: 'ml-team'
  email_configs:
  - to: 'ml-team@yourdomain.com'
    subject: 'ML System Alert - {{ .CommonLabels.alertname }}'

- name: 'security-team'
  email_configs:
  - to: 'security@yourdomain.com'
    subject: 'SECURITY ALERT - {{ .CommonLabels.alertname }}'
```

### Custom Alert Rules
```yaml
# qobuzarr-alerts.yml
groups:
  - name: qobuzarr.performance
    rules:
      - alert: QobuzarrHighResponseTime
        expr: histogram_quantile(0.95, rate(qobuzarr_request_duration_seconds_bucket[5m])) > 2
        for: 3m
        labels:
          severity: warning
        annotations:
          summary: "Qobuzarr response time high"
          description: "95th percentile response time is {{ $value }}s"

      - alert: QobuzarrHighMemoryUsage
        expr: qobuzarr_memory_usage_bytes{type="working_set"} > 1073741824  # 1GB
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Qobuzarr high memory usage"
          description: "Memory usage is {{ $value | humanizeBytes }}"

  - name: qobuzarr.ml
    rules:
      - alert: QobuzarrMLModelStale
        expr: time() - qobuzarr_ml_model_last_training_timestamp > 604800  # 7 days
        for: 1h
        labels:
          severity: warning
        annotations:
          summary: "ML model needs retraining"
          description: "Model {{ $labels.model_name }} hasn't been retrained in over 7 days"

      - alert: QobuzarrMLPredictionLatency
        expr: histogram_quantile(0.95, rate(qobuzarr_ml_prediction_duration_seconds_bucket[5m])) > 0.5
        for: 2m
        labels:
          severity: warning
        annotations:
          summary: "ML prediction latency high"
          description: "95th percentile ML prediction time is {{ $value }}s"

  - name: qobuzarr.quality
    rules:
      - alert: QobuzarrQualityCacheMiss
        expr: qobuzarr_quality_cache_hit_rate < 0.7
        for: 10m
        labels:
          severity: warning
        annotations:
          summary: "Quality cache hit rate low"
          description: "Cache hit rate is {{ $value | humanizePercentage }}"

  - name: qobuzarr.security
    rules:
      - alert: QobuzarrHighAuthFailures
        expr: rate(qobuzarr_auth_failures_total[5m]) > 0.1
        for: 2m
        labels:
          severity: critical
        annotations:
          summary: "High authentication failure rate"
          description: "{{ $value }} authentication failures per second"
```

## Dashboards

### Grafana Dashboard Configuration
```json
{
  "dashboard": {
    "title": "Qobuzarr Monitoring Dashboard",
    "tags": ["qobuzarr", "monitoring"],
    "timezone": "utc",
    "panels": [
      {
        "title": "API Call Reduction",
        "type": "stat",
        "gridPos": {"h": 4, "w": 6, "x": 0, "y": 0},
        "targets": [
          {
            "expr": "qobuzarr_api_call_reduction_percentage",
            "legendFormat": "Reduction %"
          }
        ],
        "fieldConfig": {
          "defaults": {
            "unit": "percent",
            "min": 0,
            "max": 100,
            "thresholds": {
              "steps": [
                {"color": "red", "value": 0},
                {"color": "yellow", "value": 30},
                {"color": "green", "value": 49}
              ]
            }
          }
        }
      },
      {
        "title": "Request Rate",
        "type": "graph",
        "gridPos": {"h": 8, "w": 12, "x": 0, "y": 4},
        "targets": [
          {
            "expr": "rate(qobuzarr_requests_total[5m])",
            "legendFormat": "{{ endpoint }} - {{ method }}"
          }
        ],
        "yAxes": [
          {
            "label": "Requests/sec",
            "min": 0
          }
        ]
      },
      {
        "title": "ML Model Accuracy",
        "type": "gauge",
        "gridPos": {"h": 4, "w": 6, "x": 6, "y": 0},
        "targets": [
          {
            "expr": "qobuzarr_ml_model_accuracy",
            "legendFormat": "{{ model_type }}"
          }
        ],
        "fieldConfig": {
          "defaults": {
            "unit": "percentunit",
            "min": 0,
            "max": 1,
            "thresholds": {
              "steps": [
                {"color": "red", "value": 0},
                {"color": "yellow", "value": 0.7},
                {"color": "green", "value": 0.8}
              ]
            }
          }
        }
      },
      {
        "title": "Quality Cache Performance",
        "type": "graph",
        "gridPos": {"h": 8, "w": 12, "x": 12, "y": 4},
        "targets": [
          {
            "expr": "qobuzarr_quality_cache_hit_rate",
            "legendFormat": "Cache Hit Rate"
          },
          {
            "expr": "rate(qobuzarr_quality_requests_total[5m])",
            "legendFormat": "{{ source }} requests/sec"
          }
        ]
      },
      {
        "title": "System Resources",
        "type": "graph",
        "gridPos": {"h": 8, "w": 24, "x": 0, "y": 12},
        "targets": [
          {
            "expr": "qobuzarr_memory_usage_bytes{type=\"working_set\"} / 1024 / 1024",
            "legendFormat": "Memory (MB)"
          },
          {
            "expr": "rate(process_cpu_seconds_total[5m]) * 100",
            "legendFormat": "CPU %"
          }
        ]
      },
      {
        "title": "Error Rate",
        "type": "graph",
        "gridPos": {"h": 6, "w": 12, "x": 0, "y": 20},
        "targets": [
          {
            "expr": "rate(qobuzarr_requests_total{status=~\"5..\"}[5m])",
            "legendFormat": "5xx errors/sec"
          },
          {
            "expr": "rate(qobuzarr_requests_total{status=~\"4..\"}[5m])",
            "legendFormat": "4xx errors/sec"
          }
        ]
      },
      {
        "title": "Top Slow Endpoints",
        "type": "table",
        "gridPos": {"h": 6, "w": 12, "x": 12, "y": 20},
        "targets": [
          {
            "expr": "topk(10, histogram_quantile(0.95, rate(qobuzarr_request_duration_seconds_bucket[5m])))",
            "format": "table",
            "instant": true
          }
        ]
      }
    ]
  }
}
```

### Business Intelligence Dashboard
```json
{
  "dashboard": {
    "title": "Qobuzarr Business Metrics",
    "panels": [
      {
        "title": "Daily Active Users",
        "type": "graph",
        "targets": [
          {
            "expr": "count(count by (user_id)(rate(qobuzarr_requests_total[24h])))",
            "legendFormat": "Active Users"
          }
        ]
      },
      {
        "title": "Quality Distribution",
        "type": "pie",
        "targets": [
          {
            "expr": "qobuzarr_quality_detection_total",
            "legendFormat": "{{ quality_type }}"
          }
        ]
      },
      {
        "title": "Most Popular Artists",
        "type": "table",
        "targets": [
          {
            "expr": "topk(20, sum by (artist_name)(rate(qobuzarr_search_requests_total[24h])))",
            "format": "table"
          }
        ]
      },
      {
        "title": "Download Success Rate",
        "type": "stat",
        "targets": [
          {
            "expr": "rate(qobuzarr_downloads_successful_total[1h]) / rate(qobuzarr_downloads_total[1h])",
            "legendFormat": "Success Rate"
          }
        ]
      }
    ]
  }
}
```

## Troubleshooting

### Common Monitoring Issues

#### Metrics Not Appearing
```bash
# Check metrics endpoint
curl http://localhost:9090/metrics | grep qobuzarr

# Verify Prometheus scraping
curl http://localhost:9091/api/v1/targets

# Check service discovery
kubectl get endpoints -n qobuzarr

# Verify network policies
kubectl describe networkpolicy qobuzarr-network-policy -n qobuzarr
```

#### High Cardinality Metrics
```csharp
// Bad: Creates too many metric series
QobuzarrMetrics.RequestsTotal.WithLabels(
    request.Path,        // High cardinality
    request.UserAgent,   // High cardinality  
    request.UserId       // High cardinality
).Inc();

// Good: Bounded cardinality
QobuzarrMetrics.RequestsTotal.WithLabels(
    GetEndpointCategory(request.Path),  // Limited categories
    request.Method,                     // Limited values
    GetStatusCategory(response.Status)  // Grouped statuses
).Inc();
```

#### Memory Issues in Monitoring
```yaml
# Prometheus configuration for large deployments
global:
  scrape_interval: 60s  # Increase interval
  evaluation_interval: 60s

storage:
  tsdb:
    retention.time: 30d  # Reduce retention
    retention.size: 50GB
    wal-compression: true

# Grafana resource limits
resources:
  limits:
    memory: "2Gi"
    cpu: "1000m"
  requests:
    memory: "1Gi" 
    cpu: "500m"
```

This comprehensive monitoring guide ensures you have full visibility into Qobuzarr's performance, from basic system health to advanced ML model analytics and business intelligence.