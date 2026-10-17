#!/usr/bin/env bash
# Installs Service Bus Explorer for the current user (~/.local).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
prefix="${PREFIX:-$HOME/.local}"
install -Dm755 "$here/ServiceBusExplorer" "$prefix/bin/ServiceBusExplorer"
install -Dm644 "$here/servicebusexplorer.png" "$prefix/share/icons/hicolor/512x512/apps/servicebusexplorer.png"
sed "s|^Exec=.*|Exec=$prefix/bin/ServiceBusExplorer|" "$here/servicebusexplorer.desktop" \
  | install -Dm644 /dev/stdin "$prefix/share/applications/servicebusexplorer.desktop"
echo "Installed to $prefix/bin/ServiceBusExplorer"
