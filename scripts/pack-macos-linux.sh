#!/bin/bash
# Build, assemble, sign and zip "Tablet App for Mac.app" on Linux; stage the Finder DMG layout.
# Same layout and signing identifiers as Grok's grok-scripts/sign-pack-v13.sh (Version 13), minus the .dmg step.
#
# Needs: .NET SDK from global.json ($HOME/.dotnet), rcodesign, Python ds_store and mac_alias.
# Finish the staged DMG on macOS with scripts/pack-macos-dmg.sh (explicit HFS+, never APFS/ISO).
# Usage: scripts/pack-macos-linux.sh <version> <out-dir> [arm64]
#   e.g. scripts/pack-macos-linux.sh 23 /tmp/v23 arm64
#   BETA build: BETA=yes scripts/pack-macos-linux.sh 23 /tmp/v23b arm64  (the app then knows it is "23 BETA",
#   so its update check offers the stable version, newer BETAs and going back to stable; tag it v23-beta).
#   Version names are simple (the owner, 4 Nov 2026): "Version 23 BETA", then "Version 23"; no BETA numbers.
#   Apple silicon only: no Intel builds (the owner, 4 Nov 2026).
#   (Older numbered BETAs: BETA=2 gives "-beta2".)
#   Test build for Zak: TEST=yes scripts/pack-macos-linux.sh 26 /tmp/b8 arm64  (About says "Version 26 test").
set -euo pipefail
VER=${1:?version, e.g. 14}
OUT=${2:?output folder}
shift 2
ARCHES=${*:-arm64}
BETA=${BETA:-0}
INFO=$VER
case "$BETA" in 0|no|"") ;; yes) INFO="$VER-beta" ;; *) INFO="$VER-beta$BETA" ;; esac
# Unpublished test build for Zak: TEST=yes gives "26-test", and Home's About line says "Version 26 test".
# Zak, 4 and 12 Nov 2026: a version name is one number, or one number and one word; test builds never carry a build
# number (the team counts builds in its own notes, not in the app). Never publish a test build.
TEST=${TEST:-}
[ -z "$TEST" ] || { [ "$BETA" = 0 ] || [ "$BETA" = no ] || [ -z "$BETA" ] || { echo "TEST and BETA can't be used together." >&2; exit 1; }; INFO="$VER-test"; }
for a in $ARCHES; do [ "$a" = arm64 ] || { echo "Apple silicon only: '$a' is not built (no Intel builds)." >&2; exit 1; }; done
ROOT=$(cd "$(dirname "$0")/.." && pwd)
RC=${RCODESIGN:-$HOME/.cargo/bin/rcodesign}
BID=com.freetabletapp.mac
# Issue #14: sign with ONE stable self-signed certificate (kept outside the repo, never committed) so the
# designated requirement is "identifier X and certificate root = H\"…\"" instead of the build's cdhash.
# macOS permissions are tied to that requirement, so they can survive updates. Set SIGN_PEM to override.
SIGN_PEM=${SIGN_PEM:-$HOME/.signing/tablet-app-stable.pem}
[ -f "$SIGN_PEM" ] || { echo "Missing signing key $SIGN_PEM (make it once, private to you: umask 077; mkdir -p ~/.signing; rcodesign generate-self-signed-certificate --person-name 'Tablet App for Mac' --validity-days 7300 --pem-filename ~/.signing/tablet-app-stable, then cat the .crt and .key into the .pem and delete the separate .key)" >&2; exit 1; }
# Item 8 (#15): the .pem holds the private key. Never sign with a key inside the repo, and keep the key and
# its folder readable by us only (600 / 700), so another account on the build machine can't copy it.
PEM_REAL=$(readlink -f "$SIGN_PEM")
case "$PEM_REAL/" in "$ROOT"/*) echo "Signing key $SIGN_PEM is inside the repo; move it outside (e.g. ~/.signing) and never commit it." >&2; exit 1;; esac
if [ -n "$(find "$PEM_REAL" -perm /077 2>/dev/null)" ]; then
  chmod 600 "$PEM_REAL" && echo "Signing key was readable by others; tightened to 600: $PEM_REAL" >&2
fi
PEM_DIR=$(dirname "$PEM_REAL")
if [ "$PEM_DIR" != "$HOME" ] && [ -O "$PEM_DIR" ] && [ -n "$(find "$PEM_DIR" -maxdepth 0 -perm /077 2>/dev/null)" ]; then
  chmod 700 "$PEM_DIR" && echo "Signing key folder was open to others; tightened to 700: $PEM_DIR" >&2
fi
export PATH=$HOME/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1
mkdir -p "$OUT"
python3 -c "import ds_store, mac_alias"

dotnet build "$ROOT/plugins/OpenTabletArtist.Dynamics" -c Release -nologo -v q -o "$OUT/plugin"

for a in $ARCHES; do
  suf=""
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
  # Document icon for .tabletsettings files (CFBundleDocumentTypes in Info.plist).
  cp "$ROOT/packaging/macos/SettingsDocument.icns" "$APP/Contents/Resources/SettingsDocument.icns"
  chmod 755 "$M/OpenTabletArtist" "$M/Daemon/$a/OpenTabletDriver.Daemon"
  sed -i "s#<key>CFBundleVersion</key><string>[^<]*</string>#<key>CFBundleVersion</key><string>$VER</string>#; s#<key>CFBundleShortVersionString</key><string>[^<]*</string>#<key>CFBundleShortVersionString</key><string>$VER</string>#" "$APP/Contents/Info.plist"

  # Daemon first via its own identifier, then the bundle (rcodesign does both in one pass, nested first).
  "$RC" sign --timestamp-url none --pem-file "$SIGN_PEM" --binary-identifier "@main:$BID" \
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
  # Layout is outside the already signed app; the updater ZIP above is unchanged.
  # Transfer this stage intact to a Mac, then use pack-macos-dmg.sh. Do not let
  # hdiutil choose its filesystem: APFS images showed Finder defaults despite .DS_Store.
  STAGE="$OUT/dmgstage-$a"
  rm -rf "$STAGE"; mkdir -p "$STAGE"
  cp -a "$APP" "$STAGE/"
  ln -s /Applications "$STAGE/Applications"
  python3 "$ROOT/grok-scripts/mkdsstore.py" "$STAGE/.DS_Store"
  echo "DMG stage: $STAGE (on macOS: scripts/pack-macos-dmg.sh <stage> <output.dmg>)"
  echo "== $a"; "$RC" print-signature-info "$M/OpenTabletArtist" 2>/dev/null | grep -E "identifier:|flags: Code" | sort -u
  "$RC" print-signature-info "$M/Daemon/$a/OpenTabletDriver.Daemon" 2>/dev/null | grep -E "identifier:" | sort -u
  # Must say "certificate root = H…", never cdhash, or permissions are lost on every update.
  "$RC" print-signature-info "$M/OpenTabletArtist" 2>/dev/null | grep -E "designated" | sort -u
done
(cd "$OUT" && sha256sum Tablet-App-for-Mac*.zip > SHA256SUMS && cat SHA256SUMS)
echo PACKDONE
