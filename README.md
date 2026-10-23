# Tablet App for Mac

> **UNDER CONSTRUCTION**

## Your Mac tablet, set up the Wacom way

A free alternative to the Wacom driver for Mac, built on OpenTabletDriver. Laid out like the Wacom settings, so there is nothing new to learn. Tested with a Wacom Intuos4 on macOS 27.

Apple silicon, macOS 27 · [What is new](https://digitalgroceries.github.io/tablet-app-for-mac/releases/)

## [⬇ Download for Mac](https://github.com/DigitalGroceries/tablet-app-for-mac/releases/latest/download/Tablet-App-for-Mac.dmg)

> [!WARNING]
> **ATTENTION: TO INSTALL IT, FOLLOW THESE STEPS**

If the Wacom driver is installed, remove it first with Wacom's own uninstaller (Applications › Wacom Tablet › Wacom Tablet Utility), then restart your Mac.

1. Open the downloaded file, **Tablet-App-for-Mac.dmg**, and drag **Tablet App for Mac** onto the **Applications** folder.
2. Open the app from Applications. If **Not Opened** appears, click **Done**, not Move to Bin.

   <img src="https://digitalgroceries.github.io/tablet-app-for-mac/assets/shots/install-01-not-opened.png" alt="The message: Tablet App for Mac.app Not Opened, with the Done and Move to Bin buttons" width="420">
3. Open **System Settings › Privacy & Security**, scroll down to **Security** and click **Open Anyway**.

   <img src="https://digitalgroceries.github.io/tablet-app-for-mac/assets/shots/install-02-open-anyway.png" alt="System Settings, Privacy and Security, Security section, with the Open Anyway button" width="420">
4. macOS asks once more. Click **Open Anyway**, not the blue Move to Bin button.

   <img src="https://digitalgroceries.github.io/tablet-app-for-mac/assets/shots/install-03-open-anyway-confirm.png" alt="The message: Open Tablet App for Mac.app?, with Move to Bin, Open Anyway and Done" width="420">
5. Confirm with Touch ID, or click **Use Password…** and enter your Mac login password.

   <img src="https://digitalgroceries.github.io/tablet-app-for-mac/assets/shots/install-04-touch-id.png" alt="The Touch ID prompt, with Use Password and Cancel" width="420">
6. In **System Settings › Privacy & Security › Input Monitoring**, switch on **Tablet App for Mac**. This lets the app read your pen.

   <img src="https://digitalgroceries.github.io/tablet-app-for-mac/assets/shots/setup-read-pen.png" alt="Input Monitoring, with the switch for Tablet App for Mac turned on" width="420">
7. In **System Settings › Privacy & Security › Device Control and Data Access** (called **Accessibility** on older macOS), switch on **Tablet App for Mac**. This lets the pen move the pointer.

   <img src="https://digitalgroceries.github.io/tablet-app-for-mac/assets/shots/setup-move-pointer.png" alt="Device Control and Data Access, with the switch for Tablet App for Mac turned on" width="420">
8. Quit the app and open it again.

**Tip:** if the pen still doesn't move the pointer, select Tablet App for Mac in those two lists, remove it with **−**, add it again with **+**, then reopen the app.

Help and the user manual: https://digitalgroceries.github.io/tablet-app-for-mac/

Tested only with macOS 27.0.1 and a Wacom Intuos4 Small (PTK-440).

---

## For developers

A modified version based on [OpenTabletArtist](https://github.com/thesevenpens/OpenTabletArtist) (GPL-3.0) and [OpenTabletDriver](https://github.com/OpenTabletDriver/OpenTabletDriver) (LGPL-3.0). The code keeps the original internal names (`OpenTabletArtist.*`) so existing OpenTabletDriver settings keep working.

### Use it, fork it, improve it

Anyone may use, copy, change and share this code, and publish an improved version on their own page, under the GNU General Public License v3.0 (see [LICENSE](LICENSE) and [NOTICE](NOTICE)). If you share a changed version, share its source under the same licence and say that you changed it. The bundled OpenTabletDriver in `external/OpenTabletDriver` stays under its own LGPL-3.0 licence (`external/OpenTabletDriver/LICENSE`); the patches applied to it are in [patches](patches).

### Build

Needs the .NET 10 SDK. Build on an Apple silicon Mac (or cross-publish from Linux):

```
git clone https://github.com/DigitalGroceries/tablet-app-for-mac.git
cd tablet-app-for-mac
dotnet publish OpenTabletArtist/OpenTabletArtist.csproj -c Release -r osx-arm64 --self-contained true
```

On a Mac this also assembles `dist/OpenTabletArtist.app` (see `scripts/build-macos-app.sh`). The app is unsigned; sign it with your own certificate if you want to keep macOS permissions across builds.

Tests: `dotnet test tests/OpenTabletArtist.Tests` and `dotnet test tests/OpenTabletArtist.UiTests`.

> Working on this code? Read [AGENTS.md](AGENTS.md) first.
