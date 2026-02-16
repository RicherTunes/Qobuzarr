#!/usr/bin/env bash
set -euo pipefail

# Qobuzarr helper: Extract Lidarr assemblies from the plugins Docker image
# Usage: ./scripts/extract-lidarr-assemblies.sh [--output-dir <path>] [--mode minimal|full]

OUT_DIR="ext/Lidarr/_output/net8.0"
MODE="minimal"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --output-dir)
      OUT_DIR="$2"; shift 2 ;;
    --mode)
      MODE="$2"; shift 2 ;;
    *)
      echo "Unknown argument: $1"; exit 2 ;;
  esac
done

mkdir -p "$OUT_DIR"

# Required assemblies
REQ=(
  Lidarr.dll
  Lidarr.Common.dll
  Lidarr.Core.dll
  Lidarr.Http.dll
  Lidarr.Api.V1.dll
  Lidarr.Host.dll
  NLog.dll
  FluentValidation.dll
)

# Optional Microsoft.Extensions assemblies
OPT=(
  Microsoft.Extensions.Caching.Memory.dll
  Microsoft.Extensions.Caching.Abstractions.dll
  Microsoft.Extensions.DependencyInjection.Abstractions.dll
  Microsoft.Extensions.Logging.Abstractions.dll
  Microsoft.Extensions.Options.dll
  Microsoft.Extensions.Primitives.dll
)

LIDARR_DOCKER_VERSION="${LIDARR_DOCKER_VERSION:-pr-plugins-3.1.2.4913}"
IMAGE="ghcr.io/hotio/lidarr:${LIDARR_DOCKER_VERSION}"

echo "Extracting Lidarr assemblies from: $IMAGE"

# Retry helper for flaky docker pulls
docker_pull_retry() {
  local image="$1"
  local attempts=${2:-3}
  local delay=${3:-3}
  local n=1
  until [ $n -gt $attempts ]; do
    if docker pull "$image" >/dev/null; then
      return 0
    fi
    echo "docker pull failed for $image (attempt $n/$attempts), retrying in ${delay}s..."
    sleep "$delay"
    n=$((n+1))
  done
  return 1
}

if ! docker_pull_retry "$IMAGE"; then
  echo "Failed to pull Docker image: $IMAGE" >&2
  exit 1
fi

CONTAINER="qobuzarr-extract-$$"
docker create --name "$CONTAINER" "$IMAGE" >/dev/null

copy_from_container() {
  local file="$1"
  docker cp "$CONTAINER:/app/bin/${file}" "$OUT_DIR/" 2>/dev/null || return 1
}

if [[ "$MODE" == "full" ]]; then
  echo "Mode=full: copying entire /app/bin"
  docker cp "$CONTAINER:/app/bin/." "$OUT_DIR/"
else
  echo "Mode=minimal: copying required assemblies"
  for f in "${REQ[@]}"; do
    copy_from_container "$f" || echo "Warning: $f not found (optional)"
  done
  for f in "${OPT[@]}"; do
    copy_from_container "$f" || true
  done
fi

docker rm -f "$CONTAINER" >/dev/null || true

# Sanity check
if [[ ! -f "$OUT_DIR/Lidarr.Core.dll" ]]; then
  echo "Missing Lidarr.Core.dll after extraction" >&2
  exit 1
fi

echo "Assemblies extracted to: $OUT_DIR"
ls -la "$OUT_DIR"

# Guardrail: fail if Docker-extracted assemblies are not .NET 8.
# Check Lidarr.runtimeconfig.json (plain text JSON) rather than binary DLL
# metadata, which uses UTF-16 and breaks grep.
RC="$OUT_DIR/Lidarr.runtimeconfig.json"
if [[ -f "$RC" ]]; then
  if grep -qE '"version":\s*"8\.' "$RC"; then
    echo "[guardrail] OK: Lidarr runtime targets .NET 8"
  else
    echo "FATAL: Lidarr runtime does not target .NET 8 — the Docker image is likely a .NET 6 build." >&2
    echo "Docker tag: ${LIDARR_DOCKER_VERSION}" >&2
    echo "Runtime config:" >&2
    cat "$RC" >&2
    exit 1
  fi
else
  echo "[guardrail] Lidarr.runtimeconfig.json not in output; skipping .NET version check"
fi

# Guardrail: FluentValidation must be 9.5.4.* (host-boundary package).
# The Lidarr host ships FV 9.5.4 — compiling plugins against a different major
# version risks MissingMethodException at runtime.
FV_DLL="$OUT_DIR/FluentValidation.dll"
if [[ -f "$FV_DLL" ]]; then
  # ProductVersion is embedded as ASCII in the .NET assembly PE metadata
  FV_VER=$(strings "$FV_DLL" 2>/dev/null | grep -oE '^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$' | head -1 || true)
  if [[ -z "$FV_VER" ]]; then
    echo "[guardrail] WARNING: Could not read FluentValidation version from DLL metadata"
  elif [[ "$FV_VER" == 9.5.4.* ]]; then
    echo "[guardrail] OK: FluentValidation version $FV_VER (matches host 9.5.4)"
  else
    echo "FATAL: FluentValidation version $FV_VER does not match host expectation 9.5.4.*" >&2
    echo "The Lidarr Docker image ships a different FV version than expected." >&2
    echo "Docker tag: ${LIDARR_DOCKER_VERSION}" >&2
    exit 1
  fi
else
  echo "[guardrail] FluentValidation.dll not in output; skipping FV version check"
fi

# Create manifest
{
  echo "Qobuzarr Assemblies Manifest"
  echo "Date: $(date -u +'%Y-%m-%dT%H:%M:%SZ')"
  echo "Mode: $MODE"
  echo "Image: $IMAGE"
  echo "Files:"
  ls -1 "$OUT_DIR" | sed 's/^/  - /'
} > "$OUT_DIR/MANIFEST.txt"
