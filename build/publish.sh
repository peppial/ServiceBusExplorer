#!/usr/bin/env bash
# Builds a self-contained, ready-to-share package for one platform.
#   build/publish.sh <rid> [version]
# rid: win-x64 | win-arm64 | osx-arm64 | osx-x64 | linux-x64 | linux-arm64
# Output: artifacts/dist/ServiceBusExplorer-<version>-<rid>.(zip|tar.gz)
set -euo pipefail

rid="${1:?usage: build/publish.sh <rid> [version]}"
root="$(cd "$(dirname "$0")/.." && pwd)"
version="${2:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props")}"
name="ServiceBusExplorer-$version-$rid"
publish="$root/artifacts/publish/$rid"
dist="$root/artifacts/dist"
rm -rf "$publish" && mkdir -p "$publish" "$dist"

# macOS keeps native libraries next to the executable inside the bundle; elsewhere one file is simpler to hand out.
single_file=true
[[ "$rid" == osx-* ]] && single_file=false

dotnet publish "$root/src/SbExplorer.App/SbExplorer.App.csproj" \
  -c Release -r "$rid" --self-contained \
  -p:PublishSingleFile="$single_file" \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none \
  -p:Version="$version" \
  -o "$publish"

case "$rid" in
  osx-*)
    app="$root/artifacts/publish/$rid-app/Service Bus Explorer.app"
    rm -rf "$(dirname "$app")" && mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
    cp -R "$publish/." "$app/Contents/MacOS/"
    cp "$root/src/SbExplorer.App/Assets/icon.icns" "$app/Contents/Resources/icon.icns"
    sed "s/__VERSION__/$version/g" "$root/build/macos/Info.plist" > "$app/Contents/Info.plist"
    # Ad-hoc signature so Apple Silicon will launch it; it is not notarized (see README).
    if command -v codesign >/dev/null; then
      codesign --force --deep --sign - "$app"
    fi
    (cd "$(dirname "$app")" && rm -f "$dist/$name.zip" && \
      if command -v ditto >/dev/null; then ditto -c -k --keepParent "$(basename "$app")" "$dist/$name.zip"; \
      else zip -qry "$dist/$name.zip" "$(basename "$app")"; fi)
    ;;
  win-*)
    (cd "$publish" && rm -f "$dist/$name.zip" && zip -q "$dist/$name.zip" ServiceBusExplorer.exe)
    ;;
  linux-*)
    staging="$root/artifacts/publish/$rid-tar/$name"
    rm -rf "$(dirname "$staging")" && mkdir -p "$staging"
    cp "$publish/ServiceBusExplorer" "$staging/"
    cp "$root/src/SbExplorer.App/Assets/icon.png" "$staging/servicebusexplorer.png"
    cp "$root/build/linux/servicebusexplorer.desktop" "$root/build/linux/install.sh" "$staging/"
    tar -C "$(dirname "$staging")" -czf "$dist/$name.tar.gz" "$name"
    ;;
  *)
    echo "Unknown runtime identifier: $rid" >&2
    exit 1
    ;;
esac

echo "Built $(ls "$dist/$name".* )"
