#!/usr/bin/env bash
# Assemble a macOS .app bundle from an already-published, self-contained output directory.
# Called by the BundleMacApp MSBuild target after `dotnet publish` for an osx-* RID (also usable standalone).
# NOT signed/notarized — that's only needed to distribute; a locally-built bundle runs fine (V2 = shipping).
#
# Usage: bundle-macos-app.sh <publishDir> <appBundlePath> [version]
set -euo pipefail

PUBLISH_DIR="${1:?publish dir required}"
APP="${2:?app bundle path required}"
VERSION="${3:-0.0.0}"
HERE="$(cd "$(dirname "$0")" && pwd)"
ICON_SRC="$HERE/../OpenTabletArtist/Assets/appicon.png"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH_DIR"/. "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/OpenTabletArtist"

# R17: prefer the prebuilt icon-v2 AppIcon.icns (all sizes rendered from appicon.svg);
# fall back to building one from the 1024 px appicon.png.
PREBUILT_ICNS="$HERE/../OpenTabletArtist/Assets/macos/AppIcon.icns"
DOC_ICNS="$HERE/../packaging/macos/SettingsDocument.icns"
if [[ -f "$PREBUILT_ICNS" ]]; then
  cp "$PREBUILT_ICNS" "$APP/Contents/Resources/AppIcon.icns"
  [ -f "$DOC_ICNS" ] && cp "$DOC_ICNS" "$APP/Contents/Resources/SettingsDocument.icns"
else
  ICONSET="$(mktemp -d)/OpenTabletArtist.iconset"; mkdir -p "$ICONSET"
  for s in 16 32 128 256 512; do
    sips -z "$s" "$s"             "$ICON_SRC" --out "$ICONSET/icon_${s}x${s}.png"    >/dev/null
    sips -z "$((s*2))" "$((s*2))" "$ICON_SRC" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
  done
  iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
fi

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Tablet App for Mac</string>
  <key>CFBundleDisplayName</key><string>Tablet App for Mac</string>
  <key>CFBundleIdentifier</key><string>com.freetabletapp.mac</string>
  <key>CFBundleVersion</key><string>${VERSION}</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>CFBundleExecutable</key><string>OpenTabletArtist</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeName</key><string>Tablet App settings</string>
      <key>CFBundleTypeRole</key><string>Editor</string>
      <key>LSHandlerRank</key><string>Owner</string>
      <key>CFBundleTypeIconFile</key><string>SettingsDocument</string>
      <key>LSItemContentTypes</key><array><string>com.freetabletapp.mac.tabletsettings</string></array>
    </dict>
  </array>
  <key>UTExportedTypeDeclarations</key>
  <array>
    <dict>
      <key>UTTypeIdentifier</key><string>com.freetabletapp.mac.tabletsettings</string>
      <key>UTTypeDescription</key><string>Tablet App settings</string>
      <key>UTTypeIconFile</key><string>SettingsDocument</string>
      <key>UTTypeConformsTo</key><array><string>public.zip-archive</string><string>public.data</string></array>
      <key>UTTypeTagSpecification</key>
      <dict><key>public.filename-extension</key><array><string>tabletsettings</string></array></dict>
    </dict>
  </array>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>15.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSApplicationCategoryType</key><string>public.app-category.graphics-design</string>
</dict>
</plist>
PLIST

echo "Bundled $APP (v${VERSION})"
