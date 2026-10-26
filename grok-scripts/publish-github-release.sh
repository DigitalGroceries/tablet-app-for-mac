#!/bin/bash
# Publish a GitHub release for DigitalGroceries/tablet-app-for-mac from a packed out/ dir.
# Usage:
#   ./publish-github-release.sh v13 "Version 13" notes.md /workspace/otaE/V7/v13
#   ./publish-github-release.sh v14-test1 "Version 14 - test 1 (not for general use)" notes.md /path/to/dir --prerelease
set -euo pipefail
TAG=$1; TITLE=$2; NOTES=$3; DIR=$4; shift 4 || true
EXTRA=("$@")
REPO=DigitalGroceries/tablet-app-for-mac
# Assets the updater and site expect (silicon zip name is Tablet-App-for-Mac.zip)
shopt -s nullglob
ASSETS=("$DIR"/Tablet-App-for-Mac.dmg "$DIR"/Tablet-App-for-Mac.zip
        "$DIR"/Tablet-App-for-Mac-Intel.dmg "$DIR"/Tablet-App-for-Mac-Intel.zip
        "$DIR"/tablet-app-for-mac-manual*.pdf "$DIR"/SHA256SUMS)
gh release create "$TAG" "${ASSETS[@]}" \
  --repo "$REPO" \
  --title "$TITLE" \
  --notes-file "$NOTES" \
  "${EXTRA[@]}"
echo "Created https://github.com/$REPO/releases/tag/$TAG"
