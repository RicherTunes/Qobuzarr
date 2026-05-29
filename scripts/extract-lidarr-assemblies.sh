#!/usr/bin/env bash
set -euo pipefail
# Qobuzarr shim — delegates to the shared superset in Lidarr.Plugin.Common.
# Default output dir matches qobuzarr CI: ext/Lidarr/_output/net8.0
#
# Force --mode full (appended last so it wins over any caller-supplied --mode):
# Qobuzarr references Newtonsoft.Json, which the host provides but which the
# shared script's "minimal" asset list does NOT copy. ILRepack
# (PluginPackaging.targets) must still resolve Newtonsoft.Json against its
# LibraryPath during the merge, so the release fails with
# "Failed to resolve assembly: 'Newtonsoft.Json, Version=13.0.0.0'" under
# minimal extraction. Full extraction copies every host DLL, giving ILRepack
# all referenced libraries. Non-merged host assemblies are still excluded from
# the final package by packaging/expected-contents.txt.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec bash "$SCRIPT_DIR/../ext/Lidarr.Plugin.Common/scripts/extract-lidarr-assemblies.sh" \
  --output-dir "ext/Lidarr/_output/net8.0" \
  --plugin-name "Qobuzarr" \
  "$@" \
  --mode full
