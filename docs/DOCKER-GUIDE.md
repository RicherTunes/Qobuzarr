# Docker Deployment Guide for Qobuzzarr

## Overview

This guide covers deploying Lidarr with Qobuzzarr plugin using Docker, including various deployment scenarios and configurations.

## Quick Start

### Using Docker Compose (Recommended)

```yaml
version: '3.8'
services:
  lidarr:
    image: ghcr.io/hotio/lidarr:pr-plugins
    container_name: lidarr
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=America/New_York
    volumes:
      - ./config:/config
      - ./music:/music
      - ./downloads:/downloads
      - ./plugins:/config/plugins
    ports:
      - 8686:8686
    restart: unless-stopped
```

### Manual Plugin Installation

```bash
# Create directories
mkdir -p ./config/plugins

# Download plugin
wget https://github.com/richertunes/qobuzarr/releases/latest/download/Qobuzarr.dll \
  -O ./plugins/Qobuzarr.dll

# Start container
docker-compose up -d
```

## Deployment Scenarios

### 1. Standalone Lidarr

```yaml
version: '3.8'
services:
  lidarr:
    image: ghcr.io/hotio/lidarr:pr-plugins
    container_name: lidarr-qobuz
    environment:
      - PUID=${PUID:-1000}
      - PGID=${PGID:-1000}
      - TZ=${TZ:-UTC}
      - UMASK=002
    volumes:
      - ${CONFIG_PATH:-./config}:/config
      - ${MUSIC_PATH:-/mnt/music}:/music
      - ${DOWNLOADS_PATH:-./downloads}:/downloads
    ports:
      - ${LIDARR_PORT:-8686}:8686
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8686/ping"]
      interval: 30s
      timeout: 10s
      retries: 3
```

### 2. Full *arr Stack

```yaml
version: '3.8'

networks:
  media:
    driver: bridge

services:
  lidarr:
    image: ghcr.io/hotio/lidarr:pr-plugins
    container_name: lidarr
    networks:
      - media
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=America/New_York
    volumes:
      - ./config/lidarr:/config
      - /mnt/media/music:/music
      - ./downloads:/downloads
      - ./plugins:/config/plugins
    ports:
      - 8686:8686
    restart: unless-stopped

  prowlarr:
    image: ghcr.io/hotio/prowlarr:latest
    container_name: prowlarr
    networks:
      - media
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=America/New_York
    volumes:
      - ./config/prowlarr:/config
    ports:
      - 9696:9696
    restart: unless-stopped

  qbittorrent:
    image: ghcr.io/hotio/qbittorrent:latest
    container_name: qbittorrent
    networks:
      - media
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=America/New_York
      - WEBUI_PORT=8080
    volumes:
      - ./config/qbittorrent:/config
      - ./downloads:/downloads
    ports:
      - 8080:8080
      - 6881:6881
      - 6881:6881/udp
    restart: unless-stopped
```

### 3. With VPN

```yaml
version: '3.8'

services:
  vpn:
    image: ghcr.io/bubuntux/nordvpn:latest
    container_name: vpn
    cap_add:
      - NET_ADMIN
    environment:
      - USER=${VPN_USER}
      - PASS=${VPN_PASS}
      - COUNTRY=Netherlands
      - TECHNOLOGY=NordLynx
    ports:
      - 8686:8686  # Lidarr
    sysctls:
      - net.ipv4.conf.all.src_valid_mark=1
    restart: unless-stopped

  lidarr:
    image: ghcr.io/hotio/lidarr:pr-plugins
    container_name: lidarr
    network_mode: service:vpn
    depends_on:
      - vpn
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=America/New_York
    volumes:
      - ./config:/config
      - ./music:/music
      - ./downloads:/downloads
      - ./plugins:/config/plugins
    restart: unless-stopped
```

## Plugin Management

### Automated Plugin Installation

```dockerfile
# Dockerfile.lidarr-qobuz
FROM ghcr.io/hotio/lidarr:pr-plugins

# Install plugin
RUN mkdir -p /config/plugins && \
    wget -O /config/plugins/Qobuzarr.dll \
    https://github.com/richertunes/qobuzarr/releases/latest/download/Qobuzarr.dll

# Set permissions
RUN chown -R abc:abc /config/plugins
```

Build and use:
```bash
docker build -f Dockerfile.lidarr-qobuz -t lidarr-qobuz:latest .
docker run -d --name lidarr lidarr-qobuz:latest
```

### Plugin Update Script

```bash
#!/bin/bash
# update-plugin.sh

CONTAINER_NAME="lidarr"
PLUGIN_URL="https://github.com/richertunes/qobuzarr/releases/latest/download/Qobuzarr.dll"

# Download latest plugin
wget -O Lidarr.Plugin.Qobuz.dll.new "$PLUGIN_URL"

# Backup existing
docker exec $CONTAINER_NAME cp /config/plugins/Lidarr.Plugin.Qobuz.dll /config/plugins/Lidarr.Plugin.Qobuz.dll.bak

# Copy new plugin
docker cp Lidarr.Plugin.Qobuz.dll.new $CONTAINER_NAME:/config/plugins/Lidarr.Plugin.Qobuz.dll

# Restart container
docker restart $CONTAINER_NAME

# Cleanup
rm Lidarr.Plugin.Qobuz.dll.new
```

## Environment Variables

### Standard Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `PUID` | 1000 | User ID |
| `PGID` | 1000 | Group ID |
| `TZ` | UTC | Timezone |
| `UMASK` | 002 | File permissions mask |

### Qobuzzarr Specific

```yaml
environment:
  # Plugin configuration
  - QOBUZ_APP_ID=285473059
  - QOBUZ_APP_SECRET=your_secret
  - QOBUZ_LOG_LEVEL=Info
  
  # Performance tuning
  - QOBUZ_CACHE_SIZE=1000
  - QOBUZ_RATE_LIMIT=60
  
  # Network settings
  - QOBUZ_PROXY=http://proxy:8080
  - QOBUZ_TIMEOUT=30
```

## Volume Mounts

### Required Volumes

```yaml
volumes:
  - /path/to/config:/config      # Lidarr configuration
  - /path/to/music:/music        # Music library
  - /path/to/downloads:/downloads # Download directory
```

### Optional Volumes

```yaml
volumes:
  - /path/to/plugins:/config/plugins  # Plugin directory
  - /path/to/backups:/backups         # Backup location
  - /path/to/logs:/config/logs        # Log persistence
```

### Permissions

Ensure correct permissions:
```bash
# Set ownership
sudo chown -R 1000:1000 ./config ./music ./downloads

# Set permissions
sudo chmod -R 755 ./config
sudo chmod -R 775 ./music ./downloads
```

## Networking

### Bridge Network (Default)

```yaml
services:
  lidarr:
    ports:
      - 8686:8686
```

### Host Network

```yaml
services:
  lidarr:
    network_mode: host
    environment:
      - LIDARR_PORT=8686
```

### Custom Network

```yaml
networks:
  media:
    driver: bridge
    ipam:
      config:
        - subnet: 172.20.0.0/16

services:
  lidarr:
    networks:
      media:
        ipv4_address: 172.20.0.10
```

### Reverse Proxy (Traefik)

```yaml
services:
  lidarr:
    labels:
      - traefik.enable=true
      - traefik.http.routers.lidarr.rule=Host(`lidarr.example.com`)
      - traefik.http.routers.lidarr.entrypoints=websecure
      - traefik.http.routers.lidarr.tls=true
      - traefik.http.services.lidarr.loadbalancer.server.port=8686
```

## Health Checks

### Docker Health Check

```yaml
healthcheck:
  test: ["CMD-SHELL", "curl -f http://localhost:8686/api/v1/system/status?apikey=$$LIDARR_API_KEY || exit 1"]
  interval: 30s
  timeout: 10s
  retries: 3
  start_period: 40s
```

### External Monitoring

```yaml
services:
  healthchecks:
    image: ghcr.io/linuxserver/healthchecks:latest
    container_name: healthchecks
    environment:
      - PUID=1000
      - PGID=1000
      - SITE_ROOT=https://health.example.com
    volumes:
      - ./healthchecks:/config
    ports:
      - 8000:8000
```

## Backup Strategies

### Automated Backups

```yaml
services:
  backup:
    image: offen/docker-volume-backup:latest
    container_name: backup
    environment:
      - BACKUP_SOURCES=/backup
      - BACKUP_CRON_EXPRESSION=0 2 * * *
      - BACKUP_RETENTION_DAYS=7
    volumes:
      - ./config:/backup/config:ro
      - ./backups:/archive
      - /var/run/docker.sock:/var/run/docker.sock:ro
```

### Manual Backup Script

```bash
#!/bin/bash
# backup-lidarr.sh

BACKUP_DIR="./backups/$(date +%Y%m%d_%H%M%S)"
mkdir -p "$BACKUP_DIR"

# Stop container
docker stop lidarr

# Backup config
docker run --rm \
  -v lidarr_config:/config \
  -v "$BACKUP_DIR":/backup \
  alpine tar czf /backup/config.tar.gz -C /config .

# Restart container
docker start lidarr

echo "Backup completed: $BACKUP_DIR"
```

## Troubleshooting

### Container Logs

```bash
# View logs
docker logs lidarr

# Follow logs
docker logs -f lidarr

# Last 100 lines
docker logs --tail 100 lidarr

# With timestamps
docker logs -t lidarr
```

### Debug Mode

```yaml
environment:
  - DEBUG=true
  - LOG_LEVEL=debug
  - QOBUZ_LOG_LEVEL=Debug
```

### Common Issues

**Plugin not loading:**
```bash
# Check plugin file exists
docker exec lidarr ls -la /config/plugins/

# Check permissions
docker exec lidarr stat /config/plugins/Lidarr.Plugin.Qobuz.dll

# View Lidarr logs
docker exec lidarr cat /config/logs/lidarr.txt | grep -i plugin
```

**Permission errors:**
```bash
# Fix ownership
docker exec lidarr chown abc:abc /config/plugins/Lidarr.Plugin.Qobuz.dll

# Fix permissions  
docker exec lidarr chmod 644 /config/plugins/Lidarr.Plugin.Qobuz.dll
```

**Network issues:**
```bash
# Test connectivity
docker exec lidarr ping -c 4 www.qobuz.com

# Check DNS
docker exec lidarr nslookup www.qobuz.com
```

## Performance Optimization

### Resource Limits

```yaml
services:
  lidarr:
    deploy:
      resources:
        limits:
          cpus: '2.0'
          memory: 2G
        reservations:
          cpus: '0.5'
          memory: 512M
```

### Caching

```yaml
volumes:
  - type: tmpfs
    target: /tmp
    tmpfs:
      size: 100M
```

## Security

### Read-Only Root Filesystem

```yaml
services:
  lidarr:
    read_only: true
    tmpfs:
      - /tmp
      - /var/log
    volumes:
      - ./config:/config
      - ./music:/music:ro
      - ./downloads:/downloads
```

### User Namespace

```yaml
services:
  lidarr:
    userns_mode: host
    user: "1000:1000"
```

## Examples

### Development Setup

```bash
# Clone and build
git clone https://github.com/richertunes/qobuzarr.git
cd qobuzarr

# Build plugin
docker run --rm \
  -v "$PWD":/src \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:6.0 \
  dotnet build -c Release

# Copy to Lidarr
docker cp bin/Release/net6.0/Lidarr.Plugin.Qobuz.dll lidarr:/config/plugins/

# Restart
docker restart lidarr
```

### Production Deployment

See `docker-compose.prod.yml` in the repository for a complete production setup with:
- Automatic updates
- Health monitoring  
- Backup automation
- Reverse proxy
- Security hardening