#!/bin/bash
# R10: fails if a single-file publish left any native library (or other runtime file) beside the
# executable — the Mac install copies only the executable into the bundle, so a loose .dylib means a
# startup crash. Usage: tools/check-single-file-publish.sh <publish dir> [exe name]
# The app icon (AppIcon.icns / AppIcon.iconset) is copied on purpose: the packer puts it in Resources.
set -euo pipefail
D=${1:?publish dir}; EXE=${2:-OpenTabletArtist}
[ -x "$D/$EXE" ] || { echo "FAIL: $D/$EXE missing"; exit 1; }
extra=$(cd "$D" && ls -1 | grep -v -x "$EXE" | grep -v '\.pdb$' | grep -v -x -e AppIcon.icns -e AppIcon.iconset || true)
if [ -n "$extra" ]; then echo "FAIL: files beside the single-file exe (would be missing from the bundle):"; echo "$extra"; exit 1; fi
# libAvaloniaNative exists only for macOS; require it when the exe is Mach-O (R11: Linux publish check).
libs="libSkiaSharp libHarfBuzzSharp"
magic=$(od -An -tx1 -N4 "$D/$EXE" | tr -d ' \n')
case "$magic" in cffaedfe|cefaedfe|feedfacf|feedface) libs="$libs libAvaloniaNative";; esac
for lib in $libs; do
  grep -a -q "$lib" "$D/$EXE" || { echo "FAIL: $lib not embedded in $EXE"; exit 1; }
done
echo "OK: single file, natives embedded"
