# Deployment Guide

**Version:** 0.0.12+  
**Last Updated:** August 2024

## Table of Contents
- [Overview](#overview)
- [Prerequisites](#prerequisites)
- [Local Development Deployment](#local-development-deployment)
- [Docker Deployment](#docker-deployment)
- [Kubernetes Deployment](#kubernetes-deployment)
- [Production Deployment](#production-deployment)
- [Cloud Deployments](#cloud-deployments)
- [Configuration Management](#configuration-management)
- [Security Considerations](#security-considerations)
- [Monitoring Setup](#monitoring-setup)
- [Troubleshooting](#troubleshooting)

## Overview

This guide covers deploying Qobuzarr in various environments, from local development to production cloud deployments. Qobuzarr is designed as a Lidarr plugin with optional standalone capabilities.

### Deployment Options
- **Plugin Mode**: Deploy as a Lidarr plugin (recommended)
- **Standalone Mode**: Deploy as an independent service
- **Hybrid Mode**: Plugin with external ML/caching services

### Supported Platforms
- Windows (Windows Server 2019+, Windows 10+)
- Linux (Ubuntu 20.04+, CentOS 8+, Alpine 3.14+)
- macOS (10.15+)
- Docker containers
- Kubernetes clusters

## Prerequisites

### System Requirements
```yaml
Minimum:
  CPU: 2 cores
  RAM: 4GB
  Storage: 10GB available
  Network: 100 Mbps

Recommended:
  CPU: 4+ cores
  RAM: 8GB+
  Storage: 50GB+ SSD
  Network: 1 Gbps

Production:
  CPU: 8+ cores
  RAM: 16GB+
  Storage: 100GB+ NVMe SSD
  Network: 10 Gbps
  Load Balancer: Required for HA
```

### Software Dependencies
```bash
# .NET Runtime
.NET 8.0 Runtime (or .NET 6.0 for older Lidarr versions)

# Lidarr (Plugin Mode)
Lidarr 2.13.2.4685+ (hotio/lidarr:pr-plugins recommended)

# Database (Standalone Mode)
SQLite 3.35+ (included) or PostgreSQL 13+

# Optional External Services
Redis 6.0+ (for distributed caching)
Elasticsearch 7.10+ (for advanced logging)
Prometheus + Grafana (for monitoring)
```

## Local Development Deployment

### Quick Setup
```bash
# Clone repository
git clone https://github.com/yourusername/qobuzarr.git
cd qobuzarr

# Setup development environment
./setup.sh --enable-deploy
# or PowerShell: .\setup.ps1 -EnableDeploy

# Build and deploy to local Lidarr
./build.sh --deploy
# or PowerShell: .\build.ps1 -Deploy
```

### Manual Setup
```bash
# 1. Build the plugin
dotnet build --configuration Release \
  -p:RunAnalyzersDuringBuild=false \
  -p:EnableNETAnalyzers=false \
  -p:TreatWarningsAsErrors=false

# 2. Create plugin directory
mkdir -p /lidarr/plugins/RicherTunes/Qobuzarr

# 3. Deploy plugin files
cp bin/Release/net6.0/Lidarr.Plugin.Qobuzarr.dll /lidarr/plugins/RicherTunes/Qobuzarr/
cp plugin.json /lidarr/plugins/RicherTunes/Qobuzarr/
cp src/Indexers/ml-baseline-patterns.json /lidarr/plugins/RicherTunes/Qobuzarr/

# 4. Restart Lidarr
sudo systemctl restart lidarr
```

### Development Configuration
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Lidarr.Plugin.Qobuzarr": "Debug"
    }
  },
  "Qobuz": {
    "Environment": "Development",
    "EnableDebugLogging": true,
    "EnableMetrics": true,
    "ApiTimeout": "00:00:30"
  }
}
```

## Docker Deployment

### Docker Compose Setup
```yaml
# docker-compose.yml
version: '3.8'

services:
  lidarr:
    image: ghcr.io/hotio/lidarr:pr-plugins
    container_name: lidarr-qobuzarr
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=UTC
      - LIDARR__ANALYTICS_ENABLED=False
    volumes:
      - ./config:/config
      - ./downloads:/downloads
      - ./music:/music
      - ./qobuzarr:/config/plugins/RicherTunes/Qobuzarr
    ports:
      - "8686:8686"
    restart: unless-stopped
    depends_on:
      - qobuzarr-redis
      - qobuzarr-postgres

  qobuzarr-redis:
    image: redis:7-alpine
    container_name: qobuzarr-redis
    command: redis-server --appendonly yes --requirepass ${REDIS_PASSWORD}
    environment:
      - REDIS_PASSWORD=${REDIS_PASSWORD}
    volumes:
      - redis-data:/data
    ports:
      - "6379:6379"
    restart: unless-stopped

  qobuzarr-postgres:
    image: postgres:15-alpine
    container_name: qobuzarr-postgres
    environment:
      - POSTGRES_DB=qobuzarr
      - POSTGRES_USER=${POSTGRES_USER}
      - POSTGRES_PASSWORD=${POSTGRES_PASSWORD}
    volumes:
      - postgres-data:/var/lib/postgresql/data
    ports:
      - "5432:5432"
    restart: unless-stopped

  qobuzarr-monitoring:
    image: grafana/grafana:latest
    container_name: qobuzarr-grafana
    environment:
      - GF_SECURITY_ADMIN_PASSWORD=${GRAFANA_PASSWORD}
    volumes:
      - grafana-data:/var/lib/grafana
      - ./monitoring/grafana/dashboards:/etc/grafana/provisioning/dashboards
      - ./monitoring/grafana/datasources:/etc/grafana/provisioning/datasources
    ports:
      - "3000:3000"
    restart: unless-stopped

volumes:
  redis-data:
  postgres-data:
  grafana-data:

networks:
  default:
    name: qobuzarr-network
```

### Environment Configuration
```bash
# .env
POSTGRES_USER=qobuzarr
POSTGRES_PASSWORD=your_secure_password
REDIS_PASSWORD=your_redis_password
GRAFANA_PASSWORD=your_grafana_password

# Qobuz API credentials
QOBUZ_APP_ID=your_app_id
QOBUZ_APP_SECRET=your_app_secret

# Optional: User credentials for development
QOBUZ_EMAIL=your@email.com
QOBUZ_PASSWORD=your_password
```

### Docker Build
```dockerfile
# Dockerfile (for standalone deployment)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000
EXPOSE 5001

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY ["Qobuzarr.csproj", "."]
COPY ["QobuzCLI/QobuzCLI.csproj", "QobuzCLI/"]
RUN dotnet restore "Qobuzarr.csproj"

# Copy source code
COPY . .
WORKDIR "/src"

# Build application
RUN dotnet build "Qobuzarr.csproj" -c Release -o /app/build \
  -p:RunAnalyzersDuringBuild=false \
  -p:EnableNETAnalyzers=false \
  -p:TreatWarningsAsErrors=false

FROM build AS publish
RUN dotnet publish "Qobuzarr.csproj" -c Release -o /app/publish \
  --no-restore \
  -p:RunAnalyzersDuringBuild=false \
  -p:EnableNETAnalyzers=false \
  -p:TreatWarningsAsErrors=false

FROM base AS final
WORKDIR /app

# Install additional dependencies
RUN apt-get update && apt-get install -y \
    curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

# Create non-root user
RUN groupadd -r qobuzarr && useradd -r -g qobuzarr qobuzarr
RUN chown -R qobuzarr:qobuzarr /app
USER qobuzarr

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
  CMD curl -f http://localhost:5000/health || exit 1

ENTRYPOINT ["dotnet", "Lidarr.Plugin.Qobuzarr.dll"]
```

### Deployment Commands
```bash
# Deploy with Docker Compose
docker-compose up -d

# Check logs
docker-compose logs -f lidarr

# Update plugin
docker-compose exec lidarr sh -c "
  wget https://github.com/yourusername/qobuzarr/releases/latest/download/qobuzarr-plugin.zip &&
  unzip -o qobuzarr-plugin.zip -d /config/plugins/RicherTunes/Qobuzarr/ &&
  rm qobuzarr-plugin.zip
"
docker-compose restart lidarr

# Backup configuration
docker-compose exec lidarr tar -czf /tmp/qobuzarr-config.tar.gz \
  /config/plugins/RicherTunes/Qobuzarr/
docker cp lidarr-qobuzarr:/tmp/qobuzarr-config.tar.gz ./backup/
```

## Kubernetes Deployment

### Namespace and ConfigMap
```yaml
# namespace.yaml
apiVersion: v1
kind: Namespace
metadata:
  name: qobuzarr
---
# configmap.yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: qobuzarr-config
  namespace: qobuzarr
data:
  appsettings.json: |
    {
      "Qobuz": {
        "ApiBaseUrl": "https://api.qobuz.com",
        "ApiTimeout": "00:01:00",
        "EnableCaching": true,
        "EnableMLOptimization": true
      },
      "Cache": {
        "Redis": {
          "ConnectionString": "qobuzarr-redis:6379",
          "DefaultTTL": "24:00:00"
        }
      },
      "Database": {
        "ConnectionString": "Host=qobuzarr-postgres;Database=qobuzarr;Username=qobuzarr;Password=$(POSTGRES_PASSWORD)"
      }
    }
```

### Secrets
```yaml
# secrets.yaml
apiVersion: v1
kind: Secret
metadata:
  name: qobuzarr-secrets
  namespace: qobuzarr
type: Opaque
data:
  qobuz-app-id: <base64-encoded-app-id>
  qobuz-app-secret: <base64-encoded-app-secret>
  postgres-password: <base64-encoded-password>
  redis-password: <base64-encoded-password>
```

### Lidarr Deployment
```yaml
# lidarr-deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: lidarr
  namespace: qobuzarr
spec:
  replicas: 1
  selector:
    matchLabels:
      app: lidarr
  template:
    metadata:
      labels:
        app: lidarr
    spec:
      containers:
      - name: lidarr
        image: ghcr.io/hotio/lidarr:pr-plugins
        ports:
        - containerPort: 8686
        env:
        - name: PUID
          value: "1000"
        - name: PGID
          value: "1000"
        - name: TZ
          value: "UTC"
        - name: QOBUZ_APP_ID
          valueFrom:
            secretKeyRef:
              name: qobuzarr-secrets
              key: qobuz-app-id
        - name: QOBUZ_APP_SECRET
          valueFrom:
            secretKeyRef:
              name: qobuzarr-secrets
              key: qobuz-app-secret
        volumeMounts:
        - name: config
          mountPath: /config
        - name: downloads
          mountPath: /downloads
        - name: music
          mountPath: /music
        - name: plugin-config
          mountPath: /config/plugins/RicherTunes/Qobuzarr
        resources:
          requests:
            memory: "2Gi"
            cpu: "1000m"
          limits:
            memory: "4Gi"
            cpu: "2000m"
        readinessProbe:
          httpGet:
            path: /
            port: 8686
          initialDelaySeconds: 30
          periodSeconds: 10
        livenessProbe:
          httpGet:
            path: /
            port: 8686
          initialDelaySeconds: 60
          periodSeconds: 30
      volumes:
      - name: config
        persistentVolumeClaim:
          claimName: lidarr-config
      - name: downloads
        persistentVolumeClaim:
          claimName: lidarr-downloads
      - name: music
        persistentVolumeClaim:
          claimName: lidarr-music
      - name: plugin-config
        configMap:
          name: qobuzarr-config

---
apiVersion: v1
kind: Service
metadata:
  name: lidarr-service
  namespace: qobuzarr
spec:
  selector:
    app: lidarr
  ports:
  - port: 8686
    targetPort: 8686
  type: ClusterIP
```

### Redis and PostgreSQL
```yaml
# redis-deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: redis
  namespace: qobuzarr
spec:
  replicas: 1
  selector:
    matchLabels:
      app: redis
  template:
    metadata:
      labels:
        app: redis
    spec:
      containers:
      - name: redis
        image: redis:7-alpine
        ports:
        - containerPort: 6379
        args:
        - redis-server
        - --appendonly
        - "yes"
        - --requirepass
        - "$(REDIS_PASSWORD)"
        env:
        - name: REDIS_PASSWORD
          valueFrom:
            secretKeyRef:
              name: qobuzarr-secrets
              key: redis-password
        volumeMounts:
        - name: redis-data
          mountPath: /data
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "512Mi"
            cpu: "500m"
      volumes:
      - name: redis-data
        persistentVolumeClaim:
          claimName: redis-data

---
apiVersion: v1
kind: Service
metadata:
  name: qobuzarr-redis
  namespace: qobuzarr
spec:
  selector:
    app: redis
  ports:
  - port: 6379
    targetPort: 6379
  type: ClusterIP
```

### Ingress
```yaml
# ingress.yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: qobuzarr-ingress
  namespace: qobuzarr
  annotations:
    kubernetes.io/ingress.class: "nginx"
    cert-manager.io/cluster-issuer: "letsencrypt-prod"
    nginx.ingress.kubernetes.io/ssl-redirect: "true"
    nginx.ingress.kubernetes.io/auth-basic: "Authentication Required"
    nginx.ingress.kubernetes.io/auth-secret: qobuzarr-basic-auth
spec:
  tls:
  - hosts:
    - qobuzarr.yourdomain.com
    secretName: qobuzarr-tls
  rules:
  - host: qobuzarr.yourdomain.com
    http:
      paths:
      - path: /
        pathType: Prefix
        backend:
          service:
            name: lidarr-service
            port:
              number: 8686
```

### Deployment Commands
```bash
# Deploy to Kubernetes
kubectl apply -f namespace.yaml
kubectl apply -f secrets.yaml
kubectl apply -f configmap.yaml
kubectl apply -f redis-deployment.yaml
kubectl apply -f postgres-deployment.yaml
kubectl apply -f lidarr-deployment.yaml
kubectl apply -f ingress.yaml

# Check deployment status
kubectl get pods -n qobuzarr
kubectl logs -f deployment/lidarr -n qobuzarr

# Scale deployment
kubectl scale deployment lidarr --replicas=3 -n qobuzarr

# Rolling update
kubectl set image deployment/lidarr lidarr=ghcr.io/hotio/lidarr:pr-plugins-latest -n qobuzarr
kubectl rollout status deployment/lidarr -n qobuzarr
```

## Production Deployment

### High Availability Setup
```yaml
# ha-deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: lidarr-ha
  namespace: qobuzarr
spec:
  replicas: 3
  selector:
    matchLabels:
      app: lidarr
  template:
    metadata:
      labels:
        app: lidarr
    spec:
      affinity:
        podAntiAffinity:
          preferredDuringSchedulingIgnoredDuringExecution:
          - weight: 100
            podAffinityTerm:
              labelSelector:
                matchExpressions:
                - key: app
                  operator: In
                  values:
                  - lidarr
              topologyKey: kubernetes.io/hostname
      containers:
      - name: lidarr
        image: ghcr.io/hotio/lidarr:pr-plugins
        # ... container spec
        env:
        - name: LIDARR__INSTANCENAME
          valueFrom:
            fieldRef:
              fieldPath: metadata.name
        resources:
          requests:
            memory: "4Gi"
            cpu: "2000m"
          limits:
            memory: "8Gi"
            cpu: "4000m"
```

### Production Configuration
```json
{
  "Qobuz": {
    "Environment": "Production",
    "ApiTimeout": "00:02:00",
    "MaxConcurrentRequests": 10,
    "EnableRetry": true,
    "RetryAttempts": 3
  },
  "Cache": {
    "Redis": {
      "ConnectionString": "qobuzarr-redis-cluster:6379",
      "ClusterMode": true,
      "DefaultTTL": "24:00:00",
      "MaxMemoryPolicy": "allkeys-lru"
    }
  },
  "Database": {
    "ConnectionString": "Host=postgres-primary;Database=qobuzarr;Username=qobuzarr;Password=$(POSTGRES_PASSWORD);Pooling=true;MinPoolSize=10;MaxPoolSize=100",
    "EnableReadReplica": true,
    "ReadReplicaConnectionString": "Host=postgres-replica;Database=qobuzarr;Username=qobuzarr;Password=$(POSTGRES_PASSWORD);Pooling=true;MinPoolSize=5;MaxPoolSize=50"
  },
  "ML": {
    "EnableDistributedTraining": true,
    "ModelUpdateInterval": "12:00:00",
    "EnableA11yTesting": true
  },
  "Security": {
    "EnableAuditLogging": true,
    "EnableRateLimiting": true,
    "MaxRequestsPerMinute": 1000
  },
  "Monitoring": {
    "EnableMetrics": true,
    "EnableTracing": true,
    "EnableHealthChecks": true,
    "MetricsPort": 9090
  }
}
```

### Release Automation (CI/CD)

Qobuzarr uses GitHub Actions for tests and releases.

- Fast tests: run on push/PR using `.github/workflows/ci-tests.yml`.
- Full/Live tests: manual `workflow_dispatch` (requires environment/secrets for live).
- Release options:
  - Manual workflow: Actions → “Release (manual)” → set `version` (e.g., `0.0.15`). Builds, tags `v<version>`, drafts release, uploads assets.
  - Tag-driven: push a tag `vX.Y.Z` to `main` and the “Release (tag)” workflow builds and publishes.

Artifacts per release:
- `qobuzarr-<version>.zip` containing `Lidarr.Plugin.Qobuzarr.dll` and `plugin.json`.
- Standalone `Lidarr.Plugin.Qobuzarr.dll` and `plugin.json` for manual installs.

Notes
- `plugin.json` is generated from `plugin.json.template` during Pack/Publish and release workflows. Local `dotnet build` may not regenerate it.

Examples
```bash
# Trigger tag-based release
git tag v0.0.15
git push origin v0.0.15

# Trigger manual release
gh workflow run "Release (manual)" -f version=0.0.15
```

### Database Migration
```bash
# Production database setup
kubectl exec -it postgres-primary-0 -n qobuzarr -- psql -U qobuzarr -d qobuzarr -c "
  CREATE EXTENSION IF NOT EXISTS pg_stat_statements;
  CREATE EXTENSION IF NOT EXISTS pg_trgm;
  
  -- Create indexes for performance
  CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_albums_artist_id ON albums(artist_id);
  CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_tracks_album_id ON tracks(album_id);
  CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_quality_cache_album_id ON quality_cache(album_id);
  
  -- Vacuum and analyze
  VACUUM ANALYZE;
"

# Backup setup
kubectl create cronjob qobuzarr-backup --image=postgres:15 --schedule="0 2 * * *" \
  --restart=OnFailure -- /bin/bash -c "
    pg_dump -h postgres-primary -U qobuzarr qobuzarr | 
    gzip > /backup/qobuzarr-$(date +%Y%m%d-%H%M%S).sql.gz &&
    find /backup -name '*.sql.gz' -mtime +7 -delete
  "
```

## Cloud Deployments

### AWS ECS Deployment
```json
{
  "family": "qobuzarr-lidarr",
  "networkMode": "awsvpc",
  "requiresCompatibilities": ["FARGATE"],
  "cpu": "2048",
  "memory": "4096",
  "executionRoleArn": "arn:aws:iam::account:role/ecsTaskExecutionRole",
  "taskRoleArn": "arn:aws:iam::account:role/qobuzarrTaskRole",
  "containerDefinitions": [
    {
      "name": "lidarr",
      "image": "ghcr.io/hotio/lidarr:pr-plugins",
      "portMappings": [
        {
          "containerPort": 8686,
          "protocol": "tcp"
        }
      ],
      "environment": [
        {"name": "PUID", "value": "1000"},
        {"name": "PGID", "value": "1000"},
        {"name": "TZ", "value": "UTC"}
      ],
      "secrets": [
        {
          "name": "QOBUZ_APP_ID",
          "valueFrom": "arn:aws:secretsmanager:region:account:secret:qobuzarr/api:app_id::"
        },
        {
          "name": "QOBUZ_APP_SECRET", 
          "valueFrom": "arn:aws:secretsmanager:region:account:secret:qobuzarr/api:app_secret::"
        }
      ],
      "mountPoints": [
        {
          "sourceVolume": "config",
          "containerPath": "/config",
          "readOnly": false
        }
      ],
      "logConfiguration": {
        "logDriver": "awslogs",
        "options": {
          "awslogs-group": "/aws/ecs/qobuzarr",
          "awslogs-region": "us-west-2",
          "awslogs-stream-prefix": "lidarr"
        }
      },
      "healthCheck": {
        "command": ["CMD-SHELL", "curl -f http://localhost:8686/ || exit 1"],
        "interval": 30,
        "timeout": 10,
        "retries": 3,
        "startPeriod": 60
      }
    }
  ],
  "volumes": [
    {
      "name": "config",
      "efsVolumeConfiguration": {
        "fileSystemId": "fs-12345678",
        "rootDirectory": "/qobuzarr/config"
      }
    }
  ]
}
```

### Azure Container Instances
```yaml
# azure-deployment.yaml
apiVersion: '2019-12-01'
location: westus2
properties:
  containers:
  - name: lidarr-qobuzarr
    properties:
      image: ghcr.io/hotio/lidarr:pr-plugins
      resources:
        requests:
          cpu: 2
          memoryInGb: 4
      ports:
      - port: 8686
        protocol: TCP
      environmentVariables:
      - name: PUID
        value: '1000'
      - name: PGID
        value: '1000'
      - name: TZ
        value: 'UTC'
      - name: QOBUZ_APP_ID
        secureValue: $(QOBUZ_APP_ID)
      - name: QOBUZ_APP_SECRET
        secureValue: $(QOBUZ_APP_SECRET)
      volumeMounts:
      - name: config-volume
        mountPath: /config
      - name: downloads-volume
        mountPath: /downloads
  osType: Linux
  restartPolicy: Always
  ipAddress:
    type: Public
    ports:
    - protocol: tcp
      port: 8686
  volumes:
  - name: config-volume
    azureFile:
      shareName: qobuzarr-config
      storageAccountName: qobuzarrstg
      storageAccountKey: $(STORAGE_KEY)
  - name: downloads-volume
    azureFile:
      shareName: qobuzarr-downloads
      storageAccountName: qobuzarrstg
      storageAccountKey: $(STORAGE_KEY)
tags:
  Environment: Production
  Application: Qobuzarr
```

### Google Cloud Run
```yaml
# cloudrun-service.yaml
apiVersion: serving.knative.dev/v1
kind: Service
metadata:
  name: qobuzarr-lidarr
  namespace: default
  annotations:
    run.googleapis.com/ingress: all
    run.googleapis.com/execution-environment: gen2
spec:
  template:
    metadata:
      annotations:
        autoscaling.knative.dev/minScale: "1"
        autoscaling.knative.dev/maxScale: "10"
        run.googleapis.com/cpu-throttling: "false"
        run.googleapis.com/memory: "4Gi"
        run.googleapis.com/cpu: "2"
    spec:
      containers:
      - image: ghcr.io/hotio/lidarr:pr-plugins
        ports:
        - containerPort: 8686
        env:
        - name: PUID
          value: "1000"
        - name: PGID
          value: "1000"
        - name: TZ
          value: "UTC"
        - name: QOBUZ_APP_ID
          valueFrom:
            secretKeyRef:
              name: qobuzarr-secrets
              key: app_id
        - name: QOBUZ_APP_SECRET
          valueFrom:
            secretKeyRef:
              name: qobuzarr-secrets
              key: app_secret
        volumeMounts:
        - name: config-volume
          mountPath: /config
        resources:
          limits:
            cpu: "2000m"
            memory: "4Gi"
      volumes:
      - name: config-volume
        nfs:
          server: 10.0.0.10
          path: /qobuzarr/config
```

## Configuration Management

### Environment-Specific Configurations
```bash
# environments/development.json
{
  "Qobuz": {
    "Environment": "Development",
    "EnableDebugLogging": true,
    "ApiTimeout": "00:00:30",
    "MaxRetryAttempts": 1
  },
  "Database": {
    "ConnectionString": "Data Source=qobuzarr-dev.db"
  }
}

# environments/staging.json  
{
  "Qobuz": {
    "Environment": "Staging",
    "EnableDebugLogging": false,
    "ApiTimeout": "00:01:00",
    "MaxRetryAttempts": 2
  },
  "Database": {
    "ConnectionString": "Host=postgres-staging;Database=qobuzarr_staging;Username=qobuzarr;Password=$(POSTGRES_PASSWORD)"
  }
}

# environments/production.json
{
  "Qobuz": {
    "Environment": "Production",
    "EnableDebugLogging": false,
    "ApiTimeout": "00:02:00",
    "MaxRetryAttempts": 3,
    "EnableCircuitBreaker": true
  },
  "Database": {
    "ConnectionString": "Host=postgres-primary;Database=qobuzarr;Username=qobuzarr;Password=$(POSTGRES_PASSWORD);Pooling=true;MinPoolSize=10;MaxPoolSize=100"
  }
}
```

### Secret Management
```bash
# Using Kubernetes secrets
kubectl create secret generic qobuzarr-secrets \
  --from-literal=qobuz-app-id="your_app_id" \
  --from-literal=qobuz-app-secret="your_app_secret" \
  --from-literal=postgres-password="secure_password" \
  -n qobuzarr

# Using Azure Key Vault
az keyvault secret set --vault-name qobuzarr-kv --name "qobuz-app-id" --value "your_app_id"
az keyvault secret set --vault-name qobuzarr-kv --name "qobuz-app-secret" --value "your_app_secret"

# Using AWS Secrets Manager
aws secretsmanager create-secret \
  --name "qobuzarr/api" \
  --description "Qobuz API credentials" \
  --secret-string '{"app_id":"your_app_id","app_secret":"your_app_secret"}'

# Using HashiCorp Vault
vault kv put secret/qobuzarr \
  app_id="your_app_id" \
  app_secret="your_app_secret"
```

## Security Considerations

### Network Security
```yaml
# NetworkPolicy for Kubernetes
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: qobuzarr-network-policy
  namespace: qobuzarr
spec:
  podSelector:
    matchLabels:
      app: lidarr
  policyTypes:
  - Ingress
  - Egress
  ingress:
  - from:
    - namespaceSelector:
        matchLabels:
          name: ingress-nginx
    ports:
    - protocol: TCP
      port: 8686
  egress:
  - to:
    - podSelector:
        matchLabels:
          app: redis
    ports:
    - protocol: TCP
      port: 6379
  - to:
    - podSelector:
        matchLabels:
          app: postgres
    ports:
    - protocol: TCP
      port: 5432
  - to: []  # Allow outbound to Qobuz API
    ports:
    - protocol: TCP
      port: 443
```

### Security Hardening
```dockerfile
# Hardened Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base

# Create non-root user
RUN addgroup -g 1000 qobuzarr && \
    adduser -D -s /bin/sh -u 1000 -G qobuzarr qobuzarr

# Install security updates
RUN apk update && apk upgrade && \
    apk add --no-cache curl && \
    rm -rf /var/cache/apk/*

WORKDIR /app
COPY --chown=qobuzarr:qobuzarr . .

# Remove unnecessary files
RUN find . -name "*.pdb" -delete && \
    find . -name "*.xml" -delete

# Set secure permissions
RUN chmod -R 750 /app && \
    chmod 500 /app/Lidarr.Plugin.Qobuzarr.dll

USER qobuzarr

# Security headers
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:5000
ENV DOTNET_EnableDiagnostics=0

EXPOSE 5000

HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
  CMD curl -f -H "User-Agent: HealthCheck" http://localhost:5000/health || exit 1

ENTRYPOINT ["dotnet", "Lidarr.Plugin.Qobuzarr.dll"]
```

## Monitoring Setup

### Prometheus Configuration
```yaml
# prometheus-config.yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: prometheus-config
  namespace: qobuzarr
data:
  prometheus.yml: |
    global:
      scrape_interval: 15s
    scrape_configs:
    - job_name: 'qobuzarr-lidarr'
      static_configs:
      - targets: ['lidarr-service:9090']
      scrape_interval: 30s
      metrics_path: /metrics
    - job_name: 'qobuzarr-redis'
      static_configs:
      - targets: ['qobuzarr-redis:6379']
    - job_name: 'qobuzarr-postgres'
      static_configs:
      - targets: ['qobuzarr-postgres:5432']
```

### Grafana Dashboards
```json
{
  "dashboard": {
    "title": "Qobuzarr Performance",
    "panels": [
      {
        "title": "API Call Reduction",
        "type": "stat",
        "targets": [
          {
            "expr": "qobuzarr_api_call_reduction_percentage",
            "legendFormat": "Reduction %"
          }
        ]
      },
      {
        "title": "ML Query Optimization Rate",
        "type": "graph",
        "targets": [
          {
            "expr": "rate(qobuzarr_optimized_queries_total[5m])",
            "legendFormat": "Optimized Queries/sec"
          }
        ]
      },
      {
        "title": "Quality Cache Hit Rate",
        "type": "gauge",
        "targets": [
          {
            "expr": "qobuzarr_quality_cache_hit_rate",
            "legendFormat": "Cache Hit Rate"
          }
        ]
      }
    ]
  }
}
```

## Troubleshooting

### Common Deployment Issues

#### Plugin Not Loading
```bash
# Check plugin directory
ls -la /config/plugins/RicherTunes/Qobuzarr/

# Expected files:
# - Lidarr.Plugin.Qobuzarr.dll
# - plugin.json
# - ml-baseline-patterns.json (optional)

# Check Lidarr logs
tail -f /config/logs/lidarr.txt | grep -i qobuz

# Verify plugin compatibility
dotnet --info  # Check .NET version
grep -i version /config/plugins/RicherTunes/Qobuzarr/plugin.json
```

#### Database Connection Issues
```bash
# Test PostgreSQL connection
kubectl exec -it postgres-primary-0 -n qobuzarr -- \
  psql -U qobuzarr -d qobuzarr -c "SELECT version();"

# Test Redis connection  
kubectl exec -it redis-0 -n qobuzarr -- \
  redis-cli -a "$REDIS_PASSWORD" ping

# Check connection strings in configuration
kubectl get configmap qobuzarr-config -o yaml -n qobuzarr
```

#### Performance Issues
```bash
# Monitor resource usage
kubectl top pods -n qobuzarr

# Check ML model performance
curl http://lidarr-service:8686/api/qobuzarr/ml/stats

# Analyze API call patterns
kubectl logs deployment/lidarr -n qobuzarr | grep "API call"

# Review cache performance
redis-cli -a "$REDIS_PASSWORD" info stats
```

#### Authentication Failures
```bash
# Verify Qobuz credentials
kubectl get secret qobuzarr-secrets -o yaml -n qobuzarr
echo "<base64-app-id>" | base64 -d

# Test API connectivity
kubectl exec deployment/lidarr -n qobuzarr -- \
  curl -H "X-App-Id: $QOBUZ_APP_ID" https://api.qobuz.com/api.json/0.2/application/info

# Check authentication logs
kubectl logs deployment/lidarr -n qobuzarr | grep -i "auth\|login\|credential"
```

### Health Checks
```bash
# Application health
curl http://lidarr-service:8686/health

# Detailed health check
curl http://lidarr-service:8686/health/ready

# ML system health
curl http://lidarr-service:8686/api/qobuzarr/health
```

This deployment guide provides comprehensive coverage for deploying Qobuzarr in various environments with proper security, monitoring, and troubleshooting procedures.
