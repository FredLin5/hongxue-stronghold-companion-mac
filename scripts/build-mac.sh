#!/bin/bash
# Builds the macOS port as a self-contained .app bundle (Apple Silicon).
#
#   scripts/build-mac.sh              # build + bundle into dist/
#   scripts/build-mac.sh --run        # build + bundle + launch
#
# Requirements: .NET 10 SDK (brew install dotnet), Xcode command line tools (sips, iconutil).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

APP_NAME="鸿雪·卫戍助手"
BUNDLE_ID="com.hongxue.stronghold"
DIST="$ROOT/dist"
APP="$DIST/$APP_NAME.app"
PUBLISH="$ROOT/.tmp/publish-mac"
RID="${RID:-osx-arm64}"

export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$ROOT/.dotnet-home}"
DOTNET="${DOTNET:-dotnet}"

echo "==> publish ($RID, self-contained)"
rm -rf "$PUBLISH"
"$DOTNET" publish src/ASPAssistant.Mac/ASPAssistant.Mac.csproj \
    -c Release -r "$RID" --self-contained true \
    -p:EnableWindowsTargeting=false \
    -o "$PUBLISH" -v q

echo "==> assemble bundle"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"

# Icon: PNG -> .iconset -> .icns
ICON_SRC="$ROOT/data/icons/aspa_logo.png"
ICONSET="$ROOT/.tmp/AppIcon.iconset"
rm -rf "$ICONSET"; mkdir -p "$ICONSET"
for size in 16 32 64 128 256 512; do
    sips -z $size $size "$ICON_SRC" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
    dbl=$((size * 2))
    sips -z $dbl $dbl "$ICON_SRC" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>$APP_NAME</string>
    <key>CFBundleDisplayName</key><string>$APP_NAME</string>
    <key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
    <key>CFBundleExecutable</key><string>HongxueStronghold</string>
    <key>CFBundleIconFile</key><string>AppIcon</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>0.8.0</string>
    <key>CFBundleVersion</key><string>1</string>
    <key>LSMinimumSystemVersion</key><string>12.0</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>NSSupportsAutomaticGraphicsSwitching</key><true/>
</dict>
</plist>
PLIST

# Ad-hoc signature so Gatekeeper and the PlayCover network client behave on a local build.
codesign --force --deep --sign - "$APP" >/dev/null 2>&1 || echo "warning: ad-hoc codesign failed (app still runs locally)"

echo "==> done: $APP"
du -sh "$APP"

if [ "${1:-}" = "--run" ]; then
    open "$APP"
fi
