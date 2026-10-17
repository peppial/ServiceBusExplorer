#!/usr/bin/env bash
# Regenerates the app icon assets. The .icns step needs macOS (iconutil).
set -euo pipefail
cd "$(dirname "$0")/../.."
assets=src/SbExplorer.App/Assets
dotnet run build/icon/make-icon.cs -- "$assets"
if command -v iconutil >/dev/null; then
  iconutil -c icns "$assets/icon.iconset" -o "$assets/icon.icns"
  echo "Wrote $assets/icon.icns"
fi
rm -rf "$assets/icon.iconset"
