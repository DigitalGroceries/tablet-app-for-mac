#!/bin/bash
# Version 14 (Claude): build, assemble, ad-hoc sign and zip "Tablet App for Mac.app" on Linux.
# Same layout and signing identifiers as Grok's grok-scripts/sign-pack-v13.sh (Version 13), minus the .dmg step.
#
# Needs: .NET SDK from global.json ($HOME/.dotnet), rcodesign (cargo install apple-codesign; 0.29.0 used).
# Usage: scripts/pack-macos-linux.sh <version> <out-dir> [arm64|x64 ...]
#   e.g. scripts/pack-macos-linux.sh 14 /tmp/v14 arm64
#   BETA build: BETA=2 scripts/pack-macos-linux.sh 14 /tmp/v14b2 arm64  (the app then knows it is "14 BETA 2",
#   so its update check offers the stable version, newer BETAs and going back to stable; tag it v14-beta2).
set -euo pipefail
VER=${1:?version, e.g. 14}
OUT=${2:?output folder}
shift 2
ARCHES=${*:-arm64}
BETA=${BETA:-0}
INFO=$VER; [ "$BETA" != 0 ] && INFO="$VER-beta$BETA"
ROOT=$(cd "$(dirname "$0")/.." && pwd)
RC=${RCODESIGN:-$HOME/.cargo/bin/rcodesign}
BID=com.freetabletapp.mac
export PATH=$HOME/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1
mkdir -p "$OUT"

dotnet build "$ROOT/plugins/OpenTabletArtist.Dynamics" -c Release -nologo -v q -o "$OUT/plugin"

for a in $ARCHES; do
  suf=""; [ "$a" = x64 ] && suf="-Intel"
  P="$OUT/pub-$a"; rm -rf "$P"
  dotnet publish "$ROOT/OpenTabletArtist/OpenTabletArtist.csproj" -c Release -r osx-$a --self-contained true \
    -p:PublishSingleFile=true -p:Version="$VER" -p:InformationalVersion="$INFO" -o "$P/app" -nologo -v q
  dotnet publish "$ROOT/external/OpenTabletDriver/OpenTabletDriver.Daemon/OpenTabletDriver.Daemon.csproj" -c Release -r osx-$a \
    --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$P/daemon" -nologo -v q

  D="$OUT/$a"; APP="$D/Tablet App for Mac.app"; rm -rf "$D"; mkdir -p "$D"
  cp -a "$ROOT/grok-scripts/dmgroot/Tablet App for Mac.app" "$APP"
  rm -rf "$APP/Contents/_CodeSignature" "$APP/Contents/Library"
  M="$APP/Contents/MacOS"; mkdir -p "$M/Daemon/$a" "$APP/Contents/Resources/BundledPlugins/OpenTabletArtistDynamics"
  cp "$P/app/OpenTabletArtist" "$M/OpenTabletArtist"
  cp "$P/daemon/OpenTabletDriver.Daemon" "$M/Daemon/$a/OpenTabletDriver.Daemon"
  cp "$OUT/plugin/OpenTabletArtist.Dynamics.dll" "$APP/Contents/Resources/BundledPlugins/OpenTabletArtistDynamics/"
  [ -f "$P/app/AppIcon.icns" ] && cp "$P/app/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
  chmod 755 "$M/OpenTabletArtist" "$M/Daemon/$a/OpenTabletDriver.Daemon"
  sed -i "s#<key>CFBundleVersion</key><string>[^<]*</string>#<key>CFBundleVersion</key><string>$VER</string>#; s#<key>CFBundleShortVersionString</key><string>[^<]*</string>#<key>CFBundleShortVersionString</key><string>$VER</string>#" "$APP/Contents/Info.plist"

  # Daemon first via its own identifier, then the bundle (rcodesign does both in one pass, nested first).
  "$RC" sign --binary-identifier "@main:$BID" \
    --binary-identifier "Contents/MacOS/Daemon/$a/OpenTabletDriver.Daemon:$BID.daemon" "$APP" >/dev/null

  (cd "$D" && python3 - "$OUT/Tablet-App-for-Mac$suf.zip" <<'PY'
import zipfile, os, sys
z = zipfile.ZipFile(sys.argv[1], 'w', zipfile.ZIP_DEFLATED); root = 'Tablet App for Mac.app'
for d, ds, fs in os.walk(root):
    for n in sorted(ds + fs):
        p = os.path.join(d, n); st = os.lstat(p)
        zi = zipfile.ZipInfo(p + ('/' if os.path.isdir(p) and not os.path.islink(p) else ''))
        zi.external_attr = (st.st_mode & 0xFFFF) << 16; zi.date_time = (2026, 10, 26, 12, 0, 0)
        if os.path.islink(p): zi.external_attr |= 0xA000 << 16; z.writestr(zi, os.readlink(p))
        elif os.path.isdir(p): z.writestr(zi, '')
        else: z.writestr(zi, open(p, 'rb').read(), compress_type=zipfile.ZIP_DEFLATED)
z.close()
PY
  )
  echo "== $a"; "$RC" print-signature-info "$M/OpenTabletArtist" 2>/dev/null | grep -E "identifier:|flags: Code" | sort -u
  "$RC" print-signature-info "$M/Daemon/$a/OpenTabletDriver.Daemon" 2>/dev/null | grep -E "identifier:" | sort -u
done
(cd "$OUT" && sha256sum Tablet-App-for-Mac*.zip > SHA256SUMS && cat SHA256SUMS)
echo PACKDONE
