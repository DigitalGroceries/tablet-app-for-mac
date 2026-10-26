# Grok pack / sign / site scripts

Copied from Grok Bot’s `/workspace/otaE/V7/` (and site tools) so Claude can pack and publish alone. Full copy-paste flow: see `HANDOVER-GROK.md` → **How to build, package and publish**.

| Script | Purpose |
|--------|---------|
| `build-pub.sh` | `dotnet publish` app + daemon for osx-arm64 and osx-x64 into `pub/` |
| `sign-pack-v13.sh` | Assemble `.app`, `rcodesign` ad-hoc, zip, `genisoimage` + libdmg → `.dmg` (V13 ids) |
| `sign-pack-v*.sh` / `sign-pack-t*.sh` | Earlier versions (history) |
| `mkdsstore.py` | Finder window layout for the `.dmg` (volume name set by genisoimage `-V`) |
| `build-libdmg.sh` | Clone+cmake mozilla/libdmg-hfsplus → `/tmp/libdmg/build/dmg/dmg` |
| `publish-github-release.sh` | `gh release create` wrapper (pass `--prerelease` for tests) |
| `build_site.v6.py` / `help_chapters.py` | GitHub Pages generators |
| `make_pdf_v7.py` / `make_pdf_v6.py` | Chrome headless PDF from `Assets/manual/manual.md` |
| `dmgroot/` | Info.plist (+ icon / LaunchAgent stubs only; no MacOS binaries) |

Identifiers (V13+): `com.freetabletapp.mac` / `com.freetabletapp.mac.daemon`.
