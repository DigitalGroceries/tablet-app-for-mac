#!/bin/bash
# Version 7: assemble the universal bundle, sign every Mach-O with the project certificate, build zip + dmg + SHA256SUMS.
set -euo pipefail
cd /workspace/otaE/V7
RC=$HOME/.cargo/bin/rcodesign; PEM=$HOME/.signing/release-signing.pem; BID=com.freetabletapp.mac
OUT=out; APP="$OUT/Tablet App for Mac.app"; rm -rf $OUT; mkdir -p $OUT
cp -a "dmgroot/Tablet App for Mac.app" "$APP"; rm -rf "$APP/Contents/_CodeSignature"
M="$APP/Contents/MacOS"; rm -rf "$M/Daemon"; mkdir -p "$M/Daemon/arm64" "$M/Daemon/x64"
cp launcher/OpenTabletArtist "$M/OpenTabletArtist"
for a in arm64 x64; do cp pub/app-$a/OpenTabletArtist "$M/OpenTabletArtist-$a"; cp pub/daemon-$a/OpenTabletDriver.Daemon "$M/Daemon/$a/OpenTabletDriver.Daemon"; done
cp pub/app-arm64/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
[ -f parts/OpenTabletArtist.Dynamics.dll ] && cp parts/OpenTabletArtist.Dynamics.dll "$APP/Contents/Resources/BundledPlugins/OpenTabletArtistDynamics/OpenTabletArtist.Dynamics.dll"
chmod 755 "$M/OpenTabletArtist" "$M"/OpenTabletArtist-* "$M"/Daemon/*/OpenTabletDriver.Daemon
$RC sign --timestamp-url none --pem-file $PEM \
  --binary-identifier "@main:$BID" \
  --binary-identifier "Contents/MacOS/OpenTabletArtist-arm64:$BID" --binary-identifier "Contents/MacOS/OpenTabletArtist-x64:$BID" \
  --binary-identifier "Contents/MacOS/Daemon/arm64/OpenTabletDriver.Daemon:OpenTabletDriver" --binary-identifier "Contents/MacOS/Daemon/x64/OpenTabletDriver.Daemon:OpenTabletDriver" \
  "$APP"
$RC verify "$APP/Contents/MacOS/OpenTabletArtist" 2>&1 | tail -1
for f in "$M/OpenTabletArtist" "$M"/OpenTabletArtist-* "$M"/Daemon/*/OpenTabletDriver.Daemon; do
  echo "== $f"; $RC print-signature-info "$f" 2>/dev/null | grep -iE "^ *identifier:|requirement|common|team" | sort -u | head -8
done
rm -rf release; mkdir release
(cd $OUT && python3 - <<'P'
import zipfile,os,stat
z=zipfile.ZipFile('../release/Tablet-App-for-Mac.zip','w',zipfile.ZIP_DEFLATED)
root='Tablet App for Mac.app'
for d,ds,fs in os.walk(root):
    for n in sorted(ds+fs):
        p=os.path.join(d,n); st=os.lstat(p)
        zi=zipfile.ZipInfo(p+('/' if os.path.isdir(p) and not os.path.islink(p) else ''))
        zi.external_attr=(st.st_mode & 0xFFFF)<<16; zi.date_time=(2026,10,23,12,0,0)
        if os.path.islink(p): zi.external_attr|=0xA000<<16; z.writestr(zi,os.readlink(p))
        elif os.path.isdir(p): z.writestr(zi,'')
        else: z.writestr(zi,open(p,'rb').read(),compress_type=zipfile.ZIP_DEFLATED)
z.close()
P
)
rm -rf dmgstage; mkdir dmgstage; cp -a "$APP" dmgstage/; ln -s /Applications dmgstage/Applications
genisoimage -quiet -V "Tablet App for Mac" -D -R -apple -no-pad -o /tmp/v7.iso dmgstage
/tmp/libdmg/build/dmg/dmg /tmp/v7.iso release/Tablet-App-for-Mac.dmg >/dev/null
cp manual/tablet-app-for-mac-manual-version-7.pdf release/
(cd release && sha256sum Tablet-App-for-Mac.dmg Tablet-App-for-Mac.zip tablet-app-for-mac-manual-version-7.pdf > SHA256SUMS && cat SHA256SUMS)
echo PACKDONE
