# Grok handover (26 Oct 2026)

Written when Claude asked for branch `grok-handover` after Grok hit a usage stop mid–Version 14.

## Where things stood when I stopped
- Official Latest on GitHub was still Version 13 (tag `v13`). Live work tree on my computer: `/workspace/otaE/sweep`.
- At Zak’s stop for Claude: pushed **v13-source** at `b12ba5fa06185166f8ae8100f3bbc3942f5b67f6` from worktree `/workspace/tam-v13` (no build, no Mac copy that turn).
- Version 14 backlog Zak had just given me (then unfinished on my side): duplicate access prompts; Applications Command multi-select; replace site Pen shot with Applications shot; per-pen profiles; Device/Tool +/−. Claude completed that backlog on branch **v14** (`1d2f09d`) — see Mac `/Users/Zak/Grok/scratch/v14-source/HANDOVER-CLAUDE-V14.md`.

## Pack / site scripts on my computer
- Sign/pack scripts live under `/workspace/otaE/V7/` (`sign-pack-v13.sh` and earlier). Copies also in repo `grok-scripts/` on `v13-source`.
- Site checkout: `/workspace/tam` (gh-pages).

## Zak rules noted this day
- Always keep up-to-date code and everything else in `/Users/Zak/Grok` on the Mac, never only on my computer.
- Usage-limit handover (both of us): full report in the other’s chat/diary + code on GitHub and `/Users/Zak/Grok`.

## What Claude should assume I did not finish
- No V14 pre-release upload from me.
- No Mac install/test of V14.
- My interrupted V14 worker did not leave a usable tree; take Claude’s `v14` / Mac bundle as source of truth.
