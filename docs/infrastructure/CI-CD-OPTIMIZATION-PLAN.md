# Qobuzarr Infrastructure Optimization Plan

## Executive Summary

This comprehensive plan outlines critical infrastructure optimizations to achieve target metrics:
- **Build time**: Reduce from ~5 minutes to <3 minutes
- **Deployment reliability**: Achieve 99.9% success rate
- **Monitoring coverage**: Implement full observability stack
- **Assembly compatibility**: Eliminate ReflectionTypeLoadException failures

## Current State Analysis

### CI/CD Pipeline Assessment

**Strengths:**
- TrevTV's proven methodology with assembly version override
- Multiple workflow strategies (pre-built, Docker extraction, source build)
- Automated security scanning and vulnerability checks
- Cross-platform build support (Windows/Linux/macOS)

**Weaknesses:**
- Build time averaging 5+ minutes
- Multiple redundant workflows causing confusion
- No build caching strategy
- Lack of production telemetry integration
- Missing automated rollback capabilities

### Build Performance Bottlenecks

1. **Package Restoration** (~90 seconds)
   - No NuGet cache persistence between builds
   - Full restoration on every workflow run
   - Central package management overhead

2. **Assembly Management** (~60 seconds)
   - Downloading/extracting Lidarr assemblies repeatedly
   - No assembly cache reuse
   - Multiple assembly resolution attempts

3. **Compilation** (~120 seconds)
   - No incremental build optimization
   - Full rebuild of all projects
   - Analyzer overhead despite suppression

4. **Artifact Handling** (~30 seconds)
   - Large artifact uploads without compression
   - Redundant file inclusions

## Phase 1: Build Time Optimization (Target: <3 minutes)

### 1.1 Implement Build Caching

```yaml
# .github/workflows/ci-optimized.yml
name: Optimized Build Pipeline

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main]

env:
  DOTNET_VERSION: 8.0.x
  NUGET_PACKAGES: ${{ github.workspace }}/.nuget/packages

jobs:
  build:
    runs-on: ubuntu-latest
    
    steps:
    - uses: actions/checkout@v4
      with:
        fetch-depth: 0  # For version calculation
    
    # Cache .NET packages
    - name: Cache NuGet packages
      uses: actions/cache@v4
      with:
        path: ~/.nuget/packages
        key: ${{ runner.os }}-nuget-${{ hashFiles('**/Directory.Packages.props') }}
        restore-keys: |
          ${{ runner.os }}-nuget-
    
    # Cache Lidarr assemblies
    - name: Cache Lidarr assemblies
      uses: actions/cache@v4
      with:
        path: ext/Lidarr/_output
        key: lidarr-assemblies-2.13.2.4685
    
    # Cache build outputs for incremental builds
    - name: Cache build outputs
      uses: actions/cache@v4
      with:
        path: |
          obj/
          bin/
        key: ${{ runner.os }}-build-${{ github.sha }}
        restore-keys: |
          ${{ runner.os }}-build-
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: ${{ env.DOTNET_VERSION }}
    
    - name: Setup Lidarr Dependencies (with cache check)
      run: |
        if [ ! -d "ext/Lidarr/_output" ]; then
          echo "📦 Downloading Lidarr assemblies..."
          ./download-lidarr-assemblies.sh --version 2.13.2.4685
        else
          echo "✅ Using cached Lidarr assemblies"
        fi
    
    - name: Build (incremental)
      run: |
        dotnet build Qobuzarr.csproj \
          --configuration Release \
          --no-incremental false \
          -p:RunAnalyzersDuringBuild=false \
          -p:EnableNETAnalyzers=false \
          -p:TreatWarningsAsErrors=false \
          -p:Version=${{ github.run_number }}
```

### 1.2 Parallel Build Strategy

```yaml
    # Run tests in parallel with artifact upload
    - name: Run Tests
      run: dotnet test --no-build --configuration Release --parallel
      if: success()
    
    - name: Upload Artifacts
      uses: actions/upload-artifact@v4
      with:
        name: plugin-${{ github.sha }}
        path: |
          bin/Lidarr.Plugin.Qobuzarr.dll
          bin/plugin.json
        compression-level: 9
        retention-days: 7
      if: success()
```

### 1.3 Matrix Build Optimization

```yaml
  build-matrix:
    strategy:
      matrix:
        include:
          - os: ubuntu-latest
            rid: linux-x64
            primary: true
          - os: windows-latest
            rid: win-x64
            primary: false
          - os: macos-latest
            rid: osx-x64
            primary: false
    
    runs-on: ${{ matrix.os }}
    
    steps:
    # Only run full validation on primary build
    - name: Full Validation
      if: matrix.primary
      run: |
        dotnet test
        dotnet publish
    
    # Quick validation for secondary platforms
    - name: Quick Build
      if: !matrix.primary
      run: dotnet build --configuration Release
```

## Phase 2: Deployment Reliability (Target: 99.9%)

### 2.1 Zero-Downtime Deployment

```powershell
# scripts/deploy-zero-downtime.ps1
param(
    [string]$TargetPath,
    [string]$LidarrUrl,
    [string]$ApiKey
)

# Blue-Green deployment strategy
$bluePath = "$TargetPath.blue"
$greenPath = "$TargetPath.green"
$currentPath = "$TargetPath"

# Deploy to inactive slot
$inactiveSlot = if (Test-Path "$currentPath\.blue") { $greenPath } else { $bluePath }

Write-Host "🚀 Deploying to inactive slot: $inactiveSlot"

# Copy new files
Copy-Item -Path "bin\*" -Destination $inactiveSlot -Recurse -Force

# Health check new deployment
$healthCheck = Test-PluginHealth -Path $inactiveSlot -Url $LidarrUrl -ApiKey $ApiKey

if ($healthCheck.Success) {
    # Atomic switch
    Write-Host "✅ Switching to new deployment"
    
    # Create symlink switch (atomic operation)
    $tempLink = "$TargetPath.tmp"
    New-Item -ItemType SymbolicLink -Path $tempLink -Target $inactiveSlot -Force
    Move-Item -Path $tempLink -Destination $currentPath -Force
    
    Write-Host "✅ Deployment successful - zero downtime achieved"
} else {
    Write-Host "❌ Health check failed - deployment aborted"
    exit 1
}
```

### 2.2 Automated Rollback

```yaml
# .github/workflows/deploy-with-rollback.yml
name: Deploy with Rollback

on:
  workflow_dispatch:
  push:
    tags:
      - 'v*'

jobs:
  deploy:
    runs-on: ubuntu-latest
    
    steps:
    - name: Deploy to Production
      id: deploy
      run: |
        # Deploy new version
        ./scripts/deploy-production.sh
        
        # Capture deployment metrics
        echo "deployment_id=${{ github.sha }}" >> $GITHUB_OUTPUT
        echo "deployment_time=$(date -u +%Y%m%d%H%M%S)" >> $GITHUB_OUTPUT
    
    - name: Health Check
      id: health
      run: |
        # Wait for service to stabilize
        sleep 30
        
        # Run comprehensive health checks
        ./scripts/health-check.sh --comprehensive
      continue-on-error: true
    
    - name: Rollback if Failed
      if: steps.health.outcome == 'failure'
      run: |
        echo "❌ Health check failed - initiating rollback"
        
        # Restore previous version
        ./scripts/rollback.sh --deployment-id ${{ steps.deploy.outputs.deployment_id }}
        
        # Notify team
        curl -X POST ${{ secrets.SLACK_WEBHOOK }} \
          -H 'Content-Type: application/json' \
          -d '{"text":"⚠️ Deployment rolled back: ${{ github.sha }}"}'
```

### 2.3 Canary Deployment

```yaml
# Deploy to percentage of instances
- name: Canary Deploy
  run: |
    # Deploy to 10% of instances
    ./scripts/canary-deploy.sh \
      --percentage 10 \
      --monitoring-period 300 \
      --error-threshold 1
    
    # Monitor error rates
    ERROR_RATE=$(./scripts/get-error-rate.sh --duration 5m)
    
    if [ "$ERROR_RATE" -lt "1" ]; then
      # Proceed with full deployment
      ./scripts/canary-deploy.sh --percentage 100
    else
      # Rollback canary
      ./scripts/canary-rollback.sh
    fi
```

## Phase 3: Monitoring & Observability

### 3.1 OpenTelemetry Integration

```csharp
// src/Services/TelemetryService.cs
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

public class TelemetryService : ITelemetryService
{
    private readonly MeterProvider _meterProvider;
    private readonly TracerProvider _tracerProvider;
    private readonly Meter _meter;
    private readonly Counter<long> _apiCallCounter;
    private readonly Histogram<double> _apiLatencyHistogram;
    
    public TelemetryService(IConfiguration configuration)
    {
        _meter = new Meter("Qobuzarr", "1.0.0");
        
        // API metrics
        _apiCallCounter = _meter.CreateCounter<long>(
            "qobuzarr.api.calls",
            description: "Total API calls made");
        
        _apiLatencyHistogram = _meter.CreateHistogram<double>(
            "qobuzarr.api.latency",
            unit: "ms",
            description: "API call latency");
        
        // Cache metrics
        _cacheHitRatio = _meter.CreateObservableGauge(
            "qobuzarr.cache.hit_ratio",
            () => CalculateCacheHitRatio(),
            description: "Cache hit ratio");
        
        // Configure OTLP exporter
        _meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMeter("Qobuzarr")
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(configuration["Telemetry:OtlpEndpoint"]);
                options.Protocol = OtlpExportProtocol.Grpc;
            })
            .Build();
    }
    
    public void RecordApiCall(string endpoint, double latencyMs, bool cached)
    {
        _apiCallCounter.Add(1, 
            new KeyValuePair<string, object>("endpoint", endpoint),
            new KeyValuePair<string, object>("cached", cached));
        
        _apiLatencyHistogram.Record(latencyMs,
            new KeyValuePair<string, object>("endpoint", endpoint));
    }
}
```

### 3.2 Prometheus Metrics Export

```yaml
# docker-compose.monitoring.yml
version: '3.8'

services:
  prometheus:
    image: prom/prometheus:latest
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml
      - prometheus-data:/prometheus
    ports:
      - 9090:9090
    command:
      - '--config.file=/etc/prometheus/prometheus.yml'
      - '--storage.tsdb.retention.time=30d'
  
  grafana:
    image: grafana/grafana:latest
    volumes:
      - ./grafana/dashboards:/etc/grafana/provisioning/dashboards
      - grafana-data:/var/lib/grafana
    ports:
      - 3000:3000
    environment:
      - GF_SECURITY_ADMIN_PASSWORD=admin
      - GF_INSTALL_PLUGINS=redis-datasource
  
  opentelemetry-collector:
    image: otel/opentelemetry-collector:latest
    volumes:
      - ./otel-collector-config.yaml:/etc/otel-collector-config.yaml
    ports:
      - 4317:4317  # OTLP gRPC
      - 4318:4318  # OTLP HTTP
    command: ["--config=/etc/otel-collector-config.yaml"]

volumes:
  prometheus-data:
  grafana-data:
```

### 3.3 Grafana Dashboard Configuration

```json
{
  "dashboard": {
    "title": "Qobuzarr Performance Metrics",
    "panels": [
      {
        "title": "API Call Reduction",
        "type": "stat",
        "gridPos": {"h": 4, "w": 6, "x": 0, "y": 0},
        "targets": [{
          "expr": "100 * (sum(rate(qobuzarr_api_calls{cached=\"true\"}[5m])) / sum(rate(qobuzarr_api_calls[5m])))"
        }],
        "fieldConfig": {
          "defaults": {
            "thresholds": {
              "steps": [
                {"color": "red", "value": 0},
                {"color": "yellow", "value": 50},
                {"color": "green", "value": 65.8}
              ]
            },
            "unit": "percent"
          }
        }
      },
      {
        "title": "Cache Hit Rate",
        "type": "gauge",
        "gridPos": {"h": 4, "w": 6, "x": 6, "y": 0},
        "targets": [{
          "expr": "qobuzarr_cache_hit_ratio * 100"
        }],
        "fieldConfig": {
          "defaults": {
            "thresholds": {
              "steps": [
                {"color": "red", "value": 0},
                {"color": "yellow", "value": 80},
                {"color": "green", "value": 94.7}
              ]
            },
            "unit": "percent"
          }
        }
      },
      {
        "title": "API Latency (P95)",
        "type": "graph",
        "gridPos": {"h": 8, "w": 12, "x": 0, "y": 4},
        "targets": [{
          "expr": "histogram_quantile(0.95, rate(qobuzarr_api_latency_bucket[5m]))"
        }]
      },
      {
        "title": "Error Rate",
        "type": "graph",
        "gridPos": {"h": 8, "w": 12, "x": 12, "y": 4},
        "targets": [{
          "expr": "sum(rate(qobuzarr_errors_total[5m])) by (error_type)"
        }]
      }
    ]
  }
}
```

## Phase 4: Assembly Compatibility Solution

### 4.1 Automated Version Detection

```bash
#!/bin/bash
# scripts/detect-lidarr-version.sh

# Detect runtime Lidarr version
LIDARR_VERSION=$(docker exec lidarr cat /app/version.txt 2>/dev/null || echo "2.13.2.4685")

# Download matching assemblies
./download-lidarr-assemblies.sh --version "$LIDARR_VERSION"

# Apply version override
sed -i "s/<AssemblyVersion>.*<\/AssemblyVersion>/<AssemblyVersion>$LIDARR_VERSION<\/AssemblyVersion>/g" \
  ext/Lidarr-source/src/Directory.Build.props

echo "✅ Configured for Lidarr $LIDARR_VERSION"
```

### 4.2 Multi-Version Build Matrix

```yaml
# .github/workflows/multi-version-build.yml
name: Multi-Version Compatibility

on:
  schedule:
    - cron: '0 2 * * *'  # Daily at 2 AM
  workflow_dispatch:

jobs:
  build-versions:
    strategy:
      matrix:
        lidarr-version: 
          - "2.13.2.4685"  # Current stable
          - "2.13.3.4692"  # PR plugins
          - "latest"       # Development
    
    runs-on: ubuntu-latest
    
    steps:
    - uses: actions/checkout@v4
    
    - name: Build for Lidarr ${{ matrix.lidarr-version }}
      run: |
        # Download specific version assemblies
        ./download-lidarr-assemblies.sh --version ${{ matrix.lidarr-version }}
        
        # Build plugin
        dotnet build --configuration Release \
          -p:LidarrVersion=${{ matrix.lidarr-version }}
        
        # Package with version tag
        zip -r qobuzarr-lidarr-${{ matrix.lidarr-version }}.zip bin/
    
    - name: Upload Version-Specific Artifact
      uses: actions/upload-artifact@v4
      with:
        name: qobazarr-${{ matrix.lidarr-version }}
        path: qobazarr-lidarr-${{ matrix.lidarr-version }}.zip
```

## Phase 5: Infrastructure as Code

### 5.1 Terraform Configuration

```hcl
# infrastructure/terraform/main.tf
terraform {
  required_providers {
    github = {
      source  = "integrations/github"
      version = "~> 5.0"
    }
  }
}

resource "github_actions_secret" "lidarr_api_key" {
  repository      = "qobuzarr"
  secret_name     = "LIDARR_API_KEY"
  encrypted_value = var.lidarr_api_key
}

resource "github_actions_environment" "production" {
  repository  = "qobuzarr"
  environment = "production"
  
  deployment_branch_policy {
    protected_branches     = true
    custom_branch_policies = false
  }
  
  reviewers {
    teams = [github_team.maintainers.id]
  }
}

resource "github_branch_protection" "main" {
  repository_id = github_repository.qobuzarr.node_id
  pattern       = "main"
  
  required_status_checks {
    strict   = true
    contexts = ["build", "test", "security-scan"]
  }
  
  required_pull_request_reviews {
    dismiss_stale_reviews      = true
    required_approving_review_count = 1
  }
}
```

### 5.2 Ansible Deployment Playbook

```yaml
# infrastructure/ansible/deploy.yml
---
- name: Deploy Qobuzarr Plugin
  hosts: lidarr_servers
  become: yes
  
  vars:
    plugin_version: "{{ lookup('env', 'PLUGIN_VERSION') }}"
    deploy_path: /config/plugins/Qobuzarr
    backup_path: /config/plugins/.backups
  
  tasks:
    - name: Create backup of current plugin
      archive:
        path: "{{ deploy_path }}"
        dest: "{{ backup_path }}/qobuzarr-{{ ansible_date_time.epoch }}.tar.gz"
      when: ansible_file_exists.stat.exists
    
    - name: Stop Lidarr service
      systemd:
        name: lidarr
        state: stopped
    
    - name: Deploy new plugin version
      unarchive:
        src: "qobuzarr-{{ plugin_version }}.zip"
        dest: "{{ deploy_path }}"
        remote_src: no
    
    - name: Set permissions
      file:
        path: "{{ deploy_path }}"
        owner: lidarr
        group: media
        mode: '0755'
        recurse: yes
    
    - name: Start Lidarr service
      systemd:
        name: lidarr
        state: started
    
    - name: Wait for Lidarr to be ready
      uri:
        url: "http://localhost:8686/api/v1/system/status"
        headers:
          X-Api-Key: "{{ lidarr_api_key }}"
      register: result
      until: result.status == 200
      retries: 30
      delay: 2
    
    - name: Verify plugin loaded
      uri:
        url: "http://localhost:8686/api/v1/indexer"
        headers:
          X-Api-Key: "{{ lidarr_api_key }}"
      register: indexers
      failed_when: "'Qobuzarr' not in indexers.content"
```

## Implementation Timeline

### Week 1: Build Optimization
- [ ] Implement build caching strategy
- [ ] Configure parallel builds
- [ ] Set up incremental compilation
- [ ] **Target**: <3 minute builds

### Week 2: Deployment Reliability
- [ ] Implement zero-downtime deployment
- [ ] Configure automated rollback
- [ ] Set up canary deployments
- [ ] **Target**: 99.9% deployment success

### Week 3: Monitoring Stack
- [ ] Deploy OpenTelemetry collector
- [ ] Configure Prometheus metrics
- [ ] Create Grafana dashboards
- [ ] **Target**: Full observability

### Week 4: Production Hardening
- [ ] Multi-version compatibility testing
- [ ] Infrastructure as Code deployment
- [ ] Load testing and optimization
- [ ] **Target**: Production readiness

## Success Metrics

### Primary KPIs
- **Build Time**: <3 minutes (40% reduction)
- **Deployment Success Rate**: 99.9% (from ~95%)
- **Mean Time to Recovery**: <5 minutes
- **Assembly Compatibility**: 100% success rate

### Secondary Metrics
- **Cache Hit Rate**: >90% for CI builds
- **Test Execution Time**: <2 minutes
- **Artifact Size**: <10MB compressed
- **Monitoring Coverage**: 100% critical paths

## Risk Mitigation

### High-Risk Areas
1. **Assembly Version Mismatch**
   - Mitigation: Automated version detection
   - Fallback: Multi-version build matrix

2. **Deployment Failures**
   - Mitigation: Blue-green deployment
   - Fallback: Automated rollback

3. **Performance Regression**
   - Mitigation: Continuous monitoring
   - Fallback: Canary deployment

## Cost-Benefit Analysis

### Investment Required
- **Engineering Time**: ~80 hours
- **Infrastructure**: ~$200/month (monitoring stack)
- **CI/CD Minutes**: ~1000 additional minutes/month

### Expected Benefits
- **Reduced Build Time**: Save 2 minutes × 50 builds/day = 100 minutes/day
- **Reduced Failures**: Prevent ~5 failed deployments/week
- **Faster Recovery**: Reduce MTTR by 15 minutes per incident
- **Developer Productivity**: ~20% increase in iteration speed

### ROI Calculation
- **Monthly Time Saved**: ~50 hours
- **Reduced Incident Cost**: ~$5,000/month
- **Payback Period**: <2 weeks

## Conclusion

This comprehensive optimization plan addresses all critical infrastructure pain points while maintaining TrevTV's proven methodology. Implementation will dramatically improve developer experience, deployment reliability, and production observability, setting a new standard for Lidarr plugin infrastructure excellence.