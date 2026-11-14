#!/bin/bash
# Rebuilds nothing: launches the already-built app on a given tab and captures its window.
# usage: tools/mac-dev/shot.sh <tab> <out.png>
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
APP="$ROOT/src/ASPAssistant.Mac/bin/Debug/net8.0/HongxueStronghold"
TAB="${1:-operators}"
OUT="${2:-/tmp/app-$TAB.png}"
pkill -f "HongxueStronghold" 2>/dev/null || true
sleep 1
ASPASSISTANT_SETTINGS_DIR="$ROOT/.run-settings" \
ASPASSISTANT_MAC_START_PAGE="$TAB" \
DOTNET_CLI_HOME="$ROOT/.dotnet-home" \
DOTNET_ROOT="${DOTNET_ROOT:-$(brew --prefix dotnet 2>/dev/null || true)/libexec}" \
  nohup "$APP" > "/tmp/macapp-$TAB.log" 2>&1 &
sleep 9
mkdir -p "$ROOT/.tmp/clang" "$ROOT/.tmp/swiftcache"
WID=$(TMPDIR="$ROOT/.tmp" swiftc -module-cache-path "$ROOT/.tmp/swiftcache" \
  -Xcc -fmodules-cache-path="$ROOT/.tmp/clang" "$ROOT/tools/mac-dev/winlist.swift" \
  -o "$ROOT/.tmp/winlist" 2>/dev/null && "$ROOT/.tmp/winlist" | grep HongxueStronghold | head -1 | awk -F'|' '{gsub(/ /,"",$1); print $1}')
if [ -z "$WID" ]; then echo "window not found; log:"; tail -20 "/tmp/macapp-$TAB.log"; exit 1; fi
screencapture -x -o -l "$WID" "$OUT"
echo "captured $OUT (window $WID)"
