# Grok handover (26 Oct 2026)

Written when Claude asked for branch `grok-handover` after Grok hit a usage stop mid–Version 14.

## Where things stood when I stopped
- Official Latest on GitHub was still Version 13 (tag `v13`). Live work tree on my computer: `/workspace/otaE/sweep`.
- At Zak’s stop for Claude: pushed **v13-source** at `b12ba5fa06185166f8ae8100f3bbc3942f5b67f6` from worktree `/workspace/tam-v13` (no build, no Mac copy that turn).
- Version 14 backlog Zak had just given me (then unfinished on my side): duplicate access prompts; Applications Command multi-select; replace site Pen shot with Applications shot; per-pen profiles; Device/Tool +/−. Claude completed that backlog on branch **v14** (`1d2f09d`) — see Mac `/Users/Zak/Grok/scratch/v14-source/HANDOVER-CLAUDE-V14.md`.

## Pack / site scripts on my computer
- Sign/pack scripts live under `/workspace/otaE/V7/` (`sign-pack-v13.sh` and earlier). Copies also in repo `grok-scripts/` on this branch.
- Site checkout: `/workspace/tam` (branch `gh-pages`).
- Live app source when packing: `/workspace/otaE/sweep` (or a checkout of `v14` / `v13-source`).

## Zak rules noted this day
- Always keep up-to-date code and everything else in `/Users/Zak/Grok` on the Mac, never only on my computer.
- Usage-limit handover (both of us): full report in the other’s chat/diary + code on GitHub and `/Users/Zak/Grok`.

## What Claude should assume I did not finish
- No V14 pre-release upload from me (until Zak says).
- No Mac install/test of V14.
- My interrupted V14 worker did not leave a usable tree; take Claude’s `v14` / Mac bundle as source of truth.

---

## How to build, package and publish

**Clone path note:** scripts hard-code `/workspace/otaE/site/` (tools + `docs/assets/icon-256.png`), `/workspace/otaE/sweep/` (app + `Assets/manual/`), `/workspace/otaE/V7/` (pack out), `/workspace/otaE/R18/for-claude/` (build-history / licences), `/workspace/otaE/V5/shots-used/` (Home shots), and `/workspace/tam` (live `gh-pages`); on a clone replace those with this repo’s `grok-site/` (mirrors `site/`), your app checkout, `grok-scripts/`, and your Pages checkout.

Copy-paste commands as run on Grok Bot’s Linux computer. Paths below are absolute on that machine. Repo: `DigitalGroceries/tablet-app-for-mac`. Scripts in this branch: `grok-scripts/`.

### (1) Tools and versions

```bash
# .NET SDK (self-contained publish for osx-arm64 / osx-x64)
/home/box/.dotnet/dotnet --version
# → 10.0.402
export PATH="/home/box/.dotnet:$PATH"

# rcodesign (ad-hoc Apple code signing on Linux) — either path works
/home/box/.cargo/bin/rcodesign --version
# → apple-codesign 0.29.0
# also: /workspace/tools/apple-codesign-0.29.0-x86_64-unknown-linux-musl/rcodesign
# sign-pack-v13.sh uses: RC=$HOME/.cargo/bin/rcodesign

# genisoimage (HFS/Apple ISO for the .dmg window)
genisoimage --version
# → genisoimage 1.1.11 (Linux)
# Debian package: genisoimage

# Python helpers for Finder layout (.DS_Store)
python3 -c 'import ds_store, mac_alias; print("ok")'
# pip packages: ds_store 1.3.3, mac_alias 2.2.3
# pip3 install --user 'ds_store==1.3.3' 'mac_alias==2.2.3'

# Pillow (site shot resize / WebP) + pngquant optional for Help PNG fallbacks
python3 -c 'from PIL import Image; print(Image.__version__)'
# Chrome headless for the manual PDF
command -v google-chrome

# cmake / openssl / zlib (for libdmg-hfsplus)
cmake --version   # 3.31.6 on this box
# packages used: cmake, libssl-dev, zlib1g-dev
```

**libdmg-hfsplus** (converts the Apple ISO into a compressed `.dmg`):

```bash
# One-time build (script also in grok-scripts/build-libdmg.sh)
rm -rf /tmp/libdmg
git clone https://github.com/mozilla/libdmg-hfsplus.git /tmp/libdmg
cd /tmp/libdmg
# Commit used on this computer (24 Oct 2026):
git checkout e18579b391950589b9ed16ee630c54ce4319bb29
mkdir -p build && cd build
cmake ..
make -j"$(nproc)"
test -x /tmp/libdmg/build/dmg/dmg
# Binary path every sign-pack-*.sh calls:
#   /tmp/libdmg/build/dmg/dmg
# Or: bash /path/to/repo/grok-scripts/build-libdmg.sh /tmp/libdmg
```

Repo URL: `https://github.com/mozilla/libdmg-hfsplus.git`  
Pinned commit: `e18579b391950589b9ed16ee630c54ce4319bb29` (`e18579b Merge pull request #25 from fqueze/parallel-extract`).

### (2) Publish the Mac binaries (dotnet → sign → zip → .dmg)

Working dirs Grok uses:

| Role | Path |
|------|------|
| App source | `/workspace/otaE/sweep` (or a clone of branch `v14` / `v13-source`) |
| Pack scripts + `dmgroot/` | `/workspace/otaE/V7` (mirrors in `grok-scripts/`) |
| Published binaries | `/workspace/otaE/V7/pub/app-{arm64,x64}` and `pub/daemon-{arm64,x64}` |
| Pack output (V13) | `/workspace/otaE/V7/v13/` |

**Step A — publish both architectures** (`grok-scripts/build-pub.sh`, or):

```bash
export PATH="/home/box/.dotnet:$PATH"
cd /workspace/otaE/sweep   # app source with OpenTabletArtist/ + external/OpenTabletDriver/
P=/workspace/otaE/V7/pub
VER=13                     # set to the version number baked into the assembly (14 for V14)
for a in arm64 x64; do
  rm -rf "$P/app-$a" "$P/daemon-$a"
  dotnet publish OpenTabletArtist/OpenTabletArtist.csproj \
    -c Release -r "osx-$a" --self-contained true \
    -p:PublishSingleFile=true -p:Version=$VER \
    -o "$P/app-$a" -nologo -v q
  dotnet publish external/OpenTabletDriver/OpenTabletDriver.Daemon/OpenTabletDriver.Daemon.csproj \
    -c Release -r "osx-$a" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$P/daemon-$a" -nologo -v q
done
```

**Step B — assemble app, ad-hoc sign, zip, and .dmg** — run `sign-pack-v13.sh` (for Version 13 identifiers). Core of that script:

```bash
cd /workspace/otaE/V7
RC=$HOME/.cargo/bin/rcodesign
BID=com.freetabletapp.mac
# After filling out/ from dmgroot + pub/ and signing:
# Volume name on the .dmg window (exact string):
#   "Tablet App for Mac"
rm -rf dmgstage; mkdir dmgstage
cp -a "out/arm64/Tablet App for Mac.app" dmgstage/
ln -s /Applications dmgstage/Applications
python3 mkdsstore.py dmgstage/.DS_Store
# mkdsstore.py: 128px icons, 16pt labels, dark grey background; app icon at (170,170), Applications at (470,170)
genisoimage -quiet -V "Tablet App for Mac" -D -R -apple -no-pad -o /tmp/v7.iso dmgstage
/tmp/libdmg/build/dmg/dmg dmg /tmp/v7.iso v13/Tablet-App-for-Mac.dmg
# Intel pass uses the same volume name; zip/dmg get -Intel suffix.
```

Full one-shot (preferred):

```bash
bash /workspace/otaE/V7/sign-pack-v13.sh
# or from a clone: bash grok-scripts/sign-pack-v13.sh
# (script cds to /workspace/otaE/V7 — adjust paths if you relocated)
```

Signing identifiers (V13+): main `@main:com.freetabletapp.mac`; daemon `com.freetabletapp.mac.daemon`.  
Outputs: `Tablet-App-for-Mac.dmg` / `.zip` (Apple silicon), `Tablet-App-for-Mac-Intel.dmg` / `.zip`, `SHA256SUMS`, and the manual PDF copy.

### (3) Publish a GitHub release (`gh`)

The in-app **Check for updates now** (and the weekly check) call:

`https://api.github.com/repos/DigitalGroceries/tablet-app-for-mac/releases/latest`

So only a release that GitHub marks as **Latest** (not draft, not pre-release) is seen by the updater. It parses `tag_name` as an integer version (`v13` → 13), then downloads **`Tablet-App-for-Mac.zip`** (silicon) or **`Tablet-App-for-Mac-Intel.zip`**, and checks **`SHA256SUMS`**.

**Official Latest** (example Version 13):

```bash
# notes file = release body (Download link, install steps, What's new, checksums pointer)
gh release create v13 \
  --repo DigitalGroceries/tablet-app-for-mac \
  --title "Version 13" \
  --notes-file /tmp/v13-release-body.md \
  /workspace/otaE/V7/v13/Tablet-App-for-Mac.dmg \
  /workspace/otaE/V7/v13/Tablet-App-for-Mac.zip \
  /workspace/otaE/V7/v13/Tablet-App-for-Mac-Intel.dmg \
  /workspace/otaE/V7/v13/Tablet-App-for-Mac-Intel.zip \
  /workspace/otaE/V7/v13/tablet-app-for-mac-manual-version-7.pdf \
  /workspace/otaE/V7/v13/SHA256SUMS
# Do NOT pass --prerelease for Latest.
```

**Test / pre-release** (updater ignores these — never Latest):

```bash
gh release create v14-test1 \
  --repo DigitalGroceries/tablet-app-for-mac \
  --title "Version 14 - test 1 (not for general use)" \
  --notes-file /tmp/v14-test1-notes.md \
  --prerelease \
  /Users/Zak/Grok/scratch/v14-test1/Tablet-App-for-Mac.zip \
  /Users/Zak/Grok/scratch/v14-test1/SHA256SUMS
# Wrapper: bash grok-scripts/publish-github-release.sh v14-test1 "Version 14 - test 1 (not for general use)" notes.md DIR --prerelease
```

After creating Latest, site download URL `.../releases/latest/download/Tablet-App-for-Mac.dmg` follows automatically.

### (4) Rebuild and push the website (gh-pages)

- Live Pages branch checkout: **`/workspace/tam`** on **`gh-pages`**.
- Generators: `grok-scripts/build_site.v6.py` and `grok-scripts/help_chapters.py` (also under `/workspace/otaE/V7/` and `/workspace/otaE/site/tools/build_site.py`).
- Prefer the site tools tree when rebuilding Help chapters:

```bash
cd /workspace/tam
git checkout gh-pages
git pull --ff-only origin gh-pages

# Full static rebuild into the Pages tree (SITE_OUT = this checkout):
SITE_OUT=/workspace/tam python3 /workspace/otaE/site/tools/build_site.py
# Or the V7 copy (writes to /workspace/otaE/docs by default — then rsync):
#   python3 /workspace/otaE/V7/build_site.v6.py
#   rsync -a --delete /workspace/otaE/docs/ /workspace/tam/ --exclude .git

# Image steps (also done inside build_site / help_chapters):
# Home slots: source PNGs → max width 1200, WebP quality 78 + PNG
# Help manual figures: max width 960, WebP quality 72 + palette PNG fallback
# Drop a new shot as e.g. assets source then re-run the generator, or:
python3 - <<'PY'
from PIL import Image
sid, src = "05-pen-applications", "/path/to/shot.png"
im = Image.open(src).convert("RGB")
if im.width > 1200:
    im = im.resize((1200, round(im.height * 1200 / im.width)), Image.LANCZOS)
out = "/workspace/tam/assets/shots"
im.save(f"{out}/{sid}.png", optimize=True, compress_level=9)
im.save(f"{out}/{sid}.webp", quality=78, method=6)
print("wrote", sid, im.size)
PY

git add -A
git -c user.name=DigitalGroceries -c user.email=222060208+DigitalGroceries@users.noreply.github.com \
  commit -m "Site: Version N Latest — short summary"
git push origin HEAD:gh-pages
```

Bump version strings / “What’s new” in the generator (or edit `index.html` / `releases/` if doing a minimal patch) before rebuild. Do **not** restore UNDER CONSTRUCTION. Silicon-only download wording. Logic notes: Mac `/Users/Zak/Grok` / box `logic-github-page.md` §11–12 for shot sizes.

### (5) Manual PDF from `Assets/manual/manual.md`

Source markdown (in the app tree):  
`/workspace/otaE/sweep/OpenTabletArtist/Assets/manual/manual.md`  
(images next to it under `Assets/manual/img/` etc.)

Script: `grok-scripts/make_pdf_v7.py` (copies paths for Version 7 branding; update the cover “Version N” string when shipping a new PDF).

```bash
# Needs google-chrome on PATH; writes:
#   /workspace/otaE/V7/manual/tablet-app-for-mac-manual-version-7.pdf
python3 /workspace/otaE/V7/manual/make_pdf_v7.py
# or: python3 grok-scripts/make_pdf_v7.py
# Then copy into the pack out/ folder before sha256sum / gh release:
cp /workspace/otaE/V7/manual/tablet-app-for-mac-manual-version-7.pdf /workspace/otaE/V7/v13/
```

The script builds `/tmp/manual7.html` from `manual.md` (via `build_site.md` helpers) and prints with:

`google-chrome --headless=new --no-sandbox --allow-file-access-from-files --no-pdf-header-footer --print-to-pdf=...`

`BANNER = ''` — do not put UNDER CONSTRUCTION back on the PDF cover.
