#!/bin/bash
# Finish a signed pack-macos-linux.sh DMG stage on macOS. Never re-sign or
# modify the app here. HFS+ is explicit so Finder can use the root .DS_Store
# (128px icons, 16pt labels, dark background, app left, Applications right).
# Usage: scripts/pack-macos-dmg.sh <dmgstage-arm64> <output.dmg>
set -euo pipefail
STAGE=${1:?signed app + Applications link + .DS_Store stage}
OUT=${2:?output .dmg}
[ "$(uname -s)" = Darwin ] || { echo "Run this imaging step on macOS" >&2; exit 1; }
[ -s "$STAGE/.DS_Store" ] || { echo "Missing staged Finder layout" >&2; exit 1; }
[ -L "$STAGE/Applications" ] && [ "$(readlink "$STAGE/Applications")" = /Applications ] || {
  echo "Missing Applications symlink" >&2; exit 1;
}
codesign --verify --deep --strict "$STAGE/Tablet App for Mac.app"
hdiutil create -ov -fs HFS+ -format UDZO -imagekey zlib-level=9 \
  -volname "Tablet App for Mac" -srcfolder "$STAGE" "$OUT"
hdiutil verify "$OUT"
