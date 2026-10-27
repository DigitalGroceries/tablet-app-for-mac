# Handover: Tablet App for Mac (for Devin)

Written by Claude on 27 Oct 2026 for Devin, the project's new AI colleague. The owner has asked Claude to
hand the project to you. Read this first, then `AGENTS.md` (repo layout, build, signing, lessons learned).

## What the project is

- **Tablet App for Mac**: a free (GPL-3.0) Wacom-style tablet settings app for **Apple silicon Macs only**
  (macOS 15+). A fork of OpenTabletArtist (Avalonia 12, .NET 10) with a bundled, patched copy of
  OpenTabletDriver 0.6.8-rc1 (LGPL-3.0) in `external/OpenTabletDriver`.
- Repo: https://github.com/DigitalGroceries/tablet-app-for-mac (owner account: DigitalGroceries).
- Website (branch `gh-pages`): https://digitalgroceries.github.io/tablet-app-for-mac/ (Home, Releases, Help,
  Tablets, Source code, Credits, Support). Machine summary: `/llms.txt`. Wiki: GitHub wiki `Home.md`.
- Only real test hardware: Wacom Intuos4 Small **PTK-440** (touch ring + 8 ExpressKeys) on macOS 27 (Apple
  silicon). Apps the owner tests in: Photoshop, Affinity (the new single Canva "Affinity" app), PhotoCraft.

## Branches and releases (27 Oct 2026)

| Thing | Where |
|---|---|
| Stable, "Latest" | **Version 15** (tag `v15`, branch `v15-source` = `main` at 0b28a32 + later main commits) |
| Newest BETA | **Version 16 BETA 2** (tag `v16-beta2` -> 255d808 on branch **`v16-source`**), published 27 Oct 21:44 UTC, pre-release |
| Previous BETA | Version 16 BETA 1 (tag `v16-beta1`, 1b522e7) |
| Development | branch `v16-source` (all V16 work). `main` = last stable + small fixes |
| Leftover | branch `tmp-v16-beta2-upload` (release-upload copy; safe to delete) |

Release assets: `Tablet-App-for-Mac.zip` (BETA) or `.dmg` + `.zip` (stable), the manual PDF
(`tablet-app-for-mac-manual-version-<n>.pdf`) and `SHA256SUMS`. The app's own updater reads GitHub Releases:
it offers the newest stable, newer BETAs, "Go back to Version 15" and "Earlier versions…".

## Build, test, package (Linux works; see AGENTS.md for details)

- .NET SDK 10.0.402 (`global.json`), `export PATH=$HOME/.dotnet:$PATH`.
- Tests: `dotnet test tests/OpenTabletArtist.Tests` (baseline: **16 known failures**, all environment ones:
  WindowsInk x6, BundleVerification x3, SettingsFile x3, PresetStore, PluginInventory, UpdaterTests,
  BuildR10Published) and `dotnet test tests/OpenTabletArtist.UiTests` (baseline **2 known failures**:
  BuildR2AboutTests.HomeSentence…, BuildR6HomeAlignTests.HomeLinks…). Driver tests:
  `dotnet test external/OpenTabletDriver/OpenTabletDriver.Tests` (0 failures). Any other failure is new.
- Package + ad-hoc sign on Linux: `BETA=<n> scripts/pack-macos-linux.sh <version> <out-dir> arm64`
  (needs rcodesign 0.29.0: `cargo install apple-codesign`). Omit BETA for a stable build. Stable releases also
  get a .dmg (Grok's `grok-scripts/` has the dmg recipe).
- Manual PDF: `python3 scripts/make-manual-pdf.py "<version label>" <YYYY-MM-DD> <out.pdf>` (Playwright).
  Manual source: `OpenTabletArtist/Assets/manual/manual.md` (shown in-app by the ? button), pictures in
  `OpenTabletArtist/Assets/manual/img/`.
- Tablets page generator: `scripts/make-tablets-page.py` (data from OTD configs + `TABLETS.md`).
- Translations: `OpenTabletArtist/Assets/i18n/*.json` (8 languages: en, es, de, fr, bg, ru, ja, zh).

## What Version 16 changed (on v16-source)

- BETA 1: pen scroll (PR #5 by ftrabbold, a donor); Wacom support from Linux `wacom_wac.c` / X.org layouts
  (PTH-460/660/860 ring mirrored; Intuos3, Cintiq 21UX/12WX/22HD touch strips as wheels with attribute
  `WheelsAreTouchStrips`; Cintiq 13HD centre button = ExpressKey 9; Bamboo Fun ring; CTK-4120); Supported
  tablets list without Windows notes, status Questionable; translations; Pen tabs fix (e8dbf5e).
- BETA 2 (the owner's ring test): ring **direction** fixed (Clockwise = OTD `ClockwiseRotation`; old bindings have no
  `DirectionVersion` and are read the old way and rewritten, also per-app tables); ring-mode edits go to the
  **selected app's table** (they snapped back before); smooth **zoom** stores a Speed and sends one wheel line per
  `DegreesOfSpeed(speed)`; smooth actions write their Speed as the step so fallback keys follow it;
  **Affinity**: Option+scroll zoom, ⌘+scroll rotate (needs Affinity Settings › Tools "Enable canvas rotation with
  ⌘+scroll wheel"); numbers read in the invariant culture (comma locales broke them).
  Key files: `OpenTabletArtist/Domain/RingModes.cs`, `ViewModels/RingModesEditor.cs`,
  `ViewModels/TabletDetailViewModel.cs` (+ `.Applications.cs`), `plugins/OpenTabletArtist.Dynamics/*`,
  tests `tests/OpenTabletArtist.Tests/RingFixV16Beta2Tests.cs`.

## Open items (next work)

1. Wait for the owner's test of **BETA 2** (ring in Photoshop and Affinity; after updating he clicks Save once).
   When he confirms, release **Version 16 stable** (BETA first, stable only after his test).
2. Known risks from the BETA 2 code review, not fixed yet:
   - Going back to Version 15, editing the ring there, then updating again can reverse those rings.
   - Relative wheels (Huion/XP-Pen dials) may now run backwards: the CW/CCW inversion was removed for every
     wheel, only the PTK-440 absolute ring was tested (see `WheelEditor.OnRelativeDelta` comment).
   - Wheel activation thresholds still write to All when an app tile is selected, and skip the All stash.
   - Affinity rotate direction (⌘+scroll up = clockwise?) is untested.
3. the owner proofreads the Bulgarian texts (V14/V15 update texts + touch strip texts).
4. User feedback on the improved tablets -> move confirmed ones to Supported in
   `external/OpenTabletDriver/TABLETS.md` and regenerate the Tablets page.
5. PhotoCraft draws fast curves as straight segments: PhotoCraft's own bug (storytold/photocraft #2138, their
   PR #9587), nothing to fix here.

## the owner's standing rules for this project (follow them)

- **Plain, short language.** One physical step per message when he has to do something, then wait for his
  Y/N. He has memory issues: keep track of things for him, don't expect him to remember technical steps.
- **BETA first, stable after his test.** Each release keeps the 2 previous stable versions downloadable.
- **Apple silicon only.** No Intel Mac or Windows mentions anywhere (app, site, manual, tablet list).
- **His name is never public** (commits/site use the DigitalGroceries account).
- **Every new version:** update the manual (in-app + PDF attached to the release) and **retake every screenshot
  that no longer shows the newest GUI** (website Home + Help, in-app manual, PDF, wiki).
- **Check pull requests twice a day** (start of a session and ~1 h before its end). Outside contributors' code
  runs only after the owner approves it.
- Translations: if unsure of a word and no AI can help, keep it in English. bg/ru/ja keep the word "service"
  for the driver; the other languages say "driver" (never "daemon").
- Only spelling fixes to text he wrote himself.
- Don't guess: search documentation/manuals first; verify facts in 3 sources.
- Permanent deletions are his to do unless he says otherwise; publishing needs his OK (he gave a standing OK
  to publish on 27 Oct when asked).

## Other helpers

- **Grok Bot** (another AI on the owner's Mac) has done builds before; it is away until about 29 Oct. Its notes live
  in `its own folder on the owner's Mac` on the owner's Mac.
- Claude's own detailed notes are in the owner's "Claude Memory Bank" on his T7 drive (not online).
