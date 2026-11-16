#!/bin/bash
# Runs all 6 test suites and fails if any test fails (Zak, 16 Nov 2026: the team must never again miss a failing
# suite). The same suites run on GitHub (.github/workflows/build.yml); scripts/pack-macos-linux.sh runs this first.
# Usage: scripts/run-all-tests.sh
set -uo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
# The .NET SDK from global.json is usually installed under $HOME/.dotnet (as in pack-macos-linux.sh).
[ -d "$HOME/.dotnet" ] && export PATH="$HOME/.dotnet:$PATH"
failed=()
run() { echo "== $1"; (cd "$ROOT/$2" && dotnet test "$3" -c Release) || failed+=("$1"); }
run "App tests"               .                        tests/OpenTabletArtist.Tests/OpenTabletArtist.Tests.csproj
run "UI tests"                .                        tests/OpenTabletArtist.UiTests/OpenTabletArtist.UiTests.csproj
run "OtdInterop tests"        .                        tests/OtdInterop.Tests/OtdInterop.Tests.csproj
run "OtdHealth tests"         .                        tests/OtdHealth.Tests/OtdHealth.Tests.csproj
run "OtdHealth.Collector tests" .                      tests/OtdHealth.Collector.Tests/OtdHealth.Collector.Tests.csproj
# Needs OpenTabletDriver's own global.json (VSTest), so it runs from that folder.
run "Driver tests"            external/OpenTabletDriver OpenTabletDriver.Tests/OpenTabletDriver.Tests.csproj
if [ ${#failed[@]} -gt 0 ]; then printf 'FAILED: %s\n' "${failed[@]}" >&2; exit 1; fi
echo "All 6 test suites passed."
