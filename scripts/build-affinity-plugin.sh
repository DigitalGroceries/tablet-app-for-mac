#!/usr/bin/env bash
# Builds the "Smooth pen strokes" Affinity plugin (#39) into
# OpenTabletArtist/Assets/affinity-plugin/SmoothPenStrokes.plugin, where the app embeds it and copies it
# into Affinity's plugin folder when the user asks. Works on Linux (clang + ld64.lld) and on a Mac (Xcode clang).
set -euo pipefail
cd "$(dirname "$0")/.."
SRC=tools/affinity-plugin
OUT=OpenTabletArtist/Assets/affinity-plugin/SmoothPenStrokes.plugin
NAME=SmoothPenStrokes
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

for arch in arm64 x86_64; do
  target=$([ $arch = arm64 ] && echo arm64-apple-macos11 || echo x86_64-apple-macos10.15)
  clang --target=$target -ffreestanding -fno-stack-protector -fblocks -O2 -fvisibility=hidden -c "$SRC/smooth_strokes.c" -o "$TMP/$arch.o"
  if [ "$(uname)" = Darwin ]; then
    clang --target=$target -bundle -lobjc -Wl,-exported_symbol,_PluginMain "$TMP/$arch.o" -o "$TMP/$arch"
  else
    LD=$(command -v ld64.lld || ls /usr/lib/llvm-*/bin/ld64.lld | head -1)
    minos=$([ $arch = arm64 ] && echo 11.0 || echo 10.15)
    # Links against small .tbd stubs of libSystem and libobjc (just the symbols used), as Xcode would
    # against the real ones, so no macOS SDK is needed.
    "$LD" -arch $arch -platform_version macos $minos $minos -bundle \
      "$SRC/libSystem.B.tbd" "$SRC/libobjc.A.tbd" -exported_symbol _PluginMain "$TMP/$arch.o" -o "$TMP/$arch"
  fi
done
LIPO=$(command -v lipo || command -v llvm-lipo || ls /usr/lib/llvm-*/bin/llvm-lipo | head -1)

rm -rf "$OUT"
mkdir -p "$OUT/Contents/MacOS" "$OUT/Contents/Resources/en.lproj"
"$LIPO" -create "$TMP/arm64" "$TMP/x86_64" -output "$OUT/Contents/MacOS/$NAME"
chmod 755 "$OUT/Contents/MacOS/$NAME"
python3 "$SRC/make_rsrc.py" "$OUT/Contents/Resources/$NAME.rsrc"
cp "$SRC/Info.plist" "$OUT/Contents/Info.plist"
printf '8BFM8BIM' > "$OUT/Contents/PkgInfo"
printf '"CFBundleName" = "Smooth pen strokes";\n' > "$OUT/Contents/Resources/en.lproj/InfoPlist.strings"
echo "Built $OUT"
