# Installing Tablet App for Mac 0.91.3 (R4) on a Mac

> **Please read this first.**
> This app was tested only on my own version of macOS (macOS Golden Gate 27.0.1 (build 26A434)) and with my own tablet model (Wacom Intuos4 PTK-440, the Intuos4 Small, active area 157.5 x 98.4 mm). If you install or use it, you do so entirely at your own risk. I take no responsibility for any unwanted consequences you may experience.


## Before you install: check it yourself

You don't have to trust me. Before installing, you can ask an AI assistant of your choice (or a person you trust) to check the app and its source code.

1. Get the source code. Use the link in `SOURCE.md`, or the source archive `TabletAppForMac-0.91.3-R4-src.tar.gz` published next to this download.
2. Give the AI the source code (and the app, if it can read it), together with this prompt:

   ```
   Please review this macOS app and its source code before I install it.
   1. Look through the source for anything harmful or suspicious: hidden data
      collection, uploading files, running downloaded code, changing system
      settings, or anything unrelated to a drawing tablet.
   2. Explain which macOS permissions it needs and why. It asks for Input Monitoring
      and Accessibility: check that these are only used to read the tablet and to
      send keys, mouse clicks and scrolling.
   3. List every network connection it can make, where to, and when.
   4. Check whether the app in the download matches this source code, as far as
      you can (version numbers, bundle contents, signatures).
   5. Tell me plainly whether you think it is safe to install, and what risks remain.
   ```

3. Check that your download is complete and hasn't been changed:
   1. Open **Terminal** and go to the folder that contains the download (for example, type `cd "$HOME/Downloads"` and press **Return**).
   2. Type this and press **Return**:

      ```
      shasum -a 256 "TabletAppForMac-0.91.3-R4-macOS-arm64-adhoc.zip"
      ```

   3. Compare the long code it prints with the **sha256 value published on the download page**. They must match exactly. If they don't, don't install.
   4. Important: always compare with the value on the download page, **never** with a value found inside the zip. Anyone who changed the zip could also change a value stored inside it.

- The app is free. It is a modified version based on OpenTabletArtist and OpenTabletDriver. Their licences are in the `LICENSES` folder, and the source code links are in `SOURCE.md`.
- The app is **not** from the App Store and is **not** notarised by Apple. It has an "ad-hoc" signature, which means it isn't signed by an Apple developer account. Because of that, macOS will block it the first time you open it. The steps below show you how to allow it.
- In Terminal commands, keep the quotes around app and file names exactly as shown: the app name has spaces.
- Steps marked **(unverified)** have not been tested on a clean Mac.
- You need a Mac with Apple Silicon (M1 or newer). This build will not run on Intel Macs.

## 1. Before you start: Wacom's own driver

Two drivers reading the same tablet fight each other. If Wacom's driver is still installed, do **one** of these:

- **Keep it, but switched off:** in **System Settings › Privacy & Security**, turn **off** every Wacom item in **Accessibility** and in **Input Monitoring** (and Wacom items under **General › Login Items & Extensions**). **(unverified)**
- **Or remove it** (Wacom's official steps: https://support.wacom.com/hc/en-us/articles/1500006264541):
  1. Quit any Wacom apps (Wacom Center, Wacom Tablet Properties) and unplug the tablet.
  2. Open **Finder › Applications › Wacom Tablet** and run **Wacom Tablet Utility**. Under "Tablet Software", click **Uninstall**. **(unverified)**
  3. Restart the Mac, then remove any Wacom items left in **Accessibility** and **Input Monitoring**.

## 1b. Upgrading from OpenTabletArtist (the app's old name)

The app used to be called **OpenTabletArtist**. Your ring modes, display mapping and ring pop-up choice are copied across automatically the first time you open Tablet App for Mac (the old settings folder is left untouched). If you had chosen to start automatically, the new app registers itself as a login item. The old entries can't be removed by the new app, so do this once by hand:

1. Quit OpenTabletArtist if it is running, and drag **OpenTabletArtist** from **Applications** to the **Bin**.
2. **System Settings › General › Login Items & Extensions**: under "Open at Login", select **OpenTabletArtist** and click **−**.
3. **System Settings › Privacy & Security › Input Monitoring**: select **OpenTabletArtist** and click **−**. Do the same in **Accessibility**.
4. Then add **Tablet App for Mac** in Input Monitoring and Accessibility as in step 4.

## 1c. Upgrading from Free Tablet App for Mac (build R)

Build R was called **Free Tablet App for Mac**. Tablet App for Mac keeps the same app ID, so macOS should keep its Input Monitoring, Accessibility and start-at-login entries **(unverified)**. All its settings are copied across unchanged the first time you open Tablet App for Mac; the old folder is left untouched.

1. Quit Free Tablet App for Mac if it is running.
2. Drag **Free Tablet App for Mac** from **Applications** to the **Bin**, so there is only one copy with this app ID.

## 2. Copy the app to Applications

1. Double-click the downloaded `.zip` file to unpack it.
2. Open the unpacked folder.
3. Drag **Tablet App for Mac.app** onto the **Applications** shortcut in the same folder. If the shortcut isn't there, drag it into **Applications** in a Finder window instead.

## 3. Open it the first time (macOS will block it)

1. Open **Applications** and double-click **Tablet App for Mac**.
2. macOS says it "cannot be opened" or "cannot verify the developer". Click **Done** or **OK**. Do **not** click **Move to Bin**.
3. Open the **Apple menu**, then **System Settings**, then **Privacy & Security**.
4. Scroll down to **Security**. You will see a line saying Tablet App for Mac was blocked. Click **Open Anyway**. **(unverified on a clean Mac)**
5. Type your Mac password if asked, then click **Open Anyway** again.

**Other way, using Terminal (for confident users only):**

1. Open **Applications**, then **Utilities**, then **Terminal**.
2. Paste this line and press **Return**:

   ```
   xattr -dr com.apple.quarantine "/Applications/Tablet App for Mac.app"
   ```

3. Open the app again. **(unverified on a clean Mac)**

## 4. Give it permission to read the tablet and move the pointer

The app needs two permissions, **Input Monitoring** and **Accessibility**. Without them, the pen and buttons will not work.

1. Open **System Settings**, then **Privacy & Security**, then **Input Monitoring**.
2. Click **+**, choose **Applications**, then **Tablet App for Mac**, and click **Open**. Make sure its switch is **on**. **(unverified)**
3. Go back to **Privacy & Security** and open **Accessibility**.
4. Click **+** again, add **Tablet App for Mac**, and turn its switch **on**. **(unverified)**
5. If the app asks you to, add the helper **OpenTabletDriver** in the same way. It is inside the app at `Tablet App for Mac.app/Contents/MacOS/Daemon/`. **(unverified)**
6. Quit the app and open it again.

## 5. Choose whether it starts when you log in

Your tablet only works while Tablet App for Mac is running. The first time you open it (and once after updating from an older version), it asks:

> "Start Tablet App for Mac automatically when you log in?"

- **Start automatically** (recommended): it opens by itself each time you log in, with only its icon in the menu bar, no window. **(unverified on a real Mac)**
- **Manually**: open Tablet App for Mac yourself each time before using your tablet.

To change it later: open Tablet App for Mac, go to **Advanced › Startup** and switch **Start automatically when I log in** on or off. You can also open **System Settings › General › Login Items** and add or remove **Tablet App for Mac** there; the app's switch follows what you set. If macOS shows the item as waiting for approval, turn it on in that list. If the app says it could not add itself, add it there by hand: click **+** under "Open at Login" and choose **Tablet App for Mac**. **(unverified)**

Quitting Tablet App for Mac from the menu bar stops your tablet until you open the app again; the app warns you before it quits.

## 6. After every update: remove and re-add the permissions

**Every new version needs this step.** The app has an ad-hoc signature, which changes every time the app is rebuilt, so macOS treats each new version as a different app and the old permission entries stop working.

1. Quit Tablet App for Mac.
2. Replace the old app in **Applications** with the new one.
3. In **Privacy & Security > Input Monitoring**, select the old **Tablet App for Mac** entry and click **−** to remove it. Do the same in **Accessibility** (and for **OpenTabletDriver**, if it is listed).
4. Add the new app again, as in step 4.
5. Allow the first open again if macOS blocks it, as in step 3.

## 7. If a key or mouse button gets stuck

Sometimes a key (for example Option, Shift or Control) or a mouse button can stay "held down". This can happen when the ring or a button is used while the app quits or loses permission.

1. Press and release the stuck key on your keyboard, or click the mouse once.
2. If that doesn't help, quit Tablet App for Mac. Click its menu-bar icon, or press **Cmd+Q** while the app is in front.
3. Still stuck? Open **Activity Monitor** (in **Applications > Utilities**). Find **Tablet App for Mac** and **OpenTabletDriver.Daemon**, select each one, and click the **✕** button, then **Quit**.
4. As a last resort, log out and log back in, or restart the Mac.

## 8. Uninstall

1. Quit Tablet App for Mac (see step 7.2).
2. Drag **Tablet App for Mac** from **Applications** to the **Bin**, then empty the Bin.
3. In **System Settings > Privacy & Security**, remove **Tablet App for Mac** (and **OpenTabletDriver**, if it is listed) from **Input Monitoring** and **Accessibility**. Select each entry and click **−**.
4. Optional: to delete your settings too, in Finder choose **Go > Go to Folder…**, then type `~/Library/Application Support/`. Delete the **Tablet App for Mac** and **OpenTabletDriver** folders, if they exist. **(unverified folder names)**
5. If you removed Wacom's driver and want it back, download it again from Wacom's website.
