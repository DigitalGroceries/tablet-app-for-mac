# GUI localisation (Version 8)

British English (`en.json`) is the source of truth. Other languages are JSON string tables under
`OpenTabletArtist/Assets/i18n/`. Missing keys fall back to English.

## Languages

| Code | Name | Notes |
| --- | --- | --- |
| `system` | Match system | Follows macOS UI language when it matches a row below, else English |
| `en` | English | Source of truth |
| `es` | Español | |
| `de` | Deutsch | |
| `fr` | Français | |
| `bg` | Български | **Zak proofreads** — see `bulgarian-proof-2026-10-24.md` |
| `ru` | Русский | |
| `ja` | 日本語 | |
| `zh-CN` | 中文 | |

## How it works

- `Services/Localizer.cs` loads tables from Avalonia assets, persists `ui.language` in `settings.json`.
- Settings › System has a **Language** picker. Changing it applies immediately (`LanguageChanged`).
- Call `Localizer.T("key")` or `Localizer.Tf("key", ("version", n))` from code. Placeholders use `{name}`.
- Do **not** put “Zak” in public strings. Do **not** translate the PDF manual in this pass.
- Leave technical identifiers, paths, URLs, tablet model names, and log lines in English where clearer.
- Keep macOS pane names recognisable: Input Monitoring, Accessibility, Device Control (unless a locale’s System Settings uses a clear local name you prefer).

## Adding a language

1. Copy `Assets/i18n/en.json` to `Assets/i18n/<code>.json` and translate values (keep keys and `{placeholders}`).
2. Add the code to `Localizer.Languages` and `Supported`.
3. Mirror the file under `/workspace/otaE/V7/i18n/` for release notes / proof packs.
4. For Bulgarian, update the proof markdown and wait for Zak’s corrections before polishing further.

## Proof pack (Bulgarian)

- `/workspace/otaE/V7/bulgarian-proof-2026-10-24.md`
- `/workspace/otaE/V7/i18n/bg.json`

## Wording: “daemon”

English keeps the computing word **daemon**. When translating, do **not** use a word that reads as “demon”:

| Language | Use |
| --- | --- |
| Japanese (`ja`) | **サービス** (service) — not デーモン |
| Bulgarian (`bg`) | **услуга** (service) — not демон |
| Russian (`ru`) | **служба** (service) — not демон |
| German / Spanish / French | English loanword **daemon** / **Daemon** is fine |
| Chinese (`zh-CN`) | **守护进程** is fine |
