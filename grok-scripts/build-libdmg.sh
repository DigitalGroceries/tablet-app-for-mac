#!/bin/bash
# Build mozilla/libdmg-hfsplus once; sign-pack-*.sh calls /tmp/libdmg/build/dmg/dmg
set -euo pipefail
DEST=${1:-/tmp/libdmg}
rm -rf "$DEST"
git clone --depth 1 https://github.com/mozilla/libdmg-hfsplus.git "$DEST"
cd "$DEST"
# Pin used on Grok's computer 24 Oct 2026 (e18579b Merge pull request #25 from fqueze/parallel-extract)
git fetch --depth 1 origin e18579b391950589b9ed16ee630c54ce4319bb29
git checkout e18579b391950589b9ed16ee630c54ce4319bb29
mkdir -p build && cd build
cmake ..
make -j"$(nproc)"
test -x dmg/dmg
echo "OK: $DEST/build/dmg/dmg"
