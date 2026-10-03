# IdleViz

[![CI](https://github.com/Xauno/IdleViz/actions/workflows/ci.yml/badge.svg)](https://github.com/Xauno/IdleViz/actions/workflows/ci.yml)
![Platform: macOS 26+](https://img.shields.io/badge/platform-macOS%2026%2B-lightgrey)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
![Status: in development](https://img.shields.io/badge/status-in%20development-orange)

A macOS menu-bar app that turns your Mac into a music display. When the Mac goes idle, or when you press a hotkey, it opens a fullscreen [Butterchurn](https://github.com/jberg/butterchurn) visualizer that reacts to Spotify's audio, with a now-playing overlay styled after the Spotify TV app. Any mouse, key or trackpad input closes it.

It is not a real screensaver, just a fullscreen window on top of everything.

> **Status: in development.** The menu-bar app opens a fullscreen window after a few idle minutes, from a hotkey or from a URL, but only while Spotify has a track loaded, and closes it on any input or after a keep-awake limit. It shows a Butterchurn visualizer that reacts to Spotify's audio and shuffles through 395 bundled presets, with what Spotify is playing in an overlay styled after the Spotify TV app. Every step in the [Roadmap](#roadmap) is built; it runs on the main display only.

## Contents

- [Features](#features)
- [Roadmap](#roadmap)
- [Requirements](#requirements)
- [Installation](#installation)
- [Usage](#usage)
- [Custom visualizers](#custom-visualizers)
- [How it works](#how-it-works)
- [Windows](#windows)
- [Development](#development)
- [Project layout](#project-layout)
- [Contributing](#contributing)
- [License](#license)
- [Acknowledgments](#acknowledgments)

## Features

Working now:

- Menu-bar app with a small glass popup: **Settings…** and the current open hotkey.
- Opens a fullscreen black window on the main display from a global hotkey (⌃⌥V by default), with `open idleviz://open`, or from **Open now** in settings.
- Only opens while Spotify is running and has a track loaded (playing or paused). Otherwise the menu-bar icon flashes and nothing opens.
- Opens by itself after 5 idle minutes (or 10, 15, 30, or never, set in settings). It doesn't open on idle while the screen is locked or while another app keeps the display awake, such as a video or a call. After a blocked attempt it waits until you've used the Mac again.
- Closes on any input (mouse movement, click, scroll, key, modifier key, trackpad gesture), then returns focus to the app you were using. The cursor is hidden while it's open.
- The brightness, keyboard backlight, playback and volume keys on the top row don't close it, so you can turn the music up or pause it while it's showing.
- Two more keys don't close it: **L** likes the visualizer on screen (adds it to your favorites, or takes it off again) and shows a heart next to the track title, and **N** skips to the next one. Both keys can be changed or turned off in settings.
- Fades in when it opens, and fades out quickly on input, like the macOS screensaver.
- Keeps the screen awake while it's showing, up to a limit you set (1 hour by default; 30 minutes, 2 hours or 4 hours also available). At the limit it fades out slowly, and the Mac sleeps and locks as usual. It doesn't open again until you've used the Mac.
- Optional separate idle and keep-awake times for when the Mac is on battery. They apply the moment you unplug or plug in, even while the visualizer is open. Macs without a battery don't show the option.
- Settings window with a hotkey recorder and an **Open now** button.
- A welcome window asks for the two permissions the app needs (Spotify control and Spotify audio) while you're at the Mac, so macOS never shows a prompt during an idle open. It appears at launch whenever macOS hasn't been asked yet.
- The menu-bar icon turns yellow while a permission is missing, and the popup lists what's missing. Clicking a row opens the welcome window or the right System Settings page.
- Now-playing overlay styled after the Spotify TV app: album art, title, artist, progress bar and times, in the Figtree font. When paused, only the progress bar stays. Music ads show an "Advertisement" label with the progress bar, and podcasts show no overlay. If Spotify quits or the track goes away, the overlay fades out and the window stays open.
- [Butterchurn](https://github.com/jberg/butterchurn) (WebGL Milkdrop) visualizer with 395 bundled presets. By default it shows a random preset every 30 seconds with a 2.7-second blend, and goes through all of them before repeating one.
- Preset controls in settings: shuffle or one fixed preset, which presets to shuffle from, seconds per preset, blend time, favorites, and a blocklist for presets you don't like. Changes apply at once, even while the visualizer is open. It renders at 60 fps, at most 2560 pixels wide, and only while the window is open.
- Reacts to Spotify's audio only, through a Core Audio process tap that runs while the window is open. The visuals never hear the microphone or other system audio. A slow automatic gain keeps the visuals lively when Spotify's volume is low, and silence stays silent: paused, quit, or playing on another device, the visuals just drift.
- Bring your own presets: drop Butterchurn `.json` or original Milkdrop `.milk` files, or custom `.js` visual plugins, into the presets folder, or use **Import…** in settings. Subfolders work, so a downloaded pack can go in as it is. The folder is watched, so new files show up without a restart. `.milk` files are converted once and cached.
- Custom plugins run in a sandboxed frame with no network, no storage and no access to the rest of the app. A plugin that throws, runs too slowly or hangs is dropped and listed under **Failed to load** in settings, next to presets that couldn't be read or converted.
- Audio delay for Bluetooth and AirPlay speakers, which play the sound later than the Mac sends it: the visuals and the progress bar wait to match. Set it with a slider (0 to 2.5 s), or press **Detect** and the app measures it with the microphone for about 5 seconds. It is saved for each speaker or pair of headphones, and a device used for the first time starts at the delay macOS reports for it.
- A manual delay test for when the microphone can't help, such as with headphones: the app beeps once a second and lights up a panel, and you drag the delay until the light and the beep land together. Spotify is paused while it runs.
- If the page ever stops responding, the app replaces it within a few seconds. The window also closes when the Mac goes to sleep or the displays change (one is plugged in or removed, or its resolution or position changes). A notification doesn't close it, and neither does the Dock or the menu bar changing size.
- Reads what Spotify is playing (title, artist, album, playing or paused, position, length) and whether it's a song, a podcast or an ad, and downloads the album art. It never launches Spotify: it only asks while Spotify is running, and updates when Spotify says something changed rather than polling.

- Adjustable visualizer brightness (50 to 100%, 70% by default), a switch to hide the now-playing overlay and show only the visualizer, and launch at login.
- Never launches Spotify and never uses Spotify's web API or a login.

Not built on the Mac: more than one display. The visualizer opens on the main display only, and other displays keep showing the desktop. The Windows app can cover several; see [Windows](#windows) and [Planned](#planned).

## Roadmap

Each step from [project.md](project.md) becomes one pull request, and this table is updated as it merges.

| Step | What                                                                              | Status  |
| ---- | --------------------------------------------------------------------------------- | ------- |
| 0    | Repo tooling: CI, tests, PR workflow, plugin guide, Aurora example plugin         | Done    |
| 1    | Open/close shell: Xcode project, menu-bar app, hotkey, fullscreen window, dismiss | Done    |
| 2    | Audio spike: prove Spotify audio can drive Butterchurn (findings only)            | Done    |
| 3    | Spotify now-playing: launch/quit tracking, track info, artwork, content type, ads | Done    |
| 4    | Open rules and menu-bar icon flash                                                | Done    |
| 5    | Overlay page matched to the Spotify TV app, all states                            | Done    |
| 6    | Idle trigger, with skip rules (locked screen, video or call playing)              | Done    |
| 7a   | Visualizer: Butterchurn with bundled presets                                      | Done    |
| 7b   | Visualizer: real Spotify audio through the process tap                            | Done    |
| 7c   | Visualizer: preset controls                                                       | Done    |
| 7d   | Visualizer: custom preset folder and sandboxed plugins                            | Done    |
| 7e   | Visualizer: audio delay, with a microphone-based detector                         | Done    |
| 8a   | Polish: keep-awake limit, battery times, fades                                    | Done    |
| 8b   | Polish: permission setup (welcome window, yellow icon, error rows)                | Done    |
| 8c   | Polish: brightness, overlay switch, launch at login                               | Done    |

### Planned

- **Multi-monitor support on the Mac.** The Windows app has it (step W9 under [Windows](#windows)): a main display, further displays that either each run the same visualizer or share one extended picture, a choice of display for the now-playing overlay, and a switch for whether input closes it. The Mac app still uses the main display only. No step in `project.md` covers the Mac side yet; it's listed there under "Later".

## Requirements

- macOS 26 or later
- The Spotify desktop app
- To build: full Xcode 26 or later (not just the Command Line Tools) and a free Apple Development certificate (Xcode's personal team)

## Installation

The app is built from source for personal use. There is no download.

1. Clone the repo and create your local signing config:

   ```bash
   cp Config/Local.example.xcconfig Config/Local.xcconfig
   ```

   Set `DEVELOPMENT_TEAM` in `Config/Local.xcconfig` to your team ID. It's the `OU=` value printed by:

   ```bash
   security find-certificate -c "Apple Development" -p | openssl x509 -noout -subject
   ```

   Always sign with the same certificate. macOS ties the app's permissions to its signature, so a changing signature makes permission prompts come back.

2. Double-click `IdleViz.command` in Finder (or run `./IdleViz.command`). It builds the Release app, replaces `/Applications/IdleViz.app` with it, quitting a running copy first, and opens it. Run it again after pulling changes to update. Then skip to step 4.

   To do it by hand instead, open `IdleViz.xcodeproj` in Xcode and run the **IdleViz** scheme, or build from Terminal:

   ```bash
   xcodebuild build -project IdleViz.xcodeproj -scheme IdleViz -configuration Release -derivedDataPath .build/xcode
   ```

3. Copy `.build/xcode/Build/Products/Release/IdleViz.app` to `/Applications` and open it. The app has no Dock icon; it lives in the menu bar.

4. A welcome window opens. With Spotify running, click **Continue**. If Spotify isn't running, open it and the window carries on by itself. macOS then asks two things in turn:
   - Whether IdleViz may control Spotify. Click **OK**; the app uses this only to read what's playing.
   - Whether IdleViz may record system audio. Click **Allow**; the app listens to Spotify's audio only, and only while the visualizer is open.

5. If you said no to either, the menu-bar icon turns yellow and its popup shows what's missing. macOS only asks once, so turn it on in System Settings → Privacy & Security: under **Automation** for Spotify control, or under **Screen & System Audio Recording** → **System Audio Recording Only** for the audio. Without Spotify control the visualizer doesn't open. Without the audio it opens but doesn't react to the music.

6. The first time you press **Detect** next to **Detect delay** in settings, macOS asks for the microphone. This one is optional. The app listens only while it says **Listening…**, about 5 seconds, and keeps nothing.

## Usage

- **Menu bar:** click the waveform icon for a small popup with **Settings…** (⌘,) and the current open hotkey. A yellow icon means a permission is missing: the popup then has a row for each, and clicking one opens the welcome window (if macOS hasn't asked yet) or the System Settings page where it's switched on.
- **Open:** press the hotkey (⌃⌥V by default), run `open idleviz://open` in Terminal, or click **Open now** in settings. Spotify has to be running with a track loaded, playing or paused. If it isn't, the menu-bar icon flashes a few times instead.
- **Close:** move the mouse, click, scroll, press any key (other than the like and skip keys and the media keys below) or use a trackpad gesture. Input in the first 0.4 s after opening is ignored, so the hotkey itself doesn't close it. Keys you're still holding after that are ignored until you let go; pressing one again closes it.
- **Media keys:** the brightness, keyboard backlight, play/pause, next, previous, mute and volume keys do their usual job and leave the visualizer open. The other top-row keys (Mission Control, Spotlight, Dictation, Focus) still close it, and so do F1–F12 pressed as standard function keys and the fn key itself. Like the like and skip keys, this only holds while the visualizer has keyboard focus.
- **Like and skip:** while the visualizer is open, **L** adds the visualizer on screen to your favorites and a filled heart appears next to the track title for a moment. Press it again to take it off; the heart is then an outline. With the overlay off, paused or hidden, the heart shows in the top-right corner instead. **N** blends to the next visualizer in about half a second and restarts the seconds-per-preset count; it does nothing in **Single** mode. In settings, **Like key** and **Skip key** let you pick another key (A–Z, 0–9, the arrow keys or Space) or **Off**. The keys are matched by their position on a US keyboard, they only work while the visualizer has keyboard focus (if macOS refused to activate the app, they close it like any other key), and holding a modifier still closes it.
- **Idle:** after the **Start after idle** time with no input (5 minutes by default), it opens by itself, with the same Spotify check. It skips that while the screen is locked or another app keeps the display awake (a video, a call, a presentation), and then waits until you use the Mac again before trying again.
- **Keep awake:** while the visualizer is showing, the display doesn't sleep and the Mac doesn't lock by itself. After the **Keep screen awake** time (1 hour by default), counted from when it opened, it fades out and the Mac's own sleep, screensaver and lock settings take over. It then stays closed until you use the Mac again.
- **On battery:** turn on **Different times on battery** in settings to get a second **start after idle** and **keep screen awake** time that apply while the Mac is unplugged. They start as copies of the normal times.
- **Brightness and overlay:** in settings under **Visualizer**, **Brightness** dims the visuals behind the overlay (50 to 100%), and **Show Spotify overlay** turns the now-playing layout off, leaving only the visualizer. Both apply at once, even while the visualizer is open.
- **Launch at login:** the switch in settings adds IdleViz to your login items. It works best from the copy in `/Applications`. macOS may ask you to allow it under System Settings → General → Login Items.
- **Settings:** change the idle and keep-awake times, or turn idle opening off, and change the hotkey and the like and skip keys. Close the window with its red button; ⌘Q quits the app while settings is focused.

- **Now playing:** the overlay updates whenever Spotify's track or state changes, and re-syncs the progress bar every 5 s while the window is open. The app also logs each change:

  ```bash
  log stream --predicate 'subsystem == "com.xauno.IdleViz" AND category == "spotify"'
  ```

- **Visualizer:** it moves to whatever Spotify is playing on this Mac, and a new preset blends in every 30 seconds. With Spotify paused, or playing on another device, the visuals get silence and drift slowly.
- **Presets:** in settings, **Mode** is **Shuffle** or **Single**. Shuffle has **Shuffle from** (all, bundled, custom or favorites), **Seconds per preset** and **Blend time**. Single has one **Visualizer** picker, and that preset stays on screen. Besides the like key, presets can be rated after the visualizer has closed: **Last shown** names the preset that was just on screen, with a heart to add it to your favorites and a block button to leave it out of shuffle. **Favorites** and **Blocklist** open a list where you can remove entries or search all presets to add more.

- **Audio delay:** with Bluetooth or AirPlay speakers the picture runs ahead of the sound. In settings, drag **Audio delay** until they match, or play something out loud and press **Detect**. The row names the speakers the value is for, and it changes by itself when you switch devices. Detect can't work with headphones, since the microphone can't hear them; it then says so and keeps the old value.
- **Manual delay test:** in settings, **Start** next to **Manual delay test** opens a sheet and pauses Spotify if it was playing. A beep plays every second through the current speakers or headphones, and the panel lights up one audio delay after each beep is sent. Drag the slider, or use the **−10 ms** and **+10 ms** buttons, until the panel lights up exactly when you hear the beep. Every fourth beep is higher and its flash is orange, so with a long delay (AirPlay) you can still tell which flash belongs to which beep. **Done** saves the delay for that device and starts Spotify again. It needs no microphone.
- **Your own presets:** the presets folder is `~/Library/Application Support/IdleViz/Presets/`. In settings under **Presets**, **Import…** copies files or whole folders into it (`.json`, `.milk`, `.js`), **Open** shows it in Finder, and **Reload** reads it again, though it also notices changes by itself. Custom presets appear in the **Visualizer** picker and the favorites and blocklist sheets, and **Shuffle from** can be set to **Custom**. Anything that couldn't be loaded is listed under **Failed to load** with the reason and a **Reveal** button; fix or replace the file and it is tried again. Custom plugins and presets are code, so only import ones you trust.

## Custom visualizers

You can add your own visuals without touching the app. The easiest way is a single `.js` file that draws into a canvas and receives Spotify's audio levels every frame. [`aurora.js`](IdleViz/web/visuals/aurora.js) is a complete working example, and the full guide is [docs/custom-visualizer.md](docs/custom-visualizer.md).

## How it works

One borderless window holds one web page. The page stacks the Butterchurn canvas, a dim layer and the Spotify-style overlay. A Swift helper watches for idle time and hotkeys, reads now-playing info from Spotify, and sends audio analysis to the page about 60 times a second. The full design, including the decisions behind it, is in [project.md](project.md).

## Windows

A Windows 11 version is in [`windows/`](windows/): the same page in WebView2, with the helper around it rewritten in C# and WinUI 3. The brief is [windows-port.html](windows-port.html), and what was built and decided is in [docs/windows.md](docs/windows.md).

> **Status: every step is built.** The Windows app lives in the tray and opens the visualizer full screen after a few idle minutes, or from a hotkey, a URL or its menu, while Spotify has a track loaded, and closes it on any input or at the keep-awake limit. The window shows the same page as the Mac, with the Spotify overlay, and the visuals move to Spotify's sound. Settings has the preset controls (shuffle or one preset, favorites, blocklist), your own presets and plugins, and the audio delay with Detect and the manual test. While open it keeps the screen awake, up to a limit, and the tray icon turns yellow if Spotify's sound or track can't be read. It can cover more than one display, each with the same visualizer or all with one extended picture. It has only been used on one PC, a desktop with two displays and no battery, so the battery times are untested on real hardware. Everything under [Features](#features) and [Usage](#usage) describes the Mac app.

| Step | What                                                                                              | Status |
| ---- | ------------------------------------------------------------------------------------------------- | ------ |
| W0   | Scaffold: solution, `IdleViz.Core` with its first tests, CI job, installer script, empty tray app | Done   |
| W1   | Open/close shell: tray flyout and menu, hotkey, URL, black fullscreen window, dismiss             | Done   |
| W2   | Spike: Spotify-only audio capture, the page in WebView2, what the media controls report (findings only) | Done |
| W3   | Spotify now-playing from the Windows media controls, content type, artwork                        | Done   |
| W4   | Open rules and tray icon flash                                                                    | Done   |
| W5   | The page in the window: overlay, CSP, all overlay states                                          | Done   |
| W6   | Idle trigger, with skip rules                                                                     | Done   |
| W7a  | Visualizer: real Spotify audio, and recovery from a stuck page                                    | Done   |
| W7b  | Visualizer: preset controls                                                                       | Done   |
| W7c  | Visualizer: custom preset folder, sandboxed plugins, `.milk` conversion                           | Done   |
| W7d  | Visualizer: audio delay, with Detect and the manual test                                          | Done   |
| W8a  | Polish: keep-awake limit, battery times, fades                                                    | Done   |
| W8b  | Polish: warnings (yellow icon, flyout rows)                                                       | Done   |
| W8c  | Polish: brightness, overlay switch, run at startup, like, skip and media keys                     | Done   |
| W9   | More than one display: main display, mirror or extend, overlay display, close on input            | Done   |

**Requirements:** Windows 11. To build: the .NET 10 SDK and [Inno Setup 6](https://jrsoftware.org/isinfo.php).

**Install:** there is no download. In PowerShell, from the repo:

```powershell
cd windows
.\build-installer.ps1 -Install
```

This builds the app and `windows\artifacts\IdleViz-Setup.exe`, runs it without questions, and starts IdleViz. It installs for your user only, in `%LOCALAPPDATA%\Programs\IdleViz`, so there is no admin prompt, and adds a Start menu entry. Run it again after pulling changes to update. Without `-Install` it only builds the setup file, which you can run yourself. Uninstall from **Settings → Apps → Installed apps**.

**Use:** what works so far.

- **Tray icon:** IdleViz appears in the notification area, possibly behind the **^** overflow arrow. Left-click it for a small flyout with **Settings** and the current open hotkey. Right-click it for a menu with **Settings**, **Open visualizer** and **Exit**. Exit is the only way to quit; closing the settings window leaves the app running.
- **Warnings:** if IdleViz can't capture Spotify's sound or can't read what Spotify is playing, the tray icon turns yellow and the flyout gets a row for the problem. Click the row for the error text and a button that opens the log folder. The check runs again whenever you open the flyout, when Spotify starts and when the visualizer opens, and the warning goes away by itself once it passes. Spotify not running, and silence because Spotify plays on another device or is muted, are not warnings.
- **Idle:** after 5 minutes without keyboard or mouse input (change it or turn it off with **Start after idle** in settings) it opens by itself, under the same Spotify rule as below. It doesn't while the PC is locked, used over Remote Desktop, showing something fullscreen or in presentation mode, or while any app other than Spotify is making sound (a video or a call in a normal window, say). A refused or skipped idle open shows nothing and isn't tried again until you've used the PC.
- **Keep awake:** while the visualizer is showing, the display doesn't turn off and the PC doesn't lock by itself. After the **Keep screen awake** time (1 hour by default; 30 minutes, 2 hours or 4 hours also available), counted from when it opened, it fades out slowly and Windows' own screen, sleep and lock settings take over. It then stays closed until you use the PC again. It also closes, at once, when the PC goes to sleep or a display is added, removed or changes its resolution or scale.
- **On battery:** on a laptop or tablet, settings has **Different times on battery**. Turn it on for a separate idle time and keep-awake time that apply while the PC runs on its battery, from the moment you unplug or plug in, even while the visualizer is open. PCs without a battery don't show it, and a desktop PC on a UPS counts as having none. This part has only been checked with a pretend battery, as the test PC has no real one.
- **Open:** press the hotkey (Ctrl + Alt + V by default), run `start idleviz://open` in a terminal, choose **Open visualizer** in the right-click menu, or click **Open** next to **Open now** in settings. Spotify has to be running with a track loaded, a song or a podcast, playing or paused. If it isn't, the tray icon flashes to a slashed waveform 3 times and nothing opens. Otherwise the visualizer fades in over the whole main display, taskbar included.
- **Displays:** in settings under **Displays**. **Main display** is the display the visualizer opens on: the one Windows calls primary, or one you pick. Turn on **Use more than one display** to cover others too; **Other displays** ticks which ones (all of them unless you change it). **Placement** is **Same on each display**, where every display runs its own copy of the visualizer with the same preset and the same sound, or **Extend across displays**, where one picture runs across all of them. Extended, the picture covers the rectangle that encloses the displays, so with displays of different sizes the parts no display covers aren't seen. **Spotify overlay on** puts the track, cover and progress bar on the main display, on one display you pick, or on all of them. **Close on input** can be turned off, so you can keep working on another display: the visualizer then stays until you press the hotkey again, choose **Open visualizer** again or the keep-awake limit is reached, the pointer stays visible, and the like and skip keys are off. These four rows only count while **Use more than one display** is on. Covering more than one display takes more GPU power and the visualizer may run less smoothly; settings shows a warning while the switch is on. A change made while the visualizer is open applies the next time it opens.
- **Close:** move the mouse, click, scroll or press any key other than the like and skip keys and the media keys below. The window fades out quickly and focus returns to the window you were in. The key or click that closes it is not passed on to that window. Input in the first 0.4 s after opening is ignored, so the hotkey itself doesn't close it, and keys you're still holding after that are ignored until you let go; pressing one again closes it.
- **Media keys:** mute, volume, play/pause, next and previous do their usual job and leave it open.
- **Like and skip:** while the visualizer is showing, **L** adds the preset on screen to your favorites, or takes it off again, and a heart confirms it. **N** blends to the next preset in about half a second; it does nothing in Single mode. Both work even if another window has the keyboard, and the key is not passed on to that window. Holding a key down doesn't repeat it, and with Ctrl, Alt, Shift or the Windows key it closes the visualizer like any other key. Change the keys, or turn them off, with **Like key** and **Skip key** in settings: A to Z, 0 to 9, the arrow keys or Space.
- **Brightness and overlay:** in settings under **Visualizer**, **Brightness** dims the visualizer (50 to 100%, 70% by default) and **Show Spotify overlay** hides the track, cover and progress bar altogether. Both apply at once, even while the visualizer is open.
- **Run at startup:** turn it on in settings and IdleViz starts in the tray when you sign in. If you switch IdleViz off in Windows' own list (Settings → Apps → Startup), the row shows Off with a link to that list; turn it back on there.
- **Settings:** **Start after idle**: 5, 10, 15 or 30 min, or Off. **Keep screen awake**: 30 min, 1, 2 or 4 hours. Click **Open hotkey**, then press a key together with Ctrl, Alt, Shift or the Windows key to change it. Esc cancels, and the **✕** button clears it. If another app already has the stored combination, the row says so.
- **Visualizer:** the 395 Butterchurn presets bundled with the page. By default a random one every 30 seconds with a 2.7-second blend, going through all of them before repeating one. They move to Spotify's sound, and only Spotify's: IdleViz captures Spotify's own audio while the window is open, never the microphone or other apps, so a video playing in another window leaves the visuals still. A low Spotify volume is evened out, so quiet listening still gives lively visuals. While Spotify plays on another device (Spotify Connect), is muted in the Windows volume mixer or isn't running, the visuals get silence. If a preset or plugin freezes the page, the page is replaced within about 4 s and that preset is left out until IdleViz restarts, or, for your own files, until the file changes.
- **Presets:** in settings under **Visualizer**, **Mode** is **Shuffle** or **Single**. Shuffle has **Shuffle from** (All, Bundled, Custom or Favorites), **Seconds per preset** and **Blend time**; if the chosen set is empty, the row says all presets are used instead. Single has one **Visualizer** picker, and that preset stays on screen. **Last shown** names the preset that was just on screen, with a heart to add it to your favorites and a block button to leave it out of shuffle. **Favorites** and **Blocklist** open a dialog where you can remove entries, or search all presets to add more; a preset is never on both lists. Every change applies at once, even while the visualizer is open.
- **Your own presets:** the presets folder is `%APPDATA%\IdleViz\Presets\`. Drop Butterchurn `.json` or original Milkdrop `.milk` files, or custom `.js` visual plugins ([how to write one](docs/custom-visualizer.md)), into it; subfolders work, so a downloaded pack can go in as it is. The folder is watched, so new files show up within a second, without a restart. `.milk` files are converted once and cached. In settings under **Presets**, **Import…** copies files or a folder in (a name that's taken gets a number, as File Explorer does; nothing is replaced), **Open** shows the folder in File Explorer, and **Reload** reads it again. Custom presets appear in the **Visualizer** picker and the favorites and blocklist dialogs, and **Shuffle from** can be set to **Custom**. Plugins run in a sandboxed frame with no network, no storage and no access to the rest of the app. Anything that couldn't be loaded (a file that isn't a preset, a plugin that throws, runs too slowly or freezes the page) is listed under **Failed to load** with the reason and a **Reveal** button; fix or replace the file and it is tried again. Custom plugins and presets are code, so only import ones you trust.
- **Audio delay:** with Bluetooth or network speakers the picture runs ahead of the sound. In settings under **Visualizer**, drag **Audio delay** (0 to 2500 ms) until they match, or play something in Spotify out loud and press **Detect**: IdleViz listens with a microphone for about 5 seconds, compares what it hears with what Spotify sent, and sets the delay. The recording stays in memory and is dropped afterwards, and the microphone is only on while the button says **Listening…**. The row names the speakers the value is for, and the value changes by itself when you switch output devices. Detect can't work with headphones, since the microphone can't hear them; it then says so and keeps the old value. **Microphone** chooses what Detect listens with: **Automatic** takes the PC's own microphone, else the default input. A Bluetooth microphone is refused, because using it would change the delay. If Windows doesn't let desktop apps use the microphone, the row links to the Windows setting.
- **Manual delay test:** **Start** next to **Manual delay test** opens a dialog and pauses Spotify if it was playing. A beep plays every second through the current speakers or headphones, and the panel lights up one audio delay after each beep is sent. Drag the slider, or use **−10 ms** and **+10 ms**, until the panel lights up exactly when you hear the beep. Every fourth beep is higher and its flash is orange, so with a long delay you can still tell which flash belongs to which beep. **Done** saves the delay for that device and starts Spotify again. It needs no microphone.
- **Overlay:** what Spotify is playing, read from the Windows media controls with no Spotify login (it never starts Spotify). A playing song shows its cover, title, artist and a moving progress bar; a paused song only the progress bar; a podcast nothing. When Spotify stops having a track, the overlay goes 1.5 s later. Ads aren't told apart from songs on Windows; none has been seen yet, as the test PC has Spotify Premium.
- **Log:** `%LOCALAPPDATA%\IdleViz\logs\idleviz.log` records each start, open and close, what closed it, why an open was refused, every change in what Spotify is playing, whether the page loaded, which preset is showing, and, at each close, how many frames the page drew and how much Spotify audio it got.

**Develop:** in `windows/`, `dotnet format --verify-no-changes`, `dotnet build` and `dotnet test`. The build treats warnings as errors. Details are in [docs/windows.md](docs/windows.md).

## Development

You need Node 22 or later. The JavaScript side (plugins, overlay page) is checked with:

```bash
npm install
npm run check
```

`npm run check` runs ESLint, Prettier, a TypeScript typecheck over the page's JavaScript, and the Vitest suite. The test suite loads every visualizer plugin against a fake WebGL2 context and checks it against the plugin guide: required exports, canvas sizing, no NaN values sent to the GPU, everything freed on dispose, and no forbidden APIs.

The Swift side is split in two. `IdleVizCore` (`Package.swift`, `Sources/`, `tests/IdleVizCoreTests/`) holds logic that runs without a screen, such as the dismiss rules and parsing Spotify's replies. The app target in `IdleViz.xcodeproj` (`IdleViz/App/`) is a thin AppKit and SwiftUI layer on top.

```bash
swift build && swift test
swiftlint lint --strict
```

In Debug builds, the launch argument `-IdleVizNoDismiss YES` keeps the window open so it can be inspected; trigger it again to close. `-IdleVizShowSettings YES` opens the settings window at launch, `-IdleVizShowWelcome YES` the welcome window, `-IdleVizFakePermissions automation=notAsked,audio=denied` shows the yellow icon, error rows and welcome window in that state without touching the real permissions, `-IdleVizDetectDelay YES` runs Detect delay four seconds after launch, and `-IdleVizOpenAtLaunch YES` opens the visualizer three seconds after launch (unlike `open idleviz://open`, that can't end up in another copy of the app). The page is inspectable in Debug builds: with the window open, attach Safari's Web Inspector from **Develop → [your Mac] → IdleViz**. The scheme has this argument ready to tick. Each open logs whether macOS let the app activate, and each preset change is logged too:

```bash
log stream --predicate 'subsystem == "com.xauno.IdleViz"'
```

Add `--level debug` to also see, once a second, what the tap delivered (buffers, gain, levels) and how many frames the page rendered and received.

CI runs all of this on every pull request, and builds the app unsigned with `xcodebuild` on a `macos-26` runner.

## Project layout

```
.
├─ project.md              Design and build order
├─ LICENSE                 MIT license
├─ AGENTS.md               Instructions for AI coding agents (CLAUDE.md points to it)
├─ IdleViz.command         Builds the app, installs it in /Applications and opens it
├─ Package.swift           IdleVizCore Swift package (testable logic)
├─ Sources/IdleVizCore/    Dismiss rules, the like and skip keys, open rules, idle timing and skip rules, keep-awake and battery times, fade times, permission states, brightness and overlay settings, page scheme and CSP, overlay payload, URL commands, activation stats, Spotify query parsing and tracking, audio analysis (bands, automatic gain, frame packing), page status checks, preset settings, the custom presets folder (scanning, import names, Milkdrop conversion checks), the audio delay (per-device setting, delay line, delay detection, the manual delay test's timing and beeps)
├─ IdleViz.xcodeproj       App target: bundle, Info.plist, entitlements, signing
├─ IdleViz/App/            Menu-bar app, settings window, welcome window and permission checks, triggers, fullscreen window, dismiss, keep awake, power source, Spotify info, Spotify audio tap, web view, presets folder and Milkdrop converter
├─ IdleViz/web/            The page: visualizer and overlay HTML, CSS and JS, the plugin frame and its runner, the converter page, Figtree font (served from idleviz-app://)
├─ IdleViz/web/vendor/     Butterchurn, its preset packs and the Milkdrop converter, copied unchanged from npm
├─ IdleViz/web/visuals/    Bundled visualizer plugins (aurora.js, the example plugin)
├─ Config/                 Build settings; your signing team goes in Local.xcconfig
├─ aurora-demo.html        Test page that feeds a plugin audio from a file or microphone
├─ mockups.html            UI mockups for the menu-bar popup and the settings window
├─ windows-port.html       Brief for building a Windows version: what to reuse, the Windows UI, every decision so far
├─ windows/                The Windows app: IdleViz.Core (logic) and its tests, IdleViz.App (WinUI 3), the installer script
├─ docs/                   Guides, including the custom visualizer guide
├─ tests/                  Vitest suite, fake WebGL helpers, and the Swift tests (IdleVizCoreTests)
└─ .github/                CI workflow, PR template, Dependabot
```

## Contributing

`main` is protected. All changes go through a pull request with passing CI. See [CONTRIBUTING.md](CONTRIBUTING.md) for the workflow.

## License

[MIT](LICENSE). Butterchurn, the bundled preset packs and the Milkdrop converter are MIT licensed too; their license files are in [IdleViz/web/vendor/](IdleViz/web/vendor/). Milkdrop presets and plugins that you import yourself keep their own licenses.

## Acknowledgments

- [Butterchurn](https://github.com/jberg/butterchurn), [butterchurn-presets](https://github.com/jberg/butterchurn-presets), [milkdrop-preset-converter](https://github.com/jberg/milkdrop-preset-converter) and [Milkdrop](https://www.geisswerks.com/milkdrop/), the visualizer engine and the presets it plays
- [KeyboardShortcuts](https://github.com/sindresorhus/KeyboardShortcuts) for the global hotkey
- [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) for the tray icon, and the [Windows Community Toolkit](https://github.com/CommunityToolkit/Windows) for the settings cards, on Windows
- [Figtree](https://github.com/erikdkennedy/figtree) for the overlay font
