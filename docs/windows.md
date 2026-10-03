# IdleViz for Windows

The Windows version of IdleViz: the same app as on the Mac, behaving the same way, looking like it belongs on Windows 11. This file records how it is built and what was decided. [windows-port.html](../windows-port.html) is the original brief (open it in a browser), and [project.md](../project.md) is the full Mac design that the brief refers to.

The page in `IdleViz/web/` is shared by both apps and is not forked. The native helper around it is rewritten in C#.

## Layout

```
windows/
├─ IdleViz.sln
├─ global.json               .NET SDK version, and the test runner dotnet test uses
├─ Directory.Build.props     Version, and the lint settings every project gets
├─ Directory.Packages.props  NuGet package versions, in one place
├─ .editorconfig             C# style rules
├─ IdleViz.Core/             Logic that needs no screen, ported from Sources/IdleVizCore
├─ IdleViz.Core.Tests/       xUnit tests, ported from tests/IdleVizCoreTests
├─ IdleViz.App/              The WinUI 3 app: tray icon, windows, system calls
├─ installer/IdleViz.iss     Inno Setup script
├─ tools/make-icon.ps1       Draws IdleViz.App/Assets/IdleViz.ico
└─ build-installer.ps1       Builds the app and the setup file
```

## Build and test

Needs the .NET 10 SDK, and [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the setup file. Run these in `windows/`, in PowerShell (not WSL, which can't build or start a WinUI app):

```powershell
dotnet format --verify-no-changes   # style check
dotnet build                        # the analyzers run here, and any warning fails the build
dotnet test
.\build-installer.ps1               # artifacts\IdleViz-Setup.exe
.\build-installer.ps1 -Install      # the same, then installs it and starts the app
```

- **App.** Unpackaged and self-contained, x64 only: the installed folder needs nothing else on the PC. Windows 11 only (minimum build 22000), with no fallbacks for Windows 10.
- **Publishing needs `EnableMsixTooling`**, although the app isn't an MSIX package. Without it `dotnet publish` leaves out the compiled XAML (`IdleViz.pri`), and the installed app starts but crashes as soon as it opens a window, while the Debug build works. `build-installer.ps1` checks the file is there.
- **Tests.** xUnit v3 on Microsoft.Testing.Platform, which is what `dotnet test` needs on the .NET 10 SDK.
- **Lint.** `dotnet format` checks the `.editorconfig` style. The built-in .NET analyzers (`latest-recommended`) and the code-style rules run in every build with warnings as errors.
- **Installer.** Per-user, so no admin prompt: files go to `%LOCALAPPDATA%\Programs\IdleViz`, with a Start menu entry, an uninstaller under Installed apps, and the `idleviz://` protocol registered for the user. Installing over a running copy stops it first. Unsigned, so a downloaded setup file would get a SmartScreen warning; one built on the same PC doesn't.
- **CI.** The `windows` job on `windows-latest` runs the format check, build, tests and the installer build.
- **Line endings.** `.gitattributes` makes every checkout LF. With CRLF, Prettier fails and the vendored libraries no longer match their checksums.

## Third-party packages

| Package                                          | Used for                                            |
| ------------------------------------------------ | --------------------------------------------------- |
| H.NotifyIcon.WinUI                               | The tray icon and its clicks                        |
| CommunityToolkit.WinUI.Controls.SettingsControls | The settings cards in the settings window           |
| Microsoft.Windows.CsWin32                        | Generates the Win32 calls (build-time only)         |
| xunit.v3                                         | Tests                                               |

## Decisions

Answers the owner gave to the open points in section 9 of the brief, on 2 October 2026.

| Point | Decision |
| ----- | -------- |
| J1 | Code lives in `windows/` in this repository and loads `IdleViz/web/` as it is. xUnit for tests. Lint is `dotnet format` plus the built-in analyzers, warnings as errors. |
| Packages | A few well-known third-party packages are fine (H.NotifyIcon.WinUI, NAudio, CsWin32). Each new one is named in its pull request and in the table above. |
| J2 | Settings window as suggested: about 440 × 680, title "IdleViz Settings", "PC" for "Mac", "Run at startup", the Mac's sheets as `ContentDialog`s, the trust warning as an `InfoBar`. |
| J3 | Low-level keyboard and mouse hooks while the visualizer is open. Like and skip work without focus. The key or click that closes the visualizer is swallowed, so it never reaches the app behind. |
| J4 | Two warnings: "Spotify audio can't be captured" and "Can't read what Spotify is playing". Clicking one opens a small dialog with the error text and a button that opens the log folder. |
| J5 | Inno Setup, per-user install in `%LOCALAPPDATA%\Programs\IdleViz`, built by `windows/build-installer.ps1`. Presets folder `%APPDATA%\IdleViz\Presets\`. A taken import name is numbered as in File Explorer: "Tunnel (2).milk". |
| J6 | Default open hotkey Ctrl + Alt + V. |
| R3 | The idle trigger is skipped while the session is locked, an app is fullscreen or presenting, or any app other than Spotify is producing sound. A silent video in a normal window is still missed. |
| Microphone | Detect delay uses the built-in microphone if there is one, otherwise the default input, unless that is Bluetooth: then it doesn't run and the hint says why. Settings also gets a microphone picker, a row the Mac app doesn't have. |
| Battery | Battery times are built and unit-tested. The development PC has no battery, so they have not been run on one. |
| Ads | Not recognised on Windows. Windows doesn't say what an item is and no ad could be observed (Premium), so nothing unverified is built: no artist means podcast, anything else is a song. To be revisited if someone with a free account reports what an ad looks like. |
| Track gaps | A track seen in the last 1.5 s still counts as a track while Spotify is running, so an open during the brief "no track" between two items isn't refused. |
| First reading | A trigger that comes before Windows has said anything about Spotify (the app has just started) waits up to 2 s for the first reading, then decides. |
| App icon | The tray's five-bar waveform, white on a dark rounded square. |
| Docs | The README has a Windows section with its own roadmap. This file holds the as-built design and the spike findings. The brief stays as it was written. |

The W2 spike answered R1, R4 and R5, and gave the facts for R2. R6 could not be measured. See the next section.

After the spike the owner approved one change to the shared page: it also accepts `https://app.idleviz.invalid/visuals/…` and `https://presets.idleviz.invalid/…` for plugin and preset URLs (R5 below).

## Spike findings (W2)

A throwaway build on the branch `spike/windows-w2`, which is not merged. It added a `--spike` mode to the app (`windows/IdleViz.App/Spike/`) and a small side test (`windows/Spike.Scheme/`). Measured on 2 October 2026 on one PC: Windows 11 build 26200, Spotify 1.301 from the Microsoft Store (Premium), WebView2 runtime 124.0.2478.51, an RTX 3080, primary display 3440 × 1440 at 120 Hz.

### Answers

| Point | Answer |
| ----- | ------ |
| R1: position and duration | **Yes.** Spotify reports both, and raises a timeline change about every 4.5 s while playing, and on every seek, pause and resume. |
| R2: podcast or ad | **A podcast has an empty artist**, as on the Mac. Its album is the show's name and the track number is 0. Windows calls it "Music" like a song. **Ads were not seen** (Premium), so nothing is known about them. The rule is for W3 to settle with the owner. |
| R4: fading with WebView2 | **Window opacity works.** With the page running in WebView2 inside the layered window, setting the window to half opacity showed the desktop through the page. Checked on a screenshot at one fixed opacity; nobody has watched a moving fade. |
| R5: scheme, frame, CSP | **The host page works from `idleviz-app://` unchanged; the sandboxed plugin frame does not.** Details below. Fixed by serving the page from an `https:` address the app answers itself, plus the one approved page change. |
| R6: frame cap above 120 Hz | **Not measured**: there is no display faster than 120 Hz here. On 120 Hz the page renders 60 fps. By the page's rule (skip a frame closer than 12 ms to the last) a 144 Hz display would give 72 fps, 165 Hz 82.5 and 240 Hz 80. |

### Spotify-only audio

- **Process loopback works on the Store version of Spotify.** `ActivateAudioInterfaceAsync` on `VAD\Process_Loopback`, in "include the process tree" mode, aimed at the Spotify process that owns the main window. Spotify runs as seven processes; the one with a window is their parent.
- It needs no package. The interop is about 100 lines: the activation parameters go in as a `VT_BLOB` `PROPVARIANT`, and the completion handler is an ordinary C# class.
- The capture format has to be given: `GetMixFormat` and `GetStreamLatency` return "not implemented". 32-bit float, 48 kHz, stereo was accepted, with the flags for loopback, event callback and automatic conversion.
- Packets are 480 frames (10 ms), 100 a second. The first arrived 15 to 30 ms after the start. No permission prompt and no recording indicator appeared.
- **It hears only Spotify.** With Spotify paused, a 440 Hz tone played by another program read as exact zero.
- **While Spotify is paused, packets keep coming, filled with zeros.** So "no packets" is not the sign of a pause; the level is.
- **Volume.** The capture is taken before the system volume (5 % and 52 % read the same) but after Spotify's volume in the Windows mixer (10 % read ten times lower) and after Spotify's own volume slider (raising it doubled the level). At the owner's usual slider position the level was about −42 dBFS, so the automatic gain has work to do.
- **Spotify Connect:** with playback moved to another device the capture is silent, as expected, while the media controls keep reporting (below).

### The page in WebView2

- WebView2 can be created straight on the visualizer's plain window handle (`CoreWebView2ControllerWindowReference.CreateFromWindowHandle`), with no WinUI window. The environment took about 20 ms, the controller about 200 ms, and the page was loaded 700 to 800 ms after the start.
- **It does not take focus**: the app in front stayed in front. The pointer was hidden over it, both at rest and after a move.
- **Frames.** `ExecuteScriptAsync("window.audioFrame?.(\"…\")")` 60 times a second: all 60 arrived, none were dropped, and a call took 0.6 to 0.9 ms from send to completion. Building a frame (with a rough analysis) took 0.25 ms.
- **Butterchurn held 59 to 60 fps** at 3440 × 1440 over 30 seconds.
- **Cost:** the app used about 6 % of one core, the WebView2 processes together about 25 % of one core (renderer about 17 %, GPU process about 8 %).
- From `idleviz-app://app/index.html` the unchanged page was a secure context with origin `idleviz-app://app`, loaded its ES modules, and `new Function` worked under the CSP sent as a response header.
- The page logs one warning, "The AudioContext was not allowed to start" (`visualizer.js`, line 48). Butterchurn renders all the same.

### R5 in detail: the plugin frame

The plugin frame is sandboxed without `allow-same-origin`, so its origin is opaque. WebView2 refuses every request from an opaque origin to a custom scheme: the frame's `plugin-runner.js` failed with a CORS error and the request never reached the app. This held with the scheme's allowed origins empty, `*`, `null` and `idleviz-app://*`. Microsoft's documentation says the same.

Served from `https://app.idleviz.invalid/` instead, the frame loaded its runner and the bundled Aurora plugin, with the sandbox attribute unchanged. The app answers these addresses in `WebResourceRequested` before anything goes to the network, and `.invalid` is a reserved name that never resolves. This needs no custom scheme at all.

So the Windows app will serve:

| Mac | Windows |
| --- | ------- |
| `idleviz-app://app/…` | `https://app.idleviz.invalid/…` |
| `idleviz-app://presets/…` | `https://presets.idleviz.invalid/…` |

The CSP is sent by each app, so Windows sends its own with these hosts in place of `idleviz-app:`. The only place where the page itself names the scheme is the list of URL prefixes it accepts for plugins and presets, and that list now holds both forms (the approved page change). The spike showed this working with the plain .NET WebView2 API; the WinUI flavour used by the app has not served an `https:` address yet.

### What Spotify reports

Through `GlobalSystemMediaTransportControlsSessionManager`. The Store version's session is `SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify`. The version from spotify.com identifies itself differently and was not tested.

| Case | What was reported |
| ---- | ----------------- |
| Song, playing | Title, artist, album, album artist, track number. Playback type "Music". Status Playing. Position, and the duration as the timeline's end. Shuffle and repeat state. |
| Artwork | A PNG thumbnail, 50 to 120 KB. |
| Song, paused | Status Paused. The position stops. Timeline changes keep arriving with the same position. |
| Seek | A timeline change at once with the new position. |
| Switching to something else (another album, a podcast) | For about half a second: an empty title, no thumbnail and a duration of 0. Then the new item. This is the "no track for a moment" the page's 1.5 s wait is for. Seen in W3 and W4 when switching from a song to a podcast. Spotify's crossfade is off on this PC, so crossfade isn't the cause. |
| Next track, or a song ending by itself | The new track at once, no gap (checked in W4). Each change arrives two or three times. |
| Podcast | Title of the episode, **artist empty**, album is the show's name, track number 0, playback type still "Music", a thumbnail, position and duration. |
| Spotify Connect (playing on another device) | The same as a song playing here: status Playing, position moving, seeks and pauses reported. Nothing says the sound is elsewhere. |
| Local file | Not tested. |
| Ad | Not seen (Premium). |

There is no track ID or Spotify URL, so a track change has to be recognised from title, artist and album.

### Things the spike ran into

- **The WinUI flavour of WebView2 ignores `Add` on its lists.** `options.CustomSchemeRegistrations.Add(…)` and `scheme.AllowedOrigins.Add(…)` do nothing, with no error: the getters hand out copies. Assigning a whole list to `CustomSchemeRegistrations` works. `AllowedOrigins` cannot be set at all.
- **A `DispatcherQueueTimer` kept only in a local variable is collected and stops** after a few seconds. Timers must live in fields. The `--open-at-launch` debug switch has this fault.
- **The WebView2 runtime on this PC is old** (124, from April 2024) although Windows 11 is current. The app must not assume a recent one.
- **A request handler that throws** makes the navigation fail with "connection aborted" and no other sign.

## As built

### W0: scaffold

- `IdleViz.Core` starts with `Fade.cs` (`CloseReason`, the fade times) and `Trigger.cs` (`TriggerSource`, the `idleviz://` commands), ported from the Swift files of the same names with their tests. The URL parser takes text, not a URL object, because Windows hands the protocol URL over as a command-line argument that may be anything.
- The app is a tray icon and nothing else: tooltip "IdleViz", and a right-click menu with **Exit**. It has no window, flyout, hotkey or visualizer yet.
- The right-click menu is the native Windows popup menu. The library's XAML menu needs a window to live in, and the app has none.
- The setup file is about 62 MB and installs about 234 MB, because the .NET runtime and the Windows App SDK are inside it.

### W1: open/close shell

The app opens a black fullscreen window from the hotkey, the URL, the tray menu or **Open now**, and closes it on any input. There are no open rules yet (it opens whether or not Spotify has a track), no page in the window and no idle trigger.

**Files**

| File | Job |
| ---- | --- |
| `Program.cs` | Entry point. One running copy: a second launch hands its command line to the first and exits. |
| `App.xaml.cs` | Wires everything together at launch. |
| `NativeWindow.cs` | A plain Win32 window, the base of the next two. |
| `HotkeyWindow.cs` | A window that is never shown. Receives the global hotkey and the taskbar's light/dark change. |
| `VisualizerWindow.cs`, `VisualizerController.cs` | The fullscreen window, and its open/close state, fades, focus and cursor. |
| `DismissWatcher.cs` | Keyboard and mouse hooks while open, and the backup check. |
| `TrayIcon.cs`, `TrayGlyph.cs`, `TrayMenu.cs`, `FlyoutWindow.xaml` | The tray icon, its drawing, the right-click menu and the left-click flyout. |
| `SettingsWindow.xaml`, `HotkeyRecorder.xaml` | The settings window and the hotkey recorder. |
| `Log.cs` | The log file. |

In `IdleViz.Core`: `DismissTracker.cs` (ported with its tests), `KeyboardInput.cs`, `Hotkey.cs`, `SettingsStore.cs` and `LaunchOptions.cs`.

**The window**

- A plain Win32 window, not a WinUI one: borderless, topmost, covering the primary display's full bounds, taskbar included. It is created at launch and hidden between opens.
- It is a layered window, so the fade is the window's own opacity and the desktop shows through, as on the Mac. In over 0.6 s, out over 0.25 s, eased. Whether this still works once the window holds WebView2 is the R4 question for the W2 spike.
- When a fade-out starts the PC is handed back at once: the hooks are removed, the cursor returns, clicks pass through the window, and focus goes back to the window that had it. The window is hidden when the fade ends. A trigger during a fade-out finishes the close and opens again.
- On open it asks for keyboard focus and logs whether Windows gave it. Nothing depends on it, since the hooks see input either way.
- The cursor is hidden over the window by answering `WM_SETCURSOR`. Windows only sends that when the mouse moves over the window, and a move closes the visualizer, so a pointer at rest kept the arrow in about one open in four. While open, the window therefore checks every 50 ms whether a cursor is showing over it and, if so, sets the pointer to where it already is, which makes Windows send the message without moving anything. A pointer on another display stays visible.
- A manual trigger while it is open does nothing.

**Dismiss**

- `WH_KEYBOARD_LL` and `WH_MOUSE_LL` hooks, installed when the window opens and removed when it starts to close. They need no permission.
- The input that closes the visualizer is swallowed: the key press, click or scroll never reaches the app behind. A mouse move is let through, so the pointer doesn't stick. The release of a swallowed key or button does reach the app behind, on its own, which does nothing.
- Grace period 0.4 s. Keys still held when it ends are stuck, as on the Mac: their release and repeat are ignored, and a fresh press closes.
- The hook doesn't mark key repeats, so `KeyboardInput` remembers which keys are down.
- Key codes are Windows virtual-key codes. The hooks report the sided modifiers (left Ctrl is `0xA2`), so the key-state scan skips the unsided ones (`0x10` to `0x12`), which would otherwise look like extra held keys.
- Mute, volume down and up, next, previous and play/pause don't close it and still do their job. Media stop and every other key close it. Brightness and keyboard-backlight keys never arrive as keys on Windows, so there is nothing to handle. Like and skip come in W8c.
- Backup check every 100 ms against `GetLastInputInfo`, with the 30 ms second look, for input the hooks missed: Windows silently drops a hook that answers too slowly.

**Tray**

- The glyph is drawn at run time, white on a dark taskbar and black on a light one (`SystemUsesLightTheme`), and redrawn when that changes.
- The flyout is a small WinUI window with the acrylic backdrop, placed in the corner of the primary display's work area. It closes on Esc or when it loses focus.
- The right-click menu is a WinUI `MenuFlyout`, so it looks like a Windows 11 menu. It needs a window to belong to, so `TrayMenu` makes an invisible one at the pointer for as long as the menu is open. H.NotifyIcon has the same idea built in (`SecondWindow`), but it creates that window at launch, and it took keyboard focus from the app in use every time IdleViz started. Its native mode (`PopupMenu`) doesn't, but draws an old-style menu.
- The app sets `DispatcherShutdownMode` to explicit: by default a WinUI app ends when its last window closes, which here would be the flyout.

**Settings window**

- 440 × 680, fixed, Mica, Maximize disabled, with **Open hotkey** and **Open now** so far.
- The recorder lets go of the current hotkey while it listens, and registers it again on Esc or when it loses focus.
- A combination another app has registered can't be recorded at all: Windows gives the keys to that app and the recorder never sees them. The "Another app is already using this shortcut" hint under the row therefore shows when the stored hotkey is refused, which is at launch.
- The settings cards stack their control under the label below a width that is wider than this window, so the window lowers that width.

**Settings file:** `%LOCALAPPDATA%\IdleViz\settings.json`. The hotkey is two numbers, `openHotkeyKey` (a virtual-key code, 0 for cleared) and `openHotkeyModifiers` (Alt 1, Ctrl 2, Shift 4, Win 8). Nothing stored means Ctrl + Alt + V.

**Log:** `%LOCALAPPDATA%\IdleViz\logs\idleviz.log`, with every start, open (trigger, and whether focus was given), close reason, and what closed it ("key 0x41", "mouse move (4, 0)"). At 1 MB it is renamed to `idleviz.old.log`.

**Debug switches** (Debug builds only; they can also be passed to a second launch, which hands them to the running copy):

| Switch | Effect |
| ------ | ------ |
| `--no-dismiss` | Input doesn't close the visualizer. Trigger it again to close. |
| `--open-at-launch` | Opens the visualizer three seconds after launch. |
| `--show-settings` | Opens the settings window. |
| `--show-flyout` | Opens the tray flyout. |
| `--show-menu` | Opens the tray menu at the pointer. |
| `--hang-page` | Makes the page loop forever 5 s after each open, to try the stuck-page recovery (W7a). First launch only. |
| `--detect-delay` | Runs Detect delay 5 s after launch, as the button in settings would (W7d). First launch only. |
| `--pretend-battery` | Behaves as a laptop running on its battery, so the battery rows show (W8a). First launch only. |
| `--pretend-warning` | Shows both warnings, with made-up error text (W8b). First launch only. |

**A second launch** with no URL (clicking the Start menu entry while it runs) opens the settings window, since there is no other window to bring forward. This was not asked; it is the usual behaviour of a Windows tray app.

**The Debug copy and the installed copy are separate apps to Windows**: each keeps its own single running copy, and `idleviz://` always starts the installed one. With both running, a URL open goes to the installed copy and the second copy to start can't register the hotkey. Stop the installed copy before testing a Debug build, and test URL opens on an installed build.

### W3: Spotify now-playing

The app reads what Spotify is playing and writes it to the log. The open rules (W4) and the overlay (W5) use it.

| File | What it does |
| ---- | ------------ |
| `IdleViz.Core/NowPlaying.cs` | `MediaReading` (what Windows reported), `NowPlaying` (the current item, with the rules below), `SpotifySession` (which session is Spotify's). |
| `IdleViz.Core/SpotifyTracker.cs` | Keeps the current item across readings and says when it changed or the position jumped. |
| `IdleViz.Core/Artwork.cs` | `ArtworkCache` (the covers of the five most recent tracks) and the check that a cover is a JPEG, PNG or WebP, with its `data:` URL. |
| `IdleViz.App/SpotifyInfo.cs` | Talks to the Windows media controls, feeds the tracker, reads the covers, writes the log lines. |

**Reading Spotify.** Through `GlobalSystemMediaTransportControlsSessionManager`, the same source as the media flyout in Windows. It needs no Spotify login and cannot start Spotify: with Spotify closed there is simply no session.

- Only Spotify's session is read: an ID starting with `SpotifyAB.SpotifyMusic_` (Microsoft Store) or equal to `Spotify.exe` (spotify.com). The second form is how Windows names an app that isn't from the Store (by its program file); it is assumed, not seen, since only the Store version is installed here.
- It is driven by the session's three events (properties, playback, timeline). Nothing is polled. Events arrive on other threads and are handed to the UI thread. One reading runs at a time; events that arrive during a reading are merged into one more reading after it.
- If a reading fails, the last track is kept and the failure is logged once.

**The rules** (`NowPlaying.From`):

- **No track** is: no session, any status other than Playing or Paused, or an empty title. Spotify reports an empty title for about half a second when it switches to something else (a song to a podcast, say), so "No track" appears in the log then. A plain next track has no gap. The page waits 1.5 s before it hides the overlay for exactly this reason.
- **A podcast is an item with no artist.** Everything else is a song. **Ads are not recognised** (owner's decision, since no ad could be observed on Premium): an ad is treated as whatever it looks like, most likely a podcast.
- **Position.** Spotify reports its position only every few seconds, together with the time it was true. For a playing track the time since then is added, up to the track's length, so a reading between two reports isn't seconds behind. A report older than a minute, or from the future, adds nothing.
- **Track identity.** Windows gives no track ID, so title, artist and album together stand in for one.

**What counts as a change** (`SpotifyTracker`): a different track, a different state, or a length that differs by 2 s or more. Windows raises two or three events per change and a timeline event every few seconds; those are not changes. Spotify also reports the same song's length a second longer or shorter now and then, which is why the length has a tolerance. A **seek** is a position more than 1.5 s away from where the track should have got to by itself, within the same track.

**Artwork.** The cover comes from the session as a stream, not from the network. It is kept only if its first bytes say JPEG, PNG or WebP and it is no larger than 4 MB. Spotify's covers are PNG, 50 to 120 KB. A later cover for the same track replaces the first, because Spotify can hand over a new track with the previous cover for a moment.

**Seen in the log during a track change:** Windows changes the timeline a few hundredths of a second before the title, so the old title can appear once with the new track's length, at 0:00. It corrects itself with the next event.

**Log lines** (category `spotify`): the session being followed or lost, every change ("Song, Playing: …, at 0:29 of 5:25", "Podcast, Paused: …", "No track"), every seek, and each cover with its type and size.

### W4: open rules and the tray icon flash

Every trigger now goes through the open rules. A refused manual trigger flashes the tray icon.

| File | What it does |
| ---- | ------------ |
| `IdleViz.Core/OpenRules.cs` | `OpenRules.Refusal`: why the visualizer may not open right now, or null. |
| `IdleViz.Core/SpotifyTracker.cs` | Now also keeps `TrackLostAt`, when Spotify last went from a track to no track. |
| `IdleViz.Core/IconFlash.cs` | The flash's steps and timing. |
| `TrayGlyph.cs`, `TrayIcon.cs` | The slashed waveform and `TrayIcon.Flash`. |
| `App.xaml.cs` | `OpenVisualizer` applies the rules, waits for the first reading, logs refusals and flashes the icon. |

**The rules**, in this order:

1. **Not known yet:** no reading of the media controls has arrived since the app started. The trigger waits up to 2 s for the first one, then decides. A manual trigger during the wait makes the whole wait manual. After 2 s with no reading it is refused.
2. **Spotify isn't running:** Spotify has no media session. A track seen before Spotify quit doesn't count.
3. **No track:** the session has no current track (see W3). Unless Spotify had one less than 1.5 s ago: the gap Spotify reports when it switches to something else isn't a refusal.

Songs and podcasts, playing or paused, all open. An already open window ignores the rules (in the no-dismiss debug mode a second trigger closes it, as before).

**Refusals.** Each is logged in category `open` with its trigger and reason. A refused manual trigger (hotkey, URL, menu, **Open now**) flashes the tray icon: the slashed waveform and the normal one in turn, 3 times in about 1 s (6 steps of 170 ms, the Mac's timing). A refused idle trigger (W6) will show nothing.

**The tray icon library disposes the icon it had whenever it gets a new one,** so the icon is drawn again for every step. Swapping between two kept icons crashed the app on the second step.

**Seen when checking:** a freshly started Spotify is reported with its restored, paused track within about a second, before anything plays, so the visualizer opens at once.

### W5: the page in the window

The visualizer window now holds the shared page in WebView2, with the Spotify overlay. The visuals don't get audio yet (W7a), so the presets move by themselves, and the page has no settings from the app yet (W7b, W8c): it uses its own defaults. (The preset controls came in W7b, below.)

| File | What it does |
| ---- | ------------ |
| `IdleViz.Core/AppAddresses.cs` | The two addresses the page is served from, the Content-Security-Policy for each page, file types, and which file an address names. Ported from `AppScheme.swift`. |
| `IdleViz.Core/OverlayPayload.cs` | What `window.nowPlaying` gets, and the script that sends it. Ported from `OverlayPayload.swift`. |
| `IdleViz.Core/SpotifyTracker.cs` | Now also keeps `CurrentSince`, when the current track became current. |
| `IdleViz.App/PageView.cs` | WebView2 on the visualizer window: serves the files, blocks everything else, waits for the page's scripts, reloads after a crash, suspends the page while hidden. Ported from `PageView.swift` and `AppSchemeHandler.swift`. |
| `IdleViz.App/OverlayFeed.cs` | Sends the overlay its data. |
| `IdleViz.App.csproj` | Copies `IdleViz/web/` into the app's `web` folder at build time. |

**Addresses.** `https://app.idleviz.invalid/…` is the `web` folder next to the exe, `https://presets.idleviz.invalid/…` is `%APPDATA%\IdleViz\Presets` (`.json` and `.js` only). The app answers both in `WebResourceRequested`; nothing reaches the network. An address is refused if it is for another host, port or scheme, would leave its folder (`..`, encoded or not, `\`, a drive), names a stream (`:`), ends in a dot or space (Windows would drop it and serve another file), passes through a symbolic link or junction below the folder, or names no file. Every response carries `Access-Control-Allow-Origin: *`, and each HTML page its own policy: the Mac's three policies with `idleviz-app:` replaced by the two hosts.

**What the page can't do.** Web messages and host objects are off, so the page has no way to call the app. Every permission request (microphone, camera and the rest) is denied, and so are new windows and downloads. The top frame may only show the app's own pages, and frames only those or `about:blank`. Context menus, zoom, the status bar, browser shortcut keys, autofill and script dialogs are off. DevTools are on in Debug builds only.

**Loading.** The page loads at launch into the hidden window, so the first open shows it at once. After the navigation completes, the app asks every 50 ms (for up to 10 s) whether `nowPlaying`, `setPresetSettings` and `setCustomPresets` exist, and only then sends the overlay its data, as on the Mac. If the renderer crashes or hangs, the page is loaded again; if the whole browser process goes, a new WebView2 is made. (Replacing a page stuck in a plugin's endless loop came with the status checks in W7a, below.)

**While hidden** the page is not visible and is suspended. Measured: the app and its WebView2 processes used 0.2 % of one core over 10 s between opens, and about 410 MB of memory together.

**The overlay** gets `nowPlaying` on every Spotify change and seek, when a cover arrives, when the window opens, and every 5 s while it is open. The position is worked out for the moment of sending.

**Cover on its way** (`artworkPending`). On the Mac this is true while the cover downloads. On Windows the cover comes with the track from the media controls, 20 to 150 ms after it (W3 and W4 logs), so a track without a cover counts as pending for 2 s after it became current, and the page leaves the square empty instead of showing the music note. The app tells the page again when the 2 s are over, so a track that never gets a cover shows the note.

**Chosen here without asking the owner** (they asked for the build to keep going); any of these can be changed:

- The 2 s cover wait above.
- The page runs in WebView2's InPrivate mode, the nearest thing to the Mac's non-persistent web storage: nothing the page or a plugin stores outlives it.
- WebView2 starts with `--autoplay-policy=no-user-gesture-required`, as the Mac sets no media type to need a user action. The page's AudioContext is allowed to start without a click.

**Seen when checking** (Windows 11, WebView2 124, 3440 × 1440): a paused song showed only the progress row, a playing song the cover, title, artist and a moving progress bar. Quitting Spotify with the window open left the overlay up at 0.7 s and gone at 3 s (the page's 1.5 s wait). The installed Release copy opened from `idleviz://open` with the page and a hidden pointer. The page asks for `favicon.ico` on every load and gets a 404; that one isn't logged.

### W6: idle trigger and skip rules

The visualizer now opens by itself after the "Start after idle" time without input, under the same open rules as every trigger.

| File | What it does |
| ---- | ------------ |
| `IdleViz.Core/IdleScheduler.cs` | When the next check is due, and one attempt per idle period. `IdleTimeoutSetting`: key `idleTimeout`, minutes, 5, 10, 15 or 30, 0 for off, default 5. Ported from `IdleScheduler.swift`. |
| `IdleViz.Core/IdleSkipRules.cs` | The Windows skip rules below. |
| `IdleViz.App/IdleWatcher.cs` | Runs the scheduler on a one-shot timer, reads the session, the shell and the sound sessions when it fires. |
| `IdleViz.App/HotkeyWindow.cs` | Now also hears lock, unlock and wake. |
| `SettingsWindow.xaml` | The **Start after idle** row ("Needs a Spotify track"), as on the Mac. |

**Timing.** Idle time is `GetLastInputInfo`, the same clock Windows' own idle timeouts use. There is no polling: each check sets a timer for the moment the timeout could first be reached, plus 0.1 s. After the PC wakes or the session is unlocked, and when the setting changes, it checks again at once. A trigger that fires uses up the idle period, whether it opened or not; only new input starts a new one. A trigger while the window is already open does nothing (before W6 the no-dismiss debug mode closed it on any trigger; now only on a manual one, as on the Mac).

**Skip rules**, idle trigger only, in this order:

1. **Locked:** the lock notice (`WTSRegisterSessionNotification`) said so, or the shell reports `QUNS_NOT_PRESENT` (screen saver, locked, or another user's session in front).
2. **Not at the screen:** a Remote Desktop session, or this session isn't the console session.
3. **Fullscreen or presenting:** `SHQueryUserNotificationState` reports a fullscreen app, a Direct3D exclusive-fullscreen app, or presentation mode. `QUNS_APP` ("a Store app is running") doesn't count, because Spotify from the Microsoft Store is one.
4. **Another app is making sound** (the owner's answer to R3): every active sound session on every output, except Windows' system sounds, is read for about 300 ms (6 peak readings, 50 ms apart). A peak above 0.001 (about −60 dBFS) from any process other than IdleViz and Spotify blocks the open. This covers what the Mac's display-sleep assertions catch, a video or a call in a normal window, which the shell state misses. The sound sessions are only read when rules 1 to 3 pass.

Each skip is logged in category `idle` with its reason ("powershell is playing sound").

**Seen when checking** (timeout set to 1 minute in `settings.json` for the test, Debug build, no input):

| Case | Result |
| --- | --- |
| Spotify paused, nothing else playing | Opened after 60 s idle ("Opened via Idle") |
| Spotify playing | Opened: Spotify's own sound doesn't count |
| A quiet 440 Hz tone (about −35 dBFS) from another process | "Idle, but not opening: powershell is playing sound", logged once; no second attempt in the next 100 s |

Not tried by hand: locking (the script can't unlock again), Remote Desktop, a fullscreen app and presentation mode. They are unit-tested against the shell's and the session's values.

Battery times and waiting for input after the keep-awake limit came with W8a.

### W7a: Spotify's audio and the stuck page

The visuals now move to Spotify's sound, and a page that stops answering is replaced.

| File | What it does |
| ---- | ------------ |
| `IdleViz.Core/AudioAnalyzer.cs`, `AutoGain.cs`, `AudioFrame.cs`, `SampleRing.cs`, `TapHealth.cs` | Ported from the Swift files of the same names: automatic gain, the 64-band spectrum, Butterchurn's byte arrays and the packed frame the page unpacks. Apple's vDSP FFT is replaced by a plain radix-2 FFT, scaled to give the same values; a test checks it against the direct sum. |
| `IdleViz.Core/PageStatus.cs` | Reads `window.idlevizStatus()` as untrusted JSON (depth, counts and text lengths capped), and `PageWatchdog`. Ported from `PageStatus.swift`. |
| `IdleViz.Core/CustomPresetPayload.cs` | What `setCustomPresets` gets. For now only the list of presets that hung the page; the custom folder and plugins fill it in W7c (below). |
| `IdleViz.Core/SpotifyProcess.cs` | Which process to capture: the top of Spotify's process tree. |
| `IdleViz.App/SpotifyCapture.cs` | Process loopback on that tree, on its own thread. Plays the part of `SpotifyAudioTap.swift`. |
| `IdleViz.App/AudioPump.cs` | 60 frames a second while the window is open. Ported from `AudioPump.swift`. |
| `IdleViz.App/PageView.cs` | Sends the frames, asks the page for its status once a second, and replaces it when it stops answering. |

**Capture.** As the W2 spike found: `ActivateAudioInterfaceAsync` on `VAD\Process_Loopback`, "include the process tree", 32-bit float, 48 kHz, stereo, event-driven, 10 ms packets. The target is the `Spotify.exe` whose parent isn't a `Spotify.exe` (the spike used the one with the main window, which a Spotify closed to the tray may not have; not tried); should there be two, the one with more Spotify children. While Spotify isn't running it looks again every 2 s, and once a second it checks the captured process still runs, so a Spotify that quits and starts again is followed (seen: back 4 s after the restart). Packets Windows flags as silent go into the ring as zeros. The capture runs only while the window is open, and starts in about 30 ms. Nothing needs a permission, and no recording indicator appears.

**Frames.** A thread wakes 60 times a second on a high-resolution waitable timer (the ordinary timers tick every 15.6 ms, which can't make 60 even frames; the spike's busy-wait used a core), takes the newest 1024 samples, analyses them and posts the frame's script to the UI thread, which hands it to the page. A frame is dropped rather than queued when two are already waiting on the UI thread or two are already in the page, as on the Mac. The first frame after an open is silence, so the page never starts from the last open's sound. While Spotify says it's playing but 5 s in a row bring only silence, the log says so once: Spotify is playing on another device or is muted in the Windows mixer. (On the Mac the same sign means System Audio Recording isn't allowed; Windows has no such permission.)

**Status checks.** While the window is open the app asks `window.idlevizStatus()` once a second. Each new preset on screen and each newly failed preset is logged. If no answer has come for 3 s, the page is replaced:

1. Every renderer process of IdleViz's own WebView2 environment is ended. A renderer stuck in a loop otherwise keeps a core busy.
2. The WebView is closed and a new one is made and loads the page, then gets the state again.
3. The preset that was on screen goes into the `hung` list sent with `setCustomPresets`. The page shows it as "Stopped responding" and doesn't show it again until IdleViz restarts.

Reloading the old WebView instead does not work: after its renderer was ended, WebView2 124 never reported the crash, and navigating it again crashed the app (an access violation in the runtime). A new WebView avoids both. Seen with `--hang-page`: the page froze, was replaced 4 s later, the new page was ready 0.5 s after that and drew 60 frames a second with audio again.

**Chosen here without asking the owner** (they asked for the build to keep going):

- The target process is found by parent, not by main window, so a Spotify in the tray is captured too.
- The high-resolution timer instead of the spike's busy-wait.
- A frame is dropped when two are waiting on the UI thread, on top of the Mac's two-in-the-page rule.
- The hung list lives in the page view until W7c gives it a home in the preset library (done in W7c).

**Seen when checking** (Debug build, Spotify 1.301 from the Store, idle trigger off for the test):

| Case | Result |
| --- | --- |
| Spotify playing, window open 12 s | 724 frames sent in 12 s, none dropped; the page drew 660 frames in 11 s and got 660 audio frames; gain 5.7 (a low Spotify volume) |
| Spotify paused, a 440 Hz tone from another program | 801 packets captured, all silent; loudest level 0. The visuals hear only Spotify |
| Spotify quit and restarted while open | "Spotify (2588) quit", "Spotify isn't running; sending silence", then "Capturing Spotify (21516)" 4 s later |
| `--hang-page` | Replaced 4 s after the hang, ready 0.5 s later, the frozen preset listed as "Stopped responding" |
| Cost while open, 20 s | The app 6.6 % of one core, its WebView2 processes 26.9 %, as the spike measured |

**Not yet:** the audio delay (W7d), and the "Spotify audio can't be captured" warning in the tray (W8b); a failed capture is only logged for now.

### W7b: preset controls

The Visualizer section of settings now has the preset rows from the brief (section 4.4), and the Favorites and Blocklist dialogs (4.5).

| File | What it does |
| ---- | ------------ |
| `IdleViz.Core/PresetSettings.cs` | Mode, Single preset, Shuffle from, Seconds per preset, Blend time, Favorites and Blocklist: stored under the Mac's key names and values (`visualizerMode: "single"`, `favoritePresets: [...]`) and sent with `setPresetSettings`. Ported from `PresetSettings.swift`, with the same fallbacks for stored values that aren't a choice. |
| `IdleViz.Core/PresetInfo.cs` | Reads the page's `idlevizPresets()` reply as untrusted JSON, as the Mac does. |
| `IdleViz.Core/PresetList.cs` | What the Favorites and Blocklist dialogs share: titles, empty text, adding and removing, search. |
| `IdleViz.Core/SettingsStore.cs` | Numbers with a fraction (the blend time) and lists of strings. |
| `IdleViz.App/PresetController.cs` | Keeps the controls, saves and sends each change, and remembers the last preset on screen (`lastShownPreset`). Ported from `PresetController.swift`. |
| `IdleViz.App/PageView.cs` | Sends the controls when the page is ready and on every change, asks for the preset list once the page is ready, and reports each new preset on screen. |
| `IdleViz.App/SettingsWindow.xaml` | The rows: Mode; Shuffle from, Seconds per preset and Blend time in Shuffle; Visualizer in Single; Last shown with the heart and block toggle buttons; Favorites and Blocklist with "Manage (n)". |
| `IdleViz.App/PresetListDialog.xaml` | The Favorites and Blocklist dialog: a `ContentDialog` with "On the list (n)" and "All presets" in a `SelectorBar`, a search box on the second, and Add or Remove on each row. |

The page loads when the app starts, so the preset list is there before the first open, as on the Mac.

**Chosen here without asking the owner** (they asked for the build to keep going):

- The heart and block buttons are toggle buttons, so the accent fill shows when they're on. The heart also turns solid, as on the Mac; the block glyph has no solid form in Segoe Fluent Icons.
- In "All presets", Add or Remove changes the row in place, so the list keeps its scroll position. In "On the list", a removed row goes away at once.
- The Visualizer picker is 200 px wide, so long preset names are cut short with "…" rather than wrapping the row.

**Seen when checking** (Debug build, settings driven through UI Automation, idle trigger off for the test):

| Case | Result |
| --- | --- |
| Seconds per preset set to 15 while closed, then opened | Presets changed exactly 15 s apart (3 changes in 40 s) |
| Mode switched to Single while the visualizer was open | The page switched to the Single preset within a second |
| Heart next to Last shown | Saved to `favoritePresets`, the heart filled |
| Favorites dialog, All presets, search "airhandler", Add | 6 matches shown; the first one added |
| Blocklist dialog, adding a favorite | It moved from the favorites to the blocklist |
| Screenshots of both modes and the dialogs | As in the brief's sections 4.4 and 4.5 |

**Found while checking, not fixed here:** with `--show-settings`, the settings window opening at the same moment as the app crashed it in about 4 starts out of 5 (an access violation in the XAML runtime). Main does the same, so it predates W7b; it's flagged as a separate fix. Opening settings once the page is ready works every time.

**Not tried:** picking a different preset in the Visualizer picker, and **Shuffle from** Custom (there are no custom presets until W7c). The like and skip keys are W8c.

### W7c: your own presets and plugins

The custom presets folder, `%APPDATA%\IdleViz\Presets\`, is read, watched and handed to the page, with the Presets section of settings (brief section 4.4).

| File | What it does |
| ---- | ------------ |
| `IdleViz.Core/CustomPresets.cs` | Scanning the folder (`.json`, `.js`, `.milk`, subfolders, at most 5,000 files, hidden files and folders and links skipped, sorted as File Explorer sorts), plugin names from `export const meta`, import names ("Tunnel (2).milk"), and the `.milk` checks: the cache key (SHA-256 of the converter version and the file, the same key the Mac computes), the "is this a Milkdrop preset" test, and the check of what the converter returns. Ported from `CustomPresets.swift`. |
| `IdleViz.Core/CustomPresetPayload.cs` | The entries for the page: a file served from the presets address, a `.milk` file from its cached conversion, a bundled plugin from `web\visuals`. |
| `IdleViz.App/PresetLibrary.cs` | Reads the folder off the UI thread, watches it with `FileSystemWatcher` (300 ms settle time, the cache's own writes ignored, a full rescan if Windows drops events), converts new `.milk` files one at a time and caches them in `Presets\.cache\` (a hidden folder), deletes stale cache files, keeps the hung list by file version, and does Import, Open, Reveal. Ported from `PresetLibrary.swift`. |
| `IdleViz.App/MilkConverter.cs` | Converts one `.milk` file in a hidden WebView on `converter.html`, given up after 10 s. Ported from `MilkConverter.swift`. |
| `IdleViz.App/WebServer.cs` | The request handler, moved out of `PageView` so the converter page is served the same way (without the presets address). |
| `IdleViz.App/PresetController.cs`, `SettingsWindow.xaml` | The Presets section: the trust warning, Import…, Folder with the counts, Rescan, and Failed to load with Reveal, hidden when nothing failed. |

**The converter page** has its own WebView2 profile (`converter`), so it gets a renderer process of its own: a file that hangs the converter can't stall the visuals. WebView2's `ExecuteScriptAsync` doesn't wait for a promise, so the page stores the result of `convertMilk` and the app asks for it every 50 ms, up to 10 s.

**A crash found on the way.** The page's `CoreWebView2` was only a local in `CreateController`. Its event handlers live on that wrapper, so when a garbage collection freed it, WebView2 called a freed handler (an access violation in the runtime), or the handlers silently stopped and the page never finished loading. This was the cause of the "settings at launch" crash flagged in W7b. It came and went with how much the app allocated while starting: W7c's start-up work made it happen in most starts. Both WebViews now keep their `CoreWebView2` for as long as their controller. Found here and in a separate session at the same time; the `PageView` half is that session's PR (#41), and the converter's is here.

**Chosen here without asking the owner** (they asked for the build to keep going):

- **Import…** is a button with a small menu, **Files…** or **Folder…**: a Windows picker chooses files or a folder, not both, which the Mac's panel can.
- The conversion cache folder is marked hidden, as a leading dot hides it on the Mac.
- A failed file's row shows its path in the folder ("pack/not-a-preset.milk"), as the Mac's does, since a file that never loaded has no name from the page.

**Seen when checking** (Debug build, settings driven through UI Automation, idle trigger off for the test):

| Case | Result |
| --- | --- |
| Folder with a plugin, a plugin that throws, one that hangs, a `.milk` file and a text file that isn't one | The `.milk` file converted in under a second and was cached in the hidden `.cache` folder; the other `.milk` file was listed as "Not a Milkdrop preset"; the page listed 400 presets (395 bundled, the Aurora plugin and 4 custom) |
| A `.json` copied in while running | Picked up within a second: 401 presets |
| Single preset set to the Pulse plugin, opened | The plugin drew 300 frames in 5 s, with audio |
| Single preset set to the plugin that loops forever | The page was replaced twice, 10 s in all: the first freeze came before the page had named its preset, so only the second replacement could mark the plugin. It was then listed under Failed to load as "Stopped responding", and the next page went on with a bundled preset at 60 fps |
| Settings, Presets section | As in the brief: the warning, Import…, "396 bundled, 5 custom", Rescan, Failed to load with Reveal |
| 8 starts in a row after the `CoreWebView2` fix | All 8 reached Ready; before it, 0 of 8 did and 3 crashed |
| Reveal on a failed file | A File Explorer window opened (whether the file was selected in it couldn't be read by script) |
| Import…, Files… | The Windows file picker opened; the copying itself is covered by unit tests (a taken name gets a number, a folder is copied whole, nothing is replaced) |

**Not tried:** choosing a file in the picker by hand and Import of a folder through the picker (the script couldn't drive the Windows dialog), a `.milk` file that hangs the converter (none at hand), a plugin that throws while in Shuffle, and a pack of hundreds of files.

### W7d: audio delay

The visuals can now wait for the speakers: an **Audio delay** per output device, **Detect delay** with a microphone, and the **Manual delay test** (brief sections 4.4, 4.5 and 6.8).

| File | What it does |
| ---- | ------------ |
| `IdleViz.Core/AudioDelay.cs` | `AudioDelaySetting` (key `audioDelayByDevice`, 0 to 2.5 s in 10 ms steps, the label, the `setAudioDelay` call) and `DelayLine`, which holds analysed frames back. Ported from `AudioDelay.swift`. |
| `IdleViz.Core/DelayDetector.cs` | The measurement: the softened phase transform over 300 Hz to 6 kHz, lags from −0.1 to 2.5 s, and the two tests a peak must pass. Ported with its tests; a plain radix-2 FFT stands in for vDSP. |
| `IdleViz.Core/BeepTest.cs` | The manual test's timing and its beep. Ported from `BeepTest.swift`. |
| `IdleViz.Core/Microphones.cs` | Which microphone Detect delay uses (the owner's rule, below). |
| `IdleViz.Core/SampleRing.cs`, `SettingsStore.cs` | Recording the capture for Detect delay; maps of numbers in the settings file. |
| `IdleViz.App/AudioDevices.cs` | The default output device with its name and reported latency, the list of microphones, and the notice when the default output changes. |
| `IdleViz.App/MicrophoneRecorder.cs` | Records one microphone into memory for a few seconds. |
| `IdleViz.App/BeepTestPlayer.cs` | Plays the test's beeps as one unbroken stream. |
| `IdleViz.App/AudioDelayController.cs` | Loads and saves the delay per device, follows the default device, runs Detect delay and the manual test. Ported from `AudioDelayController`. |
| `IdleViz.App/AudioPump.cs`, `SpotifyCapture.cs` | Frames go through the delay line. The capture now counts its users, so Detect delay can run it while the window is closed. |
| `IdleViz.App/SettingsWindow.xaml`, `ManualDelayDialog.xaml` | The rows and the dialog. |

**The delay.** Every analysed frame waits in a buffer and goes to the page `delay` after it was built. The page and plugins don't know; the page only gets `setAudioDelay`, for the progress bar. The buffer starts empty with every open. The value is saved per output device under its Windows endpoint ID, and switches when the default output device changes (`IMMNotificationClient`). A device with no saved value starts at the stream latency Windows reports for it; that was 0 for the two devices seen here, so in practice a new device starts at 0 until it is measured.

**One difference from the Mac's delay line.** The Mac's `DelayLine.pop` returns the newest due frame and drops the rest. With a delay that is a whole number of frames (every 50 ms is, at 60 frames a second), each frame comes due right at a timer tick, and the tick's jitter decides whether it gets none or the next gets two. Measured here: at 1 s and at 2 s delay only about 45 frames a second reached the page. The Windows version hands out one due frame per call, in order, and only skips ahead when more than two are due. After that: 549 frames in 10 s at a 1 s delay (expected 546), 603 at 100 ms. The Mac version was not changed or measured.

**Detect delay.** Only while Spotify says it's playing. For about 5 s the Spotify capture and one microphone are recorded into memory, then compared off the UI thread (about 0.1 s). The capture's side is stamped when a packet is handed to the app, the microphone's side with the time Windows gives for its first packet, both on the performance counter. A clear result is saved, also when it equals the old value; an unclear one keeps the old value and says so. The microphone is opened only for those 5 s, and nothing is written to disk.

**Which microphone** (the owner's rule): the one picked in the **Microphone** row; else the PC's own; else the default input. A Bluetooth microphone is refused with the reason, picked or not, because recording from a headset's own microphone switches it to call mode and changes the delay being measured. How a device is connected is read from its driver family (`PKEY_Device_EnumeratorName`): `BTH…` is Bluetooth, `USB` is USB, `ROOT` and `SWD` are software-only, anything else counts as the PC's own.

**Microphone privacy.** Windows has one switch for all desktop apps and no prompt. If it is off, the Detect delay row shows a link to the microphone privacy page. This never turns the tray icon yellow.

**Manual delay test.** A dialog on the settings window. One stream of silence with a 60 ms beep every second, every fourth an octave higher. The stream's first sample is placed on the performance counter from how much was still waiting in the device's buffer at the first refill, and the panel is redrawn every frame from the same clock, lit for 120 ms starting `delay` after each beep. Spotify is paused if it was playing and resumed afterwards, through the media controls (`TryPauseAsync`, `TryPlayAsync`); this is the only place the app controls playback. Done saves the delay for the device even if it wasn't moved. If the default output device changes during the test, the beeps start over on the new one.

**Chosen here without asking the owner** (they asked for the build to keep going):

- The **Microphone** row sits under Detect delay and offers "Automatic" and every active microphone. The stored key is `detectDelayMicrophone`. A picked microphone that is unplugged counts as Automatic.
- A picked Bluetooth microphone is refused like a default one.
- A microphone that delivers exact silence (a streaming or VR driver's) gets its own hint, "… heard nothing at all. Pick another microphone."
- The delay line difference above.
- `--detect-delay`, as the Mac has `-IdleVizDetectDelay`.

**Seen when checking** (Debug build, settings driven through UI Automation; output: a monitor's speakers over HDMI; microphone: a USB one about 50 cm from them):

| Case | Result |
| --- | --- |
| Start | "Output device: Odyssey G85SB (NVIDIA High Definition Audio), delay 0 ms (reported by Windows)" |
| Detect, Automatic | The default input is a VR driver's microphone, which delivered silence: "… heard nothing at all. Pick another microphone. Kept 70 ms." |
| Detect, USB microphone, three runs | Lags of 68, 58 and 67 ms, saved as 70, 60 and 70 ms. The peak stood 54 to 64 standard deviations above the rest and 5.5 to 6.5 times the runner-up (10 and 1.5 are needed) |
| Detect with Spotify paused | "Play something in Spotify first, out loud"; the microphone wasn't opened |
| Installed Release copy | Started with "delay 70 ms (saved)"; the rows, the paused hint and the manual test dialog worked as in the Debug build |
| Manual delay test | Spotify paused when it started and resumed on Done. The panel lit once a second for about 110 ms, white three times and orange the fourth. **+10 ms** twice changed 70 to 90 ms, and Done saved it |
| 10 s open at a 1 s delay | 549 frames reached the page, none dropped by the page |

**Not tried:** whether the flash and the beep line up by ear (it needs ears; with the value Detect found, they should at about 70 ms), a Bluetooth output or microphone, a PC with a built-in microphone, the microphone privacy switch turned off, and changing the output device during the test or with the window open.

### W8a: keep-awake limit, battery times, closing on sleep and display changes

The open visualizer now keeps the display awake, up to a limit, and the idle and keep-awake times can differ on battery.

| File | What it does |
| ---- | ------------ |
| `IdleViz.Core/Timing.cs` | `KeepAwakeSetting` (key `keepAwakeLimit`, minutes, 30, 60, 120 or 240, default 60), the battery keys, `PowerStatus` (does this PC have a battery, is it running on it) and `TimingSettings`, which picks the times that apply. Ported from `Timing.swift`. |
| `IdleViz.Core/DisplayLayout.cs` | The list of displays, and `DisplayTracker`, which says whether it changed. |
| `IdleViz.App/KeepAwake.cs` | The power request and the limit timer. Ported from `KeepAwake.swift`. |
| `IdleViz.App/PowerSource.cs` | Battery or mains, and the notice when that changes. |
| `IdleViz.App/Displays.cs` | Reads each display's position, size and scale. |
| `IdleViz.App/HotkeyWindow.cs` | Now also hears the sleep notice, power status changes and display changes. |
| `IdleViz.App/VisualizerController.cs` | `Closing`, raised when the fade-out starts. |
| `SettingsWindow.xaml` | **Keep screen awake** ("Then the PC sleeps as usual"), **Different times on battery** and its two rows. |

**Keeping awake.** While the window is open the app holds a display request (`PowerCreateRequest` and `PowerSetRequest` with `PowerRequestDisplayRequired`, reason "IdleViz visualizer", which `powercfg /requests` shows). It stops the display turning off, and with it the screen saver and the lock that would follow. The request is dropped when the fade-out starts, not when it ends.

**The limit** counts from when the window opened. When it is reached the window closes with the slow fade (1.5 s, `CloseReason.KeepAwakeLimit`), Windows' own timeouts take over, and the idle trigger waits for new input, so it doesn't open again on its own. A changed setting or power source applies to the open window: the new limit still counts from the opening, so a shorter one may close it at once.

**Battery times.** With **Different times on battery** on, `idleTimeoutBattery` and `keepAwakeLimitBattery` replace the two times while the PC runs on its battery. The first time the switch is turned on, both start as copies of the plugged-in values. Windows sends `PBT_APMPOWERSTATUSCHANGE` on plugging in and unplugging (and on every change of charge, which is ignored), so nothing polls. The three rows are hidden on a PC without a battery. A battery only counts on a PC Windows calls a laptop or a tablet (`PowerDeterminePlatformRoleEx`): a desktop on a UPS also reports a battery, but running on it is a power cut, not an unplugged laptop.

**Sleep and display changes** close the window at once, without a fade, because the screen it is on may be off or gone before a fade ends: `PBT_APMSUSPEND`, and `WM_DISPLAYCHANGE` or `WM_SETTINGCHANGE` when the list of displays (position, size, scale) differs from the last one seen. Windows sends both notices for much else, a new wallpaper or a moved taskbar, so the list is compared each time.

**Chosen here without asking the owner** (they asked for the build to keep going):

- The UPS rule above.
- A stored time that isn't one of the choices (a hand-edited settings file) is shown as its own entry in the picker instead of being replaced.
- A stored limit of 0 or less counts as the default, 1 hour.
- `--pretend-battery` (Debug builds), since the test PC has no battery.

**Seen when checking** (Debug build, no input; whether the display is required was read from the system's execution state every 5 s):

| Case | Result |
| --- | --- |
| Start | "Displays: 3440×1440 at 0,0 (100 %), 1920×1080 at -1920,357 (100 %)" |
| Idle timeout and limit both set to 1 min in `settings.json` | Opened via Idle. The display was required from then on. 60.0 s later: "Keep-awake limit of 1 min reached", "Closing: KeepAwakeLimit", closed 1.5 s after that, and the display was no longer required. It did not open again in the next 2 minutes without input |
| `--pretend-battery` | The battery switch appeared. Turning it on showed the two battery rows as copies (Off, 1 hour) and stored them. Picking 30 min stored `keepAwakeLimitBattery` 30 |
| No battery (this PC) | None of the three rows |
| `WM_SETTINGCHANGE` and `WM_DISPLAYCHANGE` sent to the app with the displays unchanged | Stayed open |
| The sleep notice (`PBT_APMSUSPEND`) sent to the app | "The PC is going to sleep", "Closing: DisplayChanged", closed 30 ms later |

**Not tried:** a real battery (unplugging with the window open, the role Windows reports on a laptop), a real sleep, and a real display change (unplugging a monitor, changing the resolution or scale). The rules behind them are unit-tested. The limit changing while the window is open was not tried either: the pickers have no time short enough to wait for.

### W8b: warnings

While something is wrong, the tray icon is yellow and the flyout has one row per problem (brief section 6.10, decision J4).

| File | What it does |
| ---- | ------------ |
| `IdleViz.Core/Warnings.cs` | `WarningKind`, `Warning` (title, explanation, error text) and `WarningList`, which holds what is showing and announces changes once. |
| `IdleViz.App/SpotifyCapture.cs` | Reports when the capture couldn't start and when it did. `Probe` asks Windows for a capture of Spotify without starting it. |
| `IdleViz.App/SpotifyInfo.cs` | Reports whether the media controls could be reached. `Recheck` tries again. |
| `IdleViz.App/TrayIcon.cs`, `TrayGlyph.cs` | The glyph in yellow (`#F5B800`) and the tooltip "IdleViz, something needs attention" while a warning shows. The refusal flash keeps the colour. |
| `IdleViz.App/FlyoutWindow.xaml` | A row per warning: a yellow warning glyph, the title, "Show details ›". |
| `IdleViz.App/SettingsWindow.xaml.cs` | The details dialog. |

**The two problems** (J4):

- **Spotify audio can't be captured:** Spotify is running, and Windows refused the process-loopback capture or didn't answer within 5 s.
- **Can't read what Spotify is playing:** the media controls couldn't be requested, their session list couldn't be read, or reading Spotify's session failed.

Spotify not running is neither. A failure while Spotify is quitting isn't one either: after a failed capture the process is looked for again, and a failed reading only counts if the session is still the current one. Silence while Spotify says "playing" is not a warning (Spotify Connect and a muted Spotify look the same); the log's "packets captured, silent" line records it.

**When they are checked:** when the flyout opens, when Spotify's session appears, and when the visualizer opens. Opening the visualizer starts the real capture, which reports for itself. The other two use `Probe`, on a thread-pool thread, so no audio is captured for a check. A warning clears as soon as its check passes; the flyout adds or removes the row while it is open.

**A click on a row** closes the flyout, opens the settings window and shows a dialog on it: the title, one sentence on what it means, the error text (selectable), **Open log folder** and **Close**.

**Chosen here without asking the owner** (they asked for the build to keep going):

- The details dialog sits on the settings window, like every other dialog of the app, so a click on a row opens settings too.
- The check for the capture is a probe that sets a capture up and drops it, not a short real capture.
- `--pretend-warning` (Debug builds); the brief lists "fake a warning state" among the debug switches.

**Seen when checking** (Debug build, driven through UI Automation):

| Case | Result |
| --- | --- |
| `--pretend-warning` | Both rows in the flyout, in the order above, each with the yellow glyph and "Show details ›"; the flyout was 195 px high instead of 89 |
| A click on the first row | The flyout closed, settings opened with the dialog "Spotify audio can't be captured", the explanation, the pretend error text, **Open log folder** and **Close** |
| Normal start, Spotify running | No rows, nothing logged under `warning`; opening the visualizer captured as before |

**Not tried:** a real failure of either kind, since neither could be caused on the test PC; the yellow icon and the tooltip in the tray itself (the script can't see the icon; it is drawn by the same code as the white one); **Open log folder**.

### W8c: brightness, overlay switch, run at startup, like and skip keys

The last step: the remaining settings rows and the two keys that work while the visualizer is open (brief sections 6.3 and 6.11).

| File | What it does |
| ---- | ------------ |
| `IdleViz.Core/DisplaySettings.cs` | `BrightnessSetting` (key `visualizerBrightness`, 0.5 to 1, default 0.7) and `OverlaySetting` (key `showOverlay`), with their calls to the page. Ported from `DisplaySettings.swift`. |
| `IdleViz.Core/VisualizerKeys.cs` | The like and skip keys (`likeKey`, `skipKey`, −1 for Off), the keys the pickers offer, and the page calls. Ported from `VisualizerKeys.swift`. |
| `IdleViz.Core/Startup.cs` | `StartupEntry`: what the Run key and Windows' own switch mean together. |
| `IdleViz.App/RunAtStartup.cs` | Reads and writes the entry in the registry. |
| `IdleViz.App/DismissWatcher.cs` | The keyboard hook now knows the two keys: they don't close the window, are swallowed, and are reported. |
| `IdleViz.App/PresetController.cs`, `PageView.cs` | `Perform` runs a key; the page gets `skipPreset`, `showLike`, `setBrightness` and `setOverlayEnabled`, the last two again after every load. |
| `SettingsWindow.xaml` | **Like key**, **Skip key** and **Run at startup** in General; **Show Spotify overlay** and **Brightness** at the top of Visualizer. |
| `installer/IdleViz.iss` | Uninstalling removes the startup entry. |

**Brightness and the overlay switch** are sent to the page when it is ready and whenever the setting changes, so they apply while the visualizer is open. The page does the rest, as on the Mac: a black layer over the visualizer, and an overlay that is hidden as a whole.

**Like and skip keys.** They are read when the window opens and handed to the dismiss rules as keys that don't close it. The low-level hook sees them whether or not the window has focus (J3), so they are swallowed and never type into the app behind. A fresh press runs the action; key repeat doesn't. Skip calls `skipPreset()`. Like asks the page which preset is on screen at that moment (`idlevizStatus()`), toggles it on the favorites and calls `showLike`. A modifier is a key of its own and closes the window, so Ctrl+L closes it. A like or skip key that was already held when the window opened belongs to the app behind: it does nothing, and its repeats and release are passed on. The keys are stored as Windows virtual-key codes, so the numbers differ from the Mac's for the same key.

**Run at startup** is a value named `IdleViz` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` holding this copy's path in quotes. It is never kept in the settings file: the row is read from the registry when the settings window opens and every time it comes to the front. Windows keeps its own on/off switch for each entry (Settings → Apps → Startup, stored under `Explorer\StartupApproved\Run`; an odd first byte means off). If that is off, the row shows Off with a link "Turned off in Windows. Open Startup apps…", and the app leaves Windows' switch alone. An entry that points at another copy (a Debug build) counts as Off for this one.

**Chosen here without asking the owner** (they asked for the build to keep going):

- The Run key instead of a shortcut in the Startup folder or a scheduled task. It needs no admin rights and shows up in Windows' Startup apps list.
- Windows' own startup switch is only read, never changed, even when the row is turned on.
- The like and skip keys are swallowed while the window is open.
- The keys also work during the 0.4 s after opening.

**A fault found while checking:** changing a picker's list of items from inside its own selection event took the app down ("Catastrophic failure" in the XAML library). The like and skip pickers each leave out the other's key, so each choice rebuilt both lists. Now only the other picker's list is rebuilt. And a slider reports a change as soon as its range is set, while the window is still being built; that is now ignored.

**Seen when checking** (Debug build, settings driven through UI Automation, keys injected):

| Case | Result |
| --- | --- |
| Like key picker | Off, A to Z without N, the digits, arrows and Space. Picking F stored `likeKey` 70, and the Skip picker then listed N but not F |
| Brightness slider to its lowest | "50%", `visualizerBrightness` 0.5; the open visualizer was visibly darker |
| Show Spotify overlay off | `showOverlay` false; a paused song showed no progress row |
| Run at startup on, then off | The Run value held the Debug copy's path in quotes, then was gone |
| Windows' switch set to off, then the row turned on | The row stayed Off with the link to Startup apps |
| Skip key (set to B) with the visualizer open | A new preset the same moment; the window stayed open |
| Like key (set to F) twice | "Liked …" and the preset in `favoritePresets`, then "Unliked …"; the window stayed open |
| Another key (A) | "Closed by key 0x41" |

**Not tried:** the heart on screen (it shows for 1.6 s; the script only saw the log and the settings file), a real sign-in with the row on, the installed copy's uninstaller removing the entry, and a key held across the opening.

### W9: more than one display

Not a step of the brief: the owner asked for it after W8c. The Mac app still uses one display.

| File | What it does |
| ---- | ------------ |
| `IdleViz.Core/MultiDisplay.cs` | `MultiDisplaySettings` (the six stored values), `DisplayPlan.For(settings, displays)`: which windows to show, where, which displays each covers, where the overlay goes and whether input closes it. `DisplayLabel` names displays in settings. |
| `IdleViz.Core/PresetSettings.cs` | `FollowScript(preset)`: the preset controls held on one preset, for a page that follows the main page. |
| `IdleViz.App/VisualizerController.cs` | Keeps one window and page per mirrored display (`Prepare`), shows, fades and hides them together, and remakes the other pages when one ended every renderer. |
| `IdleViz.App/PageView.cs` | A main page forwards what it is sent to its mirrors, keeps them on its preset (`SyncMirrors`), and sends each window's layout (`SendLayout`). |
| `IdleViz.App/VisualizerWindow.cs` | `Show(PlannedWindow)` instead of always the primary display. |
| `IdleViz/web/overlay.js`, `overlay-state.js`, `overlay.css`, `index.html` | The overlay lives in a `.region`, one per display the window covers; `setOverlayRegions` places them. The overlay's elements are found by class, not id, so a region can be copied. |
| `IdleViz/web/visualizer.js`, `visualizer-state.js` | `setRenderWidthCap` raises the 2560 px render cap for a window that spans displays. |
| `SettingsWindow.xaml` | A **Displays** section with the six rows and the GPU warning. |

**Settings** (stored with these keys; displays are stored by the name Windows gives them, such as `\\.\DISPLAY2`):

| Row | Key | Default |
| --- | --- | --- |
| Main display | `mainDisplay` | not set: the display Windows calls primary |
| Use more than one display | `multiDisplay` | off |
| Other displays | `multiDisplayOthers` | not set: every other display, including ones plugged in later |
| Placement | `multiDisplayPlacement` (`mirror` or `extend`) | `mirror` |
| Close on input | `multiDisplayCloseOnInput` | on |
| Spotify overlay on | `overlayDisplay` (`main`, `all` or a display's name) | `main` |

A stored display that isn't connected falls back: the main display to the primary one, the overlay to the main display. The same goes for an overlay display that isn't covered. With the switch off only the main display is covered, the overlay is on it, and input always closes the visualizer.

**Same on each display** (the owner chose a render per display over copies of one picture). Each further display gets its own window and its own page, made ahead of time and kept hidden like the main one, so opening stays instant. The main page is the only one that picks presets. The others are sent the same preset controls but in Single mode, held on the preset the main page shows; the app asks the main page for its preset every 250 ms while open and passes a change on, and the following page blends to it with the blend time. So the same preset runs on every display, up to a quarter of a second apart, but the pictures are not identical: Milkdrop presets use random numbers, and each page has its own. Audio frames, the Spotify item, the delay, brightness and the overlay switch go to every page. Only the main page reports the preset list, failures and the last shown preset.

**Extend across displays** (the owner chose a true span, cropped). The main window covers the rectangle that encloses the covered displays, with the one page in it. Displays of different sizes or offsets leave parts of that rectangle that no display shows; that part of the picture is lost. The page is told the render width cap, 2560 px times the span's width over the main display's width (at most 7680), so the main display's part is as sharp as when it is covered alone (the owner's choice). On the owner's desk: a 5360 px span, cap 3989.

**The overlay** is laid out per display. The app sends the window's size and each display's rectangle inside it, in device pixels, with a flag for whether the overlay shows there; the page scales by its own width, so the scale of the display doesn't matter. Each region clips its own overlay, has its own heart for the like key, and scales its 1920 px stage to that display's width. A window that covers one display, and the Mac app, which never sends a layout, have one region: the whole window.

**Close on input off** (the owner chose "nothing closes it" and "like and skip keys off"). No hooks are installed, so nothing is swallowed and the like and skip keys type as usual; the window doesn't take focus and the pointer stays visible. A second manual trigger (hotkey, tray menu, URL, **Open now**) closes it, as does the keep-awake limit, sleep or a display change. It only counts while **Use more than one display** is on (the owner's choice).

**A stuck page with mirrors.** Replacing a stuck page ends every renderer in the WebView2 environment, which now includes the other displays' pages. The page that did it tells the controller before anything is awaited, and the other pages drop their web views and make new ones, with their watchdogs started over. WebView2's later reports about the ended renderers then refer to web views nobody holds, and are ignored.

**Chosen here without asking the owner:**

- The section is called **Displays** and sits between Visualizer and Presets; the placement choices are worded "Same on each display" and "Extend across displays"; the default placement is the first.
- The warning is an InfoBar at the top of the section, shown only while the switch is on: "Using more than one display takes more GPU power. The visualizer may run less smoothly, at a lower frame rate."
- Displays are named by their Windows number and size ("Display 2 (3440 × 1440)"), not by the monitor's model name.
- Display settings changed while the visualizer is open apply at the next open. A display change still closes it at once, as before.
- A following page blends with the blend time even after the skip key, which blends the main page in half a second.

**Seen when checking** (Debug build on the owner's PC: a 3440 × 1440 primary display, `DISPLAY2`, and a 1920 × 1080 one to its left, `DISPLAY1`; settings written to the file or driven through UI Automation; windows captured from the screen):

| Case | Result |
| --- | --- |
| Switch on, mirror, overlay on all | Two windows, 3440 × 1440 at 0,0 and 1920 × 1080 at −1920,357; both pages logged the same preset; each showed the progress row at its own bottom edge |
| Extend, overlay on all | One window, 5360 × 1440 at −1920,0; one picture across both; a progress row on each display's part, the small display's at its own bottom edge |
| Main display `DISPLAY2`, overlay on `DISPLAY1` | The progress row only on the 1920 × 1080 display |
| Close on input off, mouse moved | "Opened … input is ignored"; still open after the moves; a second `idleviz://open` closed it |
| Close on input on, two displays, mouse moved | "Closed by mouse move"; both windows gone |
| `--hang-page` with two displays mirrored | The main page was replaced after 2 s, "Ended 2 renderer process(es)", both pages Ready again 0.6 s later on the same new preset, both windows still up |
| Settings window | The Displays section; with the switch off the four rows below it are greyed out and there is no warning; switched on through UI Automation, the warning showed and `multiDisplay` true was stored |

**Not tried:** the pickers and the Other displays menu by hand (only the switch was driven), three or more displays, displays at different scales, a display unplugged while several are covered, the like heart on a second display, the installed Release copy, and how much the frame rate actually drops (nothing measured it).
