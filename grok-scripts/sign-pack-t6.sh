#!/bin/bash
# Version 7 test 6: no bundled launch agent (start at login is the ordinary login item).
# Version 7 test 4 (dmg layout: 128px icons, 16pt labels, dark window via mkdsstore.py). Was test 3: one ad-hoc signed app per Mac type, laid out like Version 6 (the main executable IS the .NET app;
# no launcher), so the login agent, menu-bar icon and permissions see one program. Builds dmg + zip per type.
set -euo pipefail
cd /workspace/otaE/V7
RC=$HOME/.cargo/bin/rcodesign; BID=com.freetabletapp.mac
rm -rf out test6; mkdir -p out test6
for a in arm64 x64; do
  suf=""; [ $a = x64 ] && suf="-Intel"
  D=out/$a; APP="$D/Tablet App for Mac.app"; mkdir -p $D
  cp -a "dmgroot/Tablet App for Mac.app" "$APP"; rm -rf "$APP/Contents/_CodeSignature" "$APP/Contents/Library/LaunchAgents"; rmdir "$APP/Contents/Library" 2>/dev/null || true
  M="$APP/Contents/MacOS"; rm -rf "$M"/*; mkdir -p "$M/Daemon/$a"
  cp pub/app-$a/OpenTabletArtist "$M/OpenTabletArtist"
  cp pub/daemon-$a/OpenTabletDriver.Daemon "$M/Daemon/$a/OpenTabletDriver.Daemon"
  cp pub/app-arm64/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
  [ -f parts/OpenTabletArtist.Dynamics.dll ] && cp parts/OpenTabletArtist.Dynamics.dll "$APP/Contents/Resources/BundledPlugins/OpenTabletArtistDynamics/OpenTabletArtist.Dynamics.dll"
  chmod 755 "$M/OpenTabletArtist" "$M/Daemon/$a/OpenTabletDriver.Daemon"
  $RC sign --binary-identifier "@main:$BID" --binary-identifier "Contents/MacOS/Daemon/$a/OpenTabletDriver.Daemon:OpenTabletDriver" "$APP" >/dev/null
  (cd $D && python3 - "../../test6/Tablet-App-for-Mac$suf.zip" <<'P'
import zipfile,os,sys
z=zipfile.ZipFile(sys.argv[1],'w',zipfile.ZIP_DEFLATED); root='Tablet App for Mac.app'
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
  rm -rf dmgstage; mkdir dmgstage; cp -a "$APP" dmgstage/; ln -s /Applications dmgstage/Applications; python3 mkdsstore.py dmgstage/.DS_Store
  genisoimage -quiet -V "Tablet App for Mac" -D -R -apple -no-pad -o /tmp/v7.iso dmgstage
  /tmp/libdmg/build/dmg/dmg /tmp/v7.iso "test6/Tablet-App-for-Mac$suf.dmg" >/dev/null
  echo "== $a"; file "$M/OpenTabletArtist" | cut -c1-140; $RC print-signature-info "$M/OpenTabletArtist" 2>/dev/null | grep -E "flags: Code|identifier:" | sort -u
done
cp manual/tablet-app-for-mac-manual-version-7.pdf test6/
(cd test6 && sha256sum *.dmg *.zip *.pdf > SHA256SUMS && cat SHA256SUMS)
echo PACKDONE
