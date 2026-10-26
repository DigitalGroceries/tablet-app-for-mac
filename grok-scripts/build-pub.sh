#!/bin/bash
set -euo pipefail
cd /workspace/otaE/sweep
P=/workspace/otaE/V7/pub
VER=${VER:-11}
for a in arm64 x64; do
  rm -rf $P/app-$a $P/daemon-$a
  dotnet publish OpenTabletArtist/OpenTabletArtist.csproj -c Release -r osx-$a --self-contained true -p:PublishSingleFile=true -p:Version=$VER -o $P/app-$a -nologo -v q
  dotnet publish external/OpenTabletDriver/OpenTabletDriver.Daemon/OpenTabletDriver.Daemon.csproj -c Release -r osx-$a --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $P/daemon-$a -nologo -v q
done
ls -la $P/*
echo PUBDONE
