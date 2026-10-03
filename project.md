# Idle Visualizer + Spotify Overlay (macOS)

A menu-bar app that opens a fullscreen music visualizer with a Spotify now-playing overlay when the Mac goes idle (or on command), and closes on any input. Not a real screensaver, just a fullscreen window on top of everything.

## Behavior summary

- **Opens** after N idle minutes, or from a global hotkey / terminal command, but only if Spotify is running and has a current track (playing or paused). Otherwise it doesn't open, and a manual trigger flashes the menu-bar icon.
- **Doesn't open on idle** while the screen is locked, or while another app is keeping the display awake (a video, a call, a presentation). Manual triggers skip that second check, since you're clearly there.
- **Closes** on any input (mouse/trackpad movement, click, scroll, key, gesture), as sensitive as a macOS screensaver. If Spotify quits or the track disappears while it's open, the visualizer stays and only the overlay fades out; it comes back when a track does.
- **Fades.** It fades in over about 0.6 s. Input fades it out quickly (about 0.25 s), like the macOS screensaver. The keep-awake limit fades it out slowly (about 1.5 s).
- **Stays awake, up to a limit.** While it's showing, the display doesn't sleep. After the keep-awake limit (default 1 hour, adjustable in settings) it fades out and the Mac goes back to its normal sleep, screensaver and lock schedule.
- **Works on battery too.** Optionally, the idle timeout and keep-awake limit can be set differently for when the Mac is on battery.
- **Audio delay.** With Bluetooth or AirPlay speakers the sound arrives late, so the visuals can be delayed to match. Set it by hand, or press **Detect delay** and the app measures it with the microphone for a few seconds. It's saved for each speaker or pair of headphones.
- **Missing permissions show up in the menu bar.** The menu-bar icon turns yellow, and the popup lists what's missing below the "Open visualizer" row. Clicking an error opens the right System Settings page.
- **Overlay** is Spotify-only and looks like the Spotify TV app's now-playing screen, with the visualizer as the background. It can be turned off in settings, leaving only the visualizer. Font: Figtree.
- **Visualizer** reacts to Spotify's audio and runs at 70% brightness (adjustable). It is built in: Butterchurn (WebGL Milkdrop) with a bundled preset library plus your own presets imported from a folder.
- **Main display only** for now.

| Spotify state                 | Opens? | Overlay                                   |
|-------------------------------|--------|-------------------------------------------|
| Not running / no track        | No     | n/a                                       |
| Song, playing                 | Yes    | Full TV-style layout                      |
| Song, paused                  | Yes    | Progress bar only, frozen in place        |
| Podcast (playing or paused)   | Yes    | None                                      |
| Ad between songs              | Yes    | Minimal: "Advertisement" label + progress bar |
| Ad in or between podcasts     | Yes    | None                                      |
| Playing on another device (Spotify Connect) | Yes | Same as the rows above. The visuals get silence and drift slowly. |

**Non-goals:** other players (Apple Music, browsers), Spotify's Web API (no login/OAuth/Premium dependency), a `.saver` bundle, third-party visualizer apps (Synesthesia etc.), multi-display (see "Later"), and distribution to other people. The app is for personal use: no notarization, Developer ID or auto-updates. If that ever changes, the app name must not contain "Spotify" (Spotify's brand rules).

---

## Build and signing

- **Minimum macOS 26**, for both the app and the package (`platforms: [.macOS(.v26)]`).
- **Xcode project + Swift package.** `IdleViz.xcodeproj` holds the app target: the `.app` bundle, `Info.plist`, entitlements, the `idleviz://` URL scheme and signing. All logic that can be tested without a screen lives in a local Swift package, `IdleVizCore` (`Package.swift` at the repo root), which the app target depends on. The app target stays a thin AppKit/SwiftUI layer. Building needs full Xcode, not just the Command Line Tools.
- **Testability.** Put anything that touches the system behind a small protocol (AppleScript runner, idle-time source, clock, power assertions, Spotify running state) so `IdleVizCore` logic runs against fakes in `swift test`. CI can't test permissions, Spotify or audio, so those are checked by hand and written up in each PR's Verification section.
- **CI.** `swift test` for the package, `xcodebuild build` for the app with `CODE_SIGNING_ALLOWED=NO`, and SwiftLint. It runs on the `macos-26` runner.
- **Signing.** Sign every local build with the same free Apple Development certificate (Xcode's personal team). macOS ties the Automation and System Audio Recording permissions to the signature, so ad-hoc or changing signatures make the prompts come back or silently return all-zero audio.
- **Hardened Runtime** on, with the `com.apple.security.automation.apple-events` entitlement, and `com.apple.security.device.audio-input` for the delay detector's microphone use.
- **Not sandboxed.** The sandbox would need a temporary-exception entitlement for Apple Events to Spotify and would move the presets folder into a container.
- **Install** by copying the built app to `/Applications`. Launch at login (`SMAppService`) works best from there. The `.app` bundle is the whole app, so there is no installer. `IdleViz.command` at the repo root does the build, the copy and the launch in one step.

---

## Permissions and first launch

The app needs two permissions: **Automation** (to ask Spotify what's playing) and **System Audio Recording** (the process tap). A third, **Microphone**, is optional and only used by **Detect delay** (see "Audio delay").

- A small welcome window explains the two required ones and triggers the prompts on purpose. That way a prompt never pops up during an idle open while nobody is at the Mac. It appears at launch whenever macOS hasn't been asked about one of them yet (not just on the very first launch, so it returns if a new signature or a reset makes macOS forget), and from the popup's error rows. The microphone prompt only appears the first time you press **Detect delay**.
  - One **Continue** button asks for Automation and then System Audio Recording, each only if it was never asked. Each row shows Not asked, Asking…, Allowed, or Not allowed with a link to System Settings. Once nothing is left to ask, the button reads **Done**.
  - Automation is asked with `AEDeterminePermissionToAutomateTarget(..., askUserIfNeeded: true)`, off the main thread, since it waits for the answer. System Audio Recording is asked by starting the tap, which stays up until macOS has an answer (two minutes at most).
  - Both prompts need Spotify running. If it isn't, Continue waits, the window asks you to open Spotify, and it carries on by itself once Spotify is running.
- **No prompt outside the welcome window.** While Automation was never asked, the AppleScript query isn't sent (the runner checks first), and while System Audio Recording was never asked, opening the visualizer doesn't start the tap. Detect delay still starts it, since that is a button press.
- Check Automation without prompting with `AEDeterminePermissionToAutomateTarget(..., askUserIfNeeded: false)`: 0 is allowed, -1743 denied, -1744 never asked. It only answers while Spotify runs (-600 otherwise), and it doesn't launch Spotify; until then the state is unknown, which doesn't count as missing.
- There's no public API to check the audio permission, so the app asks the private `TCCAccessPreflight("kTCCServiceAudioCapture")` (0 allowed, 1 denied, 2 never asked), loaded with `dlopen` so a macOS without it just skips the check. Checked in step 8b: it answers for the app itself only when the app was launched through LaunchServices; a binary started from a terminal gets the terminal's answer. Only when that function is missing does the silent-tap check below decide. Treat several seconds (5, in `TapHealth`) of exact-zero buffers, or no buffers at all, while Spotify reports "playing" as "probably denied" and log it. Spotify playing on another device (Spotify Connect) looks the same, and the log line says so. (A paused Spotify sends exact-zero buffers, and one that hasn't played since launch sends none, so only count time while it's playing.)
- No camera permission, ever.

### When a permission is missing
- **Menu-bar icon turns yellow.** While a required permission (Automation or System Audio Recording) is missing, swap the template icon for a copy tinted `NSColor.systemYellow`. Switch back once everything is fixed.
- **Error rows in the popup**, below the "Open visualizer" row, one per missing permission: "Spotify control not allowed" or "Spotify audio blocked". Clicking one opens the matching System Settings privacy page:
  - Automation: `x-apple.systempreferences:com.apple.preference.security?Privacy_Automation`
  - System Audio Recording: `x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture` (the "Screen & System Audio Recording" page)
  - Check once on macOS 26 that each link lands on the right page. If one doesn't, System Settings just opens on a nearby page, so nothing breaks.
  - A permission that was never asked counts as missing too, but its row opens the welcome window instead ("Allow access ›"), because the app isn't listed in System Settings until macOS has asked once.
- Re-check each time the popup opens, when the app becomes active, when Spotify launches, when the visualizer opens, and whenever an AppleScript query is held back or fails with a permission error (`-1743`).
- The microphone is optional, so a missing microphone permission doesn't turn the icon yellow. It only shows as a hint next to **Detect delay** in settings.

---

## Architecture

One window, one web page. The page holds the Butterchurn visualizer canvas, a sandboxed frame for custom JS plugins, and the overlay.

```
Menu-bar helper (Swift)
├─ Triggers: IdleWatcher, Hotkey, URL scheme ──► OpenRules ──► WindowController
├─ DismissWatcher ──────────────────────────────────────────► WindowController (close)
├─ KeepAwake (display-sleep assertion + time limit) ───────► WindowController (close at limit)
├─ PowerSource (plugged in / battery) ──► picks idle timeout + keep-awake limit
├─ Permissions (check, yellow icon, popup error rows)
├─ SpotifyInfo (notifications + AppleScript + artwork) ──► page: window.nowPlaying(json)
├─ SpotifyAudioTap (Core Audio → FFT in Swift → AudioDelay) ──► page: window.audioFrame(data)
└─ PresetLibrary (scan/watch custom folder) ───► page: window.setCustomPresets(json)

Page layers (bottom → top): Butterchurn canvas or plugin frame → dim layer → overlay
```

### Menu-bar helper
**UI reference:** build the menu-bar popup and the settings window to match [mockups.html](mockups.html) (layout, grouping, row order, sizes, and build notes). Open it in a browser.

- `LSUIElement = YES`, launch at login (`SMAppService.mainApp.register()`). The settings switch reads its state from `SMAppService.mainApp.status` instead of storing a setting, since it can also be changed under Login Items in System Settings.
- The menu-bar popup is a small glass-style popover (SwiftUI `MenuBarExtra` with `.window` style, or `NSPopover`), not a plain `NSMenu`. The background is real Liquid Glass (`glassEffect`). The app requires macOS 26, so there's no fallback look. It is deliberately tiny, with two rows:
  1. **Settings…** opens the separate settings window.
  2. Below it, a disabled, informational row showing the current open hotkey (e.g. "Open visualizer: ⌃⌥V"), read from `KeyboardShortcuts` so it updates if the shortcut changes.
  3. Only while a required permission is missing: one error row per missing permission, each opening its System Settings page (see "When a permission is missing").
- There is no Quit item in the menu or in settings. The red close button closes the settings window, and ⌘Q (while the window is focused) quits the app.
- **Settings window** (separate native window, a normal `NSWindow` with SwiftUI content that follows the macOS light/dark appearance automatically, with no setting; the app switches to `.regular` activation policy while it's open so it can take focus, then back to accessory on close). It is small, portrait and fixed-size (about 340 × 560 pt): no `.resizable` in the style mask, `collectionBehavior = [.fullScreenNone]`, zoom button disabled. It is one scrolling page (no sidebar or tabs) with three sections, in this order:
  - **General:** idle timeout (5/10/15/30 min, Off), keep screen awake for (30 min, 1 hour, 2 hours, 4 hours; default 1 hour), **Different times on battery** switch (only on Macs with a battery; off by default; when on, it reveals "On battery: start after idle" and "On battery: keep screen awake", with the same choices, starting as copies of the values above), open hotkey recorder (`KeyboardShortcuts.Recorder`), **Like key** and **Skip key** pickers (see "Like and skip keys" under Dismiss), an "Open now" button, launch at login.
  - **Visualizer:** Show Spotify overlay toggle, brightness slider (50–100%), audio delay slider (0–2.5 s, for the current output device) with a **Detect delay** button and a **Manual delay test** row under it, mode (Single or Shuffle). Single: a picker for the one visualizer to show. Shuffle: shuffle-from filter (all/bundled/custom/favorites), seconds per preset, blend time. Then favorites and blocklist management.
  - **Presets:** plugin trust warning, Import Presets…, Open Presets Folder, Reload Presets, and the list of presets that failed to load.
- Settings in `UserDefaults`. The window writes them and the helper applies changes live.

### Triggers
- **Idle:** `CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: CGEventType(rawValue: ~0)!)` (any input). Instead of polling on a fixed interval, schedule the next check for `timeout − idleTime` (the earliest it could fire). Setting `idleTimeout` in minutes (5/10/15/30, 0 = Off), default 5. Also recheck after wake and unlock.
- **One attempt per idle period.** When the idle trigger fires and the open is blocked (by a skip rule below or by the open rules), it doesn't retry until there has been new input. While still idle, the next check is a full timeout later, the earliest a new idle period could fire.
- **Idle skip rules** (idle trigger only):
  - The screen is locked (`CGSessionCopyCurrentDictionary`, `CGSSessionScreenIsLocked`), or the session isn't on the console (fast user switching).
  - Another process, not Spotify or IdleViz itself, holds a `PreventUserIdleDisplaySleep` / `NoDisplaySleepAssertion` power assertion (`IOPMCopyAssertionsByProcess`). This covers fullscreen video, calls and presentations, whether or not they're frontmost. Assertions a daemon holds on behalf of Spotify or IdleViz (`AssertionOnBehalfOfPID`) don't count either.
  - After the keep-awake limit closed the visualizer, don't reopen until there has been new input. Otherwise it would reopen at once, since the Mac is still idle.
- **Hotkey:** `KeyboardShortcuts` Swift package.
- **Terminal:** URL scheme, so `open idleviz://open` works.
- **Open rules check:** Spotify is running (see below) and has a current track. The check uses the track state the notifications keep up to date, so opening is instant. Only if nothing is known yet (no query has answered since Spotify launched) does it run one AppleScript query and wait for it. On failure from a manual trigger, flash the menu-bar icon (`waveform` ↔ `waveform.slash`, 3 times over ~1 s).

### Dismiss
- Make the window key (subclass `NSWindow`, override `canBecomeKey` → `true`) so a **local** `NSEvent` monitor receives keys without needing Accessibility/Input Monitoring permission. Add a global monitor for mouse events as a backstop.
- **Activation may be refused.** On current macOS activation is cooperative, so `NSApp.activate()` can be denied for an idle open (another app is active and there was no user action). Then the window isn't key and the local monitor gets no keys. The backup below still closes the window, but the key that woke it goes to the app behind (for example, a letter typed into a document). `NSCursor.hide()` also only works while the app is active, so the cursor may stay visible. Each open logs whether activation was granted, with a running count (`log stream --predicate 'subsystem == "com.xauno.IdleViz"'`), so this can be measured once the idle trigger exists.
- Remember the frontmost app when opening and reactivate it on close, so focus returns where it was.
- Events: `.mouseMoved`, `.leftMouseDown`, `.rightMouseDown`, `.otherMouseDown`, `.scrollWheel`, `.keyDown`, `.flagsChanged`, `.gesture`, `.magnify`, `.swipe`. The local monitor also takes `.systemDefined`, for the media keys below.
- Threshold ≈ 0: close on the first `.mouseMoved` with any nonzero delta (≥ 1 px if the trackpad sends phantom zero-delta events).
- Ignore input for the first ~300–500 ms after opening so the trigger itself doesn't close it.
- Backup: while open, check `secondsSinceLastEventType` every ~100 ms; if it's less than the time since opening (minus the grace period), close. This needs no permissions and also catches keys when the window isn't key.
- Hide the cursor while open, restore on close.
- **Media keys.** The top-row keys that leave you watching don't close the window: brightness, keyboard backlight, play/pause, next, previous, mute and volume. They don't arrive as key events but as `.systemDefined` events with subtype 8, with the `NX_KEYTYPE_` value in bits 16 to 31 of `data1` (`MediaKey` in `IdleVizCore` lists the ones that count). The local monitor passes system-defined events on instead of swallowing them, and the tracker treats a media key as explained input for the backup check. Mission Control, Spotlight, Dictation and Focus still close the window, as do F1–F12 as standard function keys and the fn key. If the window isn't key, the monitor doesn't see media keys and the backup check closes the window.
- **Like and skip keys.** Two more keys don't close the window. The local monitor hands them to the app instead:
  - **Like** (L by default) toggles the preset on screen on the favorites list. Swift asks the page which preset is on screen at that moment (`window.idlevizStatus()`), since `lastShownPreset` can be a second behind, then calls `window.showLike(bool)`. The page shows a heart for about 1.6 s: filled when the preset was added, an outline when it was taken off. The heart sits beside the track title while the full layout is showing, and in the top-right corner in every other layout (paused, ad, podcast, no track, overlay off).
  - **Skip** (N by default) calls `window.skipPreset()`. In shuffle mode the page blends to the next preset from the shuffle pool in 0.5 s, whatever the blend time setting is, and the seconds-per-preset count starts over. In single mode it does nothing.
  - Settings `likeKey` and `skipKey` hold a virtual key code, or -1 for Off. The pickers offer A–Z, 0–9, the arrow keys and Space, and leave out the key the other action uses. Keys are matched by position, so the labels are those of a US layout. A stored value that isn't one of the choices counts as the default. The keys are read each time the window opens.
  - `DismissTracker` gets the two key codes as `passKeys`. A pass key the event monitor sees never closes the window, pressed, repeating or released, and counts as explained input for the backup check. Key repeat doesn't run the action again. A modifier still closes the window, so ⌘L does too.
  - The keys only work while the window is key. If activation was refused, the monitor never sees them and the backup check closes the window as for any other key.
  - The system idle time resets the moment a key moves, before the event reaches the local monitor. So when the backup check wants to close, it waits 30 ms and checks again, which gives the monitor time to explain a like, skip or media key.
- **Fades** animate the window's `alphaValue`: in over 0.6 s, out over 0.25 s on input and 1.5 s at the keep-awake limit (`CloseReason` in `IdleVizCore`). Sleep and display changes close it with no fade. When a fade-out starts, the Mac is handed back at once: the cursor returns, the previous app is reactivated, the window ignores clicks and the keep-awake assertion is released. The page, the tap and the status checks keep running until the fade ends, so the visuals don't freeze. A trigger during a fade-out finishes the close and opens again.
- **Display changes.** The window closes when the Mac goes to sleep and when the displays change. `NSApplication.didChangeScreenParametersNotification` is not enough to tell: macOS also posts it when the Dock or the menu bar changes size, and a notification can do that (a message arrives on the phone, a Handoff tile joins the Dock). So the app keeps the display layout (each display's ID, frame and scale, main display first; `DisplayTracker` in `IdleVizCore`) and closes only when that differs from the last one it saw.
- **Debug switch:** in Debug builds, the launch argument `-IdleVizNoDismiss YES` turns dismiss off, so the page can be inspected while it's open.

### Keep awake
- While the window is open, hold a `kIOPMAssertionTypePreventUserIdleDisplaySleep` assertion (`IOPMAssertionCreateWithName`, named "IdleViz visualizer"). Release it on close.
- The limit (setting `keepAwakeLimit`, in minutes, default 60) counts from when the window opened. When it's reached, fade out, close and release the assertion, so the normal display sleep, screensaver and lock take over.
- A changed setting applies at once to an open window, still counted from when it opened.
- Security tradeoff, accepted: during the limit the Mac doesn't lock on its own.

### Battery
- The app opens on battery like it does when plugged in.
- Settings `useBatteryTimes` (default off), `idleTimeoutBattery` and `keepAwakeLimitBattery`, in minutes. While `useBatteryTimes` is on and the Mac is on battery, those two replace `idleTimeout` and `keepAwakeLimit`. A battery value that was never set counts as a copy of the plugged-in one. A UPS doesn't count as a battery.
- Read the power source with `IOPSGetProvidingPowerSourceType(nil)` and watch for changes with `IOPSNotificationCreateRunLoopSource`. No polling.
- On a power-source change: reschedule the next idle check with the new timeout. If the window is open, apply the new limit, still counted from when it opened (close right away if it's already past).
- On a Mac with no battery (Mac mini, iMac, Mac Studio, Mac Pro), hide the **Different times on battery** switch and its two rows entirely. Detect this by checking `IOPSCopyPowerSourcesInfo` for an internal battery (`kIOPSInternalBatteryType`).

### Window
- One borderless `NSWindow` on `NSScreen.screens.first` (the menu-bar display). Create it via a function that takes an `NSScreen`, so multi-display is easy later.
- `level = .screenSaver`, `collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]`.
- Content: one `WKWebView`. Serve everything from a custom scheme with a `WKURLSchemeHandler`, not `file://`:
  - `idleviz-app://app/…` serves the bundled `web/` folder.
  - `idleviz-app://presets/…` serves the custom presets folder, with `Access-Control-Allow-Origin: *` so the sandboxed plugin frame (an opaque origin) can import from it.
  - This gives one origin, working ES modules, and a place to send the Content-Security-Policy as a response header. `idleviz://` stays the external "open" URL scheme and is never loaded in the page.
  - The Windows app serves the same folders at `https://app.idleviz.invalid/…` and `https://presets.idleviz.invalid/…`, answered by the app itself and never sent to the network. WebView2 blocks a sandboxed frame from loading scripts off a custom scheme, so the plugin frame can't work from `idleviz-app://` there. The page accepts both forms for plugin and preset URLs (`URL_PREFIXES` in `visualizer-state.js`); nothing else in the page names the scheme.
- Keep the window and web view alive between opens (hide instead of destroying it) so opening is instant.
- **Create the window at launch**, hidden, while the app is still an accessory. macOS decides when a window is created whether it may join another app's fullscreen Space, and the policy at that moment is what counts: a window created while the app is `.regular` (settings open) only ever shows on desktop Spaces, even after the app is an accessory again, while one created as an accessory also shows over fullscreen apps while settings is open. Switching the policy later, hiding the settings window or setting `collectionBehavior` again doesn't change it. So the settings window stays open when the visualizer opens, and the policy isn't touched.
- **Wait for the page's scripts.** WebKit can report the navigation as finished before the page's modules have run (about one launch in five, found in step 7c), and a call made then is silently lost. After `didFinish`, Swift asks the page every 50 ms whether its functions exist, and only then sends the current state (preset settings, now playing).
- **Recovery:**
  - Reload the page and re-send state on `webViewWebContentProcessDidTerminate`.
  - Replace the web view if the once-a-second status check gets no reply for ~3 s (see "Loading and failure" under Presets). A reload isn't enough: checked in step 7b, a page stuck in a JavaScript loop never starts the navigation. A new `WKWebView` gets a new web content process and the latest state is sent again. WebKit leaves the stuck process spinning at full CPU, so the app ends it with `kill`, using the process ID from WebKit's private `_webProcessIdentifier` property (skipped if a future WebKit drops it).
  - Rebuild Butterchurn on `webglcontextlost`, which can happen after sleep and wake.
  - Close the visualizer on `NSWorkspace.willSleepNotification` and `NSApplication.didChangeScreenParametersNotification`, since the main display may have changed.
- Set `isInspectable = true` in Debug builds so Safari's Web Inspector can attach.
- Deny all media capture (see the audio guarantee) and set `mediaTypesRequiringUserActionForPlayback = []` in case Web Audio is used.

---

## Spotify now-playing

**Never launch Spotify.** Sending an Apple Event to a closed app launches it, so only talk to Spotify when it's running:
- Track running state with `NSWorkspace` `didLaunchApplicationNotification` / `didTerminateApplicationNotification` (bundle ID `com.spotify.client`), seeded once from `runningApplications`. No polling.

**Getting track info without constant polling:**
- Spotify posts a distributed notification, `com.spotify.client.PlaybackStateChanged`, on play/pause/track change. Observe it with `DistributedNotificationCenter` and use it as the trigger for updates. Checked in step 3: it carries name, artist, album, album artist, duration, playback position, player state and track ID, but not the artwork URL. When playback switches to a new context (playing an album or playlist by URL), Spotify first posts `Player State = Stopped` with no track ID, then `Playing`. Skipping with next/previous doesn't. So "no track" can last a fraction of a second during a normal switch; the overlay should wait a moment before hiding on it.
- On a `Stopped` notification, don't query: Spotify may be quitting, and an Apple Event then could launch it again.
- On each notification (and once when the window opens), run one AppleScript query for the full state, including artwork URL:

```applescript
tell application "Spotify"
    with timeout of 2 seconds
        set s to player state as string
        if s is "stopped" then return "stopped"
        set t to current track
        set d to character id 31
        set a to ""
        try
            set a to artwork url of t
        end try
        set u to ""
        try
            set u to spotify url of t
        end try
        return s & d & (name of t) & d & (artist of t) & d & (album of t) & d & a & d & (duration of t) & d & (player position) & d & u
    end timeout
end tell
```

- The page advances the progress bar locally from position + timestamp, minus the current audio delay so it matches what you hear; no per-second polling. While the window is open, do a light re-sync every ~5 s to correct drift and catch seeks.
- Run via `NSAppleScript`, compiled once, on one dedicated background thread (`NSAppleScript` isn't thread-safe, and a hung Spotify must never block the main thread). Needs `NSAppleEventsUsageDescription`.
- Fields are joined with ASCII unit separator (`character id 31`), which can't appear in a title, unlike a printable separator such as `||`. The query lives in `SpotifyQuery` in `IdleVizCore`.
- Units: `duration` in **milliseconds**, `player position` in **seconds**. AppleScript turns numbers into text with the system locale, so the position can have a decimal comma.
- Podcast episodes report an empty artist (the show is in `album`), and a duration of 0 for a moment right after they start.
- "No current track" = state `stopped`, a `Stopped` notification, Spotify quitting, or an empty name/URL. Ads may have an empty name; they still count as a track.
- A failed query (a timeout or other AppleScript error) keeps the last known track instead of clearing it, so one slow answer from Spotify doesn't stop the visualizer from opening. Before any query has succeeded, there is no track.
- The first query after installing a new build can time out (error -1712) while macOS shows the Automation prompt, because the prompt counts against the 2 s timeout. The next notification queries again.
- **Artwork:** Swift downloads the artwork URL with `URLSession`, keeps a few recent images in memory, and passes the image to the page as a `data:` URL inside the `nowPlaying` JSON. The page itself never uses the network. Local files (`local:`) have no artwork, so the overlay shows a placeholder, designed against the reference screenshots.
- **Spotify Connect:** when Spotify plays on another device, AppleScript still reports the track, so the window opens as usual. The tap finds no local audio, so the visuals get silence. No special detection needed.

**Content type** from the `spotify url` prefix: `track:` / `local:` → song, `episode:` → podcast, `ad:` → ad.
- Ad context: remember the type of the last non-ad item. After a podcast episode, an ad is a podcast ad (no overlay); otherwise it's a music ad (minimal overlay).
- Test how ads report in practice. Ads baked into podcast audio never show up as a separate item and simply count as the podcast.

---

## Overlay design (Spotify TV app look)

Match the Spotify TV app's now-playing screen, with these changes: the visualizer replaces the artist image in the background, and the overlay shows only the album art, title, artist, progress bar and times.

- **Left out on purpose:** the Spotify logo and "Playing from" header in the top left (the app doesn't know the playlist or album being played from), and the row under the progress bar (heart, shuffle, previous, play/pause, next, repeat). The progress row sits lower than on the TV (at y 884 on the stage instead of about 840), taking some of the space the control row used.
- The reference is a photo of the TV app (`design/reference/tv-now-playing-photo.jpg`). The folder is in `.gitignore`: the repo is public and the image shows Spotify's copyrighted design, so it stays local. Positions on the 1920×1080 stage were measured from it: art 176 × 176 at (136, 608), text 48 px right of the art, progress row from x 136 to 1784.
- Build on a fixed 1920×1080 stage scaled with `transform: scale()` so proportions match the TV on any display. Scale to the window width and pin the stage to the bottom, where all of the overlay sits. On a 16:10 Mac screen that keeps the TV's bottom margins instead of adding empty space below.
- A soft black gradient (the scrim) rises from the bottom of the screen so white text stays readable over a bright visualizer.
- Paused: fade out everything except the progress bar (and its time labels), which stays exactly where it is.
- Music ad: an "Advertisement" label just above the progress row, at the art's left edge.
- No track (or Spotify quit): wait 1.5 s, since a context switch reports "no track" for a moment, then fade the overlay out. The visualizer keeps running.
- Missing artwork (local files): a grey square with a music note. While artwork is still downloading, the square stays empty instead of flashing the placeholder.
- Track change: crossfade art and text.
- The page logic that doesn't touch the DOM (layout per state, progress interpolation, time format, payload checks) lives in `web/overlay-state.js` and is tested with Vitest.
- **Font:** Figtree (SIL Open Font License), bundled locally in `web/fonts/` with its license file; don't load it from a CDN. Use the variable font. Weights tuned against the reference: title 800 at 72 px (`letter-spacing: -0.02em`), artist 600 at 26 px, times 400 at 24 px with tabular figures. Don't copy Spotify's font files from the Spotify app.
- **Brightness:** a black dim layer between the visualizer and the overlay. This is cheaper than a CSS `filter` on a WebGL canvas.

```css
@font-face {
  font-family: "Figtree";
  src: url("fonts/Figtree[wght].ttf") format("truetype");
  font-weight: 300 900;
}
:root {
  --overlay-font: "Figtree", -apple-system, sans-serif;
  --viz-brightness: 0.7;            /* setting: visualizerBrightness */
}
#dim { background: #000; opacity: calc(1 - var(--viz-brightness)); }
```

Swift updates brightness live from the settings slider with `window.setBrightness(value)` (setting `visualizerBrightness`, 0.5 to 1, default 0.7, in whole percent). The page clamps the value and sets `--viz-brightness`; setting it from script is allowed by the CSP, which only blocks inline style attributes and `<style>` blocks.

**Show Spotify overlay** (setting `showOverlay`, default on) reaches the page as `window.setOverlayEnabled(bool)`. Off means no layout in any state, the scrim included. Both values are sent when the page is ready and again on every change, like the preset settings.

### Page security (CSP)
Sent as a response header by the scheme handler for the host page:

```
default-src 'none';
script-src 'self' 'unsafe-eval';
style-src 'self';
font-src 'self';
img-src 'self' data: blob:;
connect-src 'self' idleviz-app://presets;
frame-src 'self';
```

- `'unsafe-eval'` is needed because Butterchurn compiles preset equations with `new Function`. Verify this against the version you use.
- That means custom `.json` and `.milk` presets are code too: their equations run in the host page. The plugin trust warning in settings covers them as well.

---

## Visualizer (Butterchurn)

The only visualizer is **Butterchurn** (WebGL port of Milkdrop), chosen because the goal is a big, browsable library rather than hand-written scenes. There is no external-app mode.

### Audio
- Core Audio process tap on Spotify only.
  - Find Spotify's audio process objects via `kAudioHardwarePropertyProcessObjectList`. Match every process whose bundle ID starts with `com.spotify.client`, which includes its helper processes, and tap them all.
  - Spotify's process object appears in that list as soon as Spotify launches, before anything plays (checked in the step 2 spike). Listen for changes to `kAudioHardwarePropertyProcessObjectList` and rebuild the tap when Spotify's processes come or go, not just when Spotify relaunches.
  - `CATapDescription(stereoMixdownOfProcesses:)` with `muteBehavior = .unmuted` → `AudioHardwareCreateProcessTap` → private aggregate device → `AudioDeviceCreateIOProcIDWithBlock`.
  - Create the I/O block in a `nonisolated` function. A closure written inside a `@MainActor` method inherits that isolation, and Swift 6 crashes when Core Audio calls it on its I/O thread.
  - Clear the sample buffer when the tap is torn down, so that frames built afterwards are silence rather than the last samples repeated.
  - Needs `NSAudioCaptureUsageDescription`; macOS prompts once (see "Permissions and first launch"). Builds with a changing signature may get all-zero buffers until permission is granted to the right binary.
  - Run it only while the window is open, plus briefly for the first-launch permission prompt and during Detect delay.
- **Analysis happens in Swift** (vDSP). Each frame, Swift computes everything the page needs, so JS only unpacks numbers:
  - the plugin audio object: 64 bands (noise-floored, smoothed), `bass`/`mid`/`treble`, `rms`, and a 1024-sample waveform;
  - Butterchurn's input: 1024-sample 8-bit time-domain data, mono plus left and right.
  - Swift packs this into one binary frame and sends it ~60×/s with `evaluateJavaScript`, base64-encoded.
- **Automatic gain.** The tap captures after Spotify's own volume slider (but before the system volume), so a low Spotify volume would mean weak visuals. Normalize with a slow automatic gain on the RMS level, with a gate so real silence stays silent. As built (`AutoGain`): it follows the RMS of each 1024-sample frame, rising to louder audio in about 0.5 s and falling to quieter audio in about 5 s, and scales it to 0.2. The gain stays between 0.5× and 32×, starts at 1×, is held while the input is below −60 dBFS, and is remembered between opens.
- **Bands as built** (`AudioAnalyzer`): a Hann-windowed 1024-point FFT of the gained mono signal. Each band maps −65 dB to −10 dB (a full-scale sine is 0 dB) onto 0..1, moves 70% of the way to a higher value per frame and 12% of the way to a lower one, and snaps to exact zero in silence. The lowest bands are narrower than one FFT bin (47 Hz at 48 kHz), so they read a value interpolated between the two nearest bins.
- **Sample rate.** Each frame carries the tap's sample rate. When it changes (another output device), the page recreates Butterchurn with a new `AudioContext` at that rate. The tap is rebuilt when the default output device changes, since that device is its clock.
- **Feeding Butterchurn.** Pass the levels straight to `visualizer.render({ audioLevels: { timeByteArray, timeByteArrayL, timeByteArrayR } })`, which skips Web Audio entirely. The step 2 spike confirmed this works in `butterchurn` 2.6.7 (the current stable release; 3.0 is still in beta), so no `AudioWorklet` fallback is needed. `createVisualizer` still takes an `AudioContext`, but it can stay suspended and unconnected. Butterchurn reads that context's `sampleRate` to place its bass/mid/treble ranges, so create it with the tap's rate (`new AudioContext({ sampleRate })`).
- **Spotify-only audio guarantee: the visuals only ever see Spotify's audio** (applies to Butterchurn and to every custom plugin):
  - The only audio source for the visuals is the Spotify process tap above. Never feed them from a global/system tap, an input device, or the microphone. Always build the tap with `CATapDescription(stereoMixdownOfProcesses:)` containing Spotify's process objects only. If none are found, send silence.
  - The one exception to "no microphone" is **Detect delay** (below). It runs only in Swift, only after you press the button, for about 5 s, and the mic audio is never sent to the page, stored, or used for anything but measuring the delay.
  - Audio reaches JS only through `window.audioFrame(data)`, then a single host wrapper hands each visual its frame. A plugin never gets an `AudioContext`, `MediaStream` or node it could wire to something else.
  - The web view denies all media capture: implement `WKUIDelegate`'s `requestMediaCapturePermissionFor` to always return `.deny`. The app has the microphone entitlement and usage string only for Detect delay, so this delegate is what stops page or plugin code from calling `getUserMedia` to get mic or other audio. No camera entitlement or usage string.
  - If Spotify quits, plays on another device, or the track is an ad or podcast with no audio source, the frame is silence (zeros), not a fallback to anything else.
  - Test: play other audio (a YouTube tab, system sounds) while Spotify is paused and confirm the visualizer stays flat.

### Audio delay
The tap hears Spotify's audio before it reaches the speakers. With built-in speakers the gap is tiny, but Bluetooth adds about 150–300 ms and AirPlay about 2 s, so the visuals run ahead of the sound.

- **Delay line in Swift.** Keep the analysed frames in a small ring buffer and send each one to the page `delay` ms after it was captured. The page and plugins don't know about it. The progress bar subtracts the same delay (see "Spotify now-playing").
- **Per output device.** Store the delay by the default output device's UID (`kAudioHardwarePropertyDefaultOutputDevice` → `kAudioDevicePropertyDeviceUID`) in a dictionary setting, `audioDelayByDevice`. Listen for default-device changes and switch to that device's value. A device with no saved value starts at the latency macOS reports for it: the output device's `kAudioDevicePropertyLatency` + `kAudioDevicePropertySafetyOffset` + its stream's `kAudioStreamPropertyLatency`. That's roughly right for AirPlay and often a bit low for Bluetooth, so it's a starting point, not a replacement for Detect delay.
- **Manual:** the Audio delay slider in settings, 0–2.5 s in 10 ms steps, for the current device. The hint under it names the device.
- **Detect delay button:**
  1. Only works while Spotify is playing out loud. Otherwise the hint explains why and nothing happens.
  2. Asks for microphone permission the first time (`NSMicrophoneUsageDescription`, "Used only when you press Detect delay, to match the visuals to your speakers.").
  3. For about 5 s, record the mic with `AVAudioEngine` while also recording the tap's signal.
  4. Cross-correlate the two recordings over lags of 0–2.5 s (see "As built" for the method). Subtract the mic's own input latency (`kAudioDevicePropertyLatency` + safety offset + stream latency of the input device).
  5. If the correlation peak is clear, save the result for the current device and move the slider. If not (too quiet, noisy room, or headphones, where the mic can't hear the music), keep the old value and say so in the hint.
  6. Stop the mic immediately after. The mic audio is only held in memory during those few seconds.
- The correlation math lives in `IdleVizCore` and is tested with synthetic signals (a known delay plus noise).
- **Manual delay test** (the row under Detect delay): for when the microphone can't hear the sound (headphones), or to check a measured value by eye.
  1. **Start** opens a sheet on the settings window. If Spotify is playing, the app pauses it (`tell application "Spotify" to pause`, through the same AppleScript thread and checks as the query), since the beeps are hard to hear over music. This and the `play` at the end are the only times the app controls playback.
  2. An `AVAudioEngine` with one `AVAudioPlayerNode` plays a 60 ms beep every second on the default output device, each scheduled at an exact host time (`scheduleBuffer(_:at:)`), handed over about 2 s ahead. If the output device changes, the engine is rebuilt on the new one.
  3. The sheet's panel is redrawn every frame (`TimelineView(.animation)`) and lit for 120 ms starting `delay` after each beep's scheduled time, read from the same host clock. When the light and the sound land together, the delay matches what the speakers add.
  4. Every fourth beep is an octave higher (1760 Hz instead of 880 Hz) and its flash is orange instead of white. The delay can be longer than the gap between beeps, and this tells a flash from the one a beep earlier or later.
  5. The sheet has the delay slider and −10 ms and +10 ms buttons. They change the same per-device delay as the slider in the Visualizer section, live.
  6. **Done** stops the beeps, saves the delay for the device even if it wasn't moved, and sends `play` if the app had paused Spotify.
  - The timing (`BeepTest.flash(at:start:delay:)`) and the beep samples live in `IdleVizCore` and are unit-tested. Detect delay and the manual test can't run at the same time.
- **As built (step 7e):**
  - `DelayLine` holds the packed frames with their capture time; each timer tick sends the newest frame that is at least `delay` old. Opening the window clears it and sends one frame of silence.
  - The page gets the delay with `window.setAudioDelay(seconds)` and subtracts it from the progress bar's position while playing.
  - `DelayDetector` cross-correlates the two waveforms with a softened phase transform (GCC-PHAT with the magnitude raised to 0.8, 300 Hz to 6 kHz), averages the result over 5 ms so a spike's closest echoes add to it, and takes the best lag from -0.1 to 2.5 s. A result counts only if its peak is at least 10 standard deviations above the lags more than 100 ms away and 1.5 times the best of them, which rejects headphones, noise and other music. A lag below zero is saved as no delay. The first version correlated loudness envelopes instead; with real speakers it succeeded about half the time, read about 30 ms late, and once returned 320 ms for the built-in speakers.
  - The two recordings are lined up on the host clock: the tap's by when its first buffer was handed to the app (`inNow` in the I/O block), the microphone's by `AVAudioTime.hostTime` of its first buffer minus its input latency. The tap is measured from the hand-over, not from the samples' own timestamps, because the delay line also counts from the hand-over.
  - The tap delivers audio at the aggregate device's sample rate, which is the output device's, whatever `kAudioTapPropertyFormat` says: 44,100 Hz on AirPlay while the format says 48,000. `SpotifyAudioTap` reads the rate from the aggregate.
  - The microphone is tapped in the input node's input format. Its output format follows the output device's sample rate, and a tap in that format gets no audio when the rates differ (AirPlay).
  - Measured on a MacBook Pro: the built-in speakers sound about 40 ms before the tap hands the audio over, so they measure as 0 ms and the visuals trail the music by about that much, which no delay can fix. An AirPlay speaker measured 1,950 ms, five runs within 1.5 ms of each other, and matched by eye on track changes.
  - The tap's signal is recorded inside `SampleRing` while Detect delay runs, and the tap runs for it even when the window is closed.
  - The microphone is the Mac's built-in one when it has one, whatever the default input is: recording from a Bluetooth headset's own microphone would switch the headset to call mode and change the delay being measured.
  - A measured value is saved even when it equals the reported latency. A device that was never measured or adjusted keeps following what macOS reports.

### Audio spike findings (step 2)
Tested on the throwaway `spike/audio-tap` branch, on a MacBook Pro (M5 Pro, 3024 × 1964 built-in display, built-in speakers) with macOS 26 and the Spotify desktop app. The spike used a Spotify process tap, a vDSP analysis in Swift, one packed frame per display frame sent with `evaluateJavaScript`, and Butterchurn 2.6.7 in a `WKWebView` loaded with `loadFileURL` (no custom scheme or CSP yet).

**What worked**
- **Permission.** macOS showed the System Audio Recording prompt when the tap first started, and after Allow the audio was real (not all-zero). The build was signed with the Apple Development team.
- **Tap.** The tap delivers 48 kHz, 2-channel, interleaved Float32. Setting up the tap and the aggregate device (default output as the main sub-device, the tap with drift compensation, `TapAutoStart`) took 20–25 ms.
- **Process list.** Spotify had exactly one audio process object with a `com.spotify.client` bundle ID, and its helpers never showed up. The list listener fired when Spotify launched and when it quit, and the tap rebuilt and tore down cleanly each time.
- **Spotify only.** With Spotify paused, system sounds (`afplay`) left the tap at exact zero.
- **Swift analysis.** Building a frame (a 1024-point vDSP FFT, 64 log bands, RMS and packing) took 50–75 µs in an optimized build (about 1 ms unoptimized).
- **Transport.** One frame is 7,448 bytes: a 24-byte header, 64 bands as `f32`, a 1024-sample `f32` waveform, and 3 × 1024 bytes for Butterchurn. Sent base64-encoded with `evaluateJavaScript` 60 times a second, the round trip averaged about 0.5 ms and the page decoded a frame in 0.02–0.1 ms. No frames were dropped.
- **Butterchurn.** `render({ audioLevels })` works (see "Feeding Butterchurn"). Its `bass` value ranged about 0–2.4 each second, a normal Milkdrop range, and the visuals clearly followed the beat. Rendering held 60 fps at 2560 × 1663, with `render()` taking 1.5–2 ms of JS per frame. `requestAnimationFrame` ran at 60 Hz on the 120 Hz display.
- **Cost while playing** (`top`, percent of one core): IdleViz about 5.5%, WebKit WebContent 13–15%, WebKit GPU process 5–7%, `coreaudiod` 7–9%, WindowServer about 19% (including compositing the fullscreen window). The GPU's "Device Utilization" was about 22% at 2560 px wide and 0–12% at 1512 px and below. That counter is noisy at low load, so tune the resolution cap by power use in step 7b.

**What had to change** (already written into the sections above)
- Spotify's process object appears when Spotify launches, not when it first plays. Before the first play the I/O block gets no buffers at all; once paused, it gets exact-zero buffers. This changes the permission check under "Permissions and first launch".
- The I/O block has to be created outside the main actor, or Swift 6 crashes on the first buffer.
- The sample buffer has to be cleared on teardown.
- The 60 fps cap needs a tolerant threshold.
- Frames sent before the page has loaded throw a JavaScript exception. Swift should only start sending after the page finishes loading, or call `window.audioFrame?.(…)`.
- Presets differ a lot in how reactive they look. The first one tried ("Flexi + Martin - cascading decay swing") looked disconnected from the music, while beat-driven ones ("Flexi, martin + geiss - dedicated to the sherwin maxawow", "Zylot - Paint Spill (Music Reactive Paint Mix)") clearly followed it. Keep this in mind when picking the default preset and the shuffle list in 7a/7c.

**Development notes**
- LaunchServices sends `idleviz://open` to whichever registered copy of `com.xauno.IdleViz` it picks, so several builds on disk (Debug, Release, other derived-data folders) can make the URL open a stale copy. Keep one build around, or unregister the others with `lsregister -u`.
- `loadFileURL` drops query strings. This won't matter once the page is served from `idleviz-app://`.

### Performance
- Cap rendering at 60 fps, including on 120 Hz ProMotion displays (skip every other `requestAnimationFrame`). Skip a frame only when it comes less than ~12 ms after the last one. A strict 16.7 ms threshold drops about a third of the frames on a 60 Hz display because of timestamp jitter (step 2 spike).
- Render Butterchurn below full device resolution on large displays (at most 2560 px wide for now) and let the GPU scale it up. Tune by eye and by power use.
- The page only renders while the window is open. WebKit stops `requestAnimationFrame` while the web view is hidden, so a closed window costs nothing (checked in step 7a: the frame count stays still while closed and runs at 60 per second while open).
- Butterchurn is created when the page loads, so its shaders are compiled before the window first opens.

### Presets
- **Bundled:** `butterchurn-presets` 2.4.7, shipped in the app: the base, Extra, Extra2 and MD1 packs, 395 presets once names that appear in more than one pack are dropped. Butterchurn and the packs are copied unchanged from npm into `web/vendor/` and committed, so building the app needs no Node step. A test checks their hashes.
- **Shuffle** shows every preset in the chosen set once, in random order, before any repeats.
- **Custom (folder import):** any preset you add yourself, loaded alongside the bundled ones.
  - Folder: `~/Library/Application Support/IdleViz/Presets/` (created on first launch; subfolders allowed, so a downloaded pack can be dropped in as-is).
  - Accepted files:
    - `.json`: Butterchurn-format presets, loaded directly.
    - `.js`: custom visual plugins (see "Custom JS plugins" below).
    - `.milk`: original Milkdrop presets, converted with `milkdrop-preset-converter` 0.1.2 (vendored in `web/vendor/`; its WebAssembly is inside the file, so it runs offline, checked in step 7d). Converted results are cached in `Presets/.cache/<key>.json`, where the key is a SHA-256 of the converter version and the file's contents, so conversion runs once per file and cached results of deleted or changed files are removed.
      - Conversion runs in a hidden web page of its own (`converter.html`, CSP `default-src 'none'; script-src 'self' 'unsafe-eval'`), not in the visualizer page. Untrusted files are parsed away from the visualizer, and a slow conversion can't stall the visuals. Swift opens it only while there are files to convert, hands it each file's text with `callAsyncJavaScript`, and checks the JSON that comes back before caching it. A conversion that takes over 10 s is abandoned.
      - The converter doesn't report errors itself: any text becomes an empty preset, and a shader it can't translate becomes the text "parsing failed". So Swift first checks that the file looks like a Milkdrop preset, and treats "parsing failed" in the result as a failure. A failed conversion isn't tried again until the file's contents change.
    - At most 5,000 files are read. Hidden files and folders are skipped.
  - Settings window controls (Presets section):
    - **Import Presets…** opens an `NSOpenPanel` (files or folders) and copies the picks into the presets folder. A name that's already taken gets a number, as in Finder ("Tunnel 2.milk"), so nothing is replaced.
    - **Open Presets Folder** reveals it in Finder.
    - **Reload Presets** rescans it.
  - `PresetLibrary.swift` scans the folder and watches it for changes (FSEvents, which covers subfolders; its own writes to `.cache` are ignored), so dropping files in updates the library without a restart. It sends the list to the page with `window.setCustomPresets(json)`: each entry has an id (`custom:` plus the path inside the folder), a name, its kind (preset or plugin), the `idleviz-app://presets/…` URL to load it from, and a version (size and modification time). The list also carries the plugins bundled in `web/visuals/`.
  - The page fetches a custom preset when it is first shown, not up front, so a big pack costs nothing until its presets come up. Editing or replacing the file that is on screen shows the new version at once.
  - `idleviz-app://presets/…` only serves `.json` and `.js` files inside the folder.
  - Each preset is validated (parse, compile its shaders) in a `try/catch` when first loaded. Bad ones are skipped and listed under "Failed to load" in settings ("3 presets failed to load"). They are not added to the blocklist, which only holds presets you chose to hide. When a failed file changes or is replaced, it's tried again.
  - Presets are tagged by source (bundled / custom) so the settings can shuffle all, bundled only, or custom only.
  - Check each pack's license before sharing the app. User-imported presets stay on the user's machine and are never bundled.
- **Preset control** (in the settings window, since any input but the like and skip keys closes the visualizer): mode (single visualizer or shuffle), seconds per preset, blend time, shuffle-from filter (all/bundled/custom/favorites), favorites, and a blocklist for presets you dislike.
  - Settings: `visualizerMode` (`shuffle`, the default, or `single`), `singlePreset` (a preset id), `shuffleFrom` (`all`), `secondsPerPreset` (15, 30, 45, 60, 120 or 300; default 30), `blendSeconds` (0, 1, 2.7, 5 or 8; default 2.7), `favoritePresets` and `blockedPresets` (lists of preset ids). A preset id is its source, a colon and its name, for example `bundled:Geiss - Swirlie 5`.
  - Swift sends them all to the page with `window.setPresetSettings(json)` when the page is ready and on every change, and the page applies them at once: a newly blocked or filtered-out preset on screen blends to the next one.
  - Blocked presets are left out of shuffle in every filter. A preset picked in Single mode is shown even if it's blocked.
  - A filter with nothing in it (no favorites yet, no custom presets) falls back to all presets that aren't blocked, and the settings row says so. If everything is blocked, shuffle uses all presets rather than showing nothing.
  - A preset is never both a favorite and blocked: adding it to one list takes it off the other.
  - **Getting presets onto the lists.** The **Last shown** row names the preset that was last on screen (from the status check, kept in `lastShownPreset`), with a favorite and a block button. The Favorites and Blocklist sheets also list all presets, with search, to add from.
  - Swift gets the preset list for the pickers by asking the page (`window.idlevizPresets()`), checked like the status reply.
- **Custom JS plugins:** users can drop `.js` files into the presets folder (subfolders allowed) and they appear in the preset list next to Butterchurn presets, tagged "custom".
  - Each file is an ES module with named exports `init(canvas)`, `frame(audio, time)` and `dispose()`, plus an optional `meta = { name }`. The same interface is used for plugins bundled in `web/visuals/*.js` (see `web/visuals/aurora.js` for an example). The full author guide is in `docs/custom-visualizer.md`.
  - `audio` is a plain object: `{ bands: Float32Array(64), bass, mid, treble, waveform: Float32Array(1024), rms }`, built from the Spotify-only frame (see the audio guarantee above). Bands are 0..1 and log-spaced (about 40 Hz to 16 kHz), already noise-floored and smoothed (fast attack, slow release); `bass`/`mid`/`treble` average bands 0-7, 8-29 and 30-63. The arrays are reused every frame. It is the only audio data a plugin ever sees.
  - Plugins draw only into the canvas they are given. Each `init` gets a fresh canvas, so a plugin picks its own context type (WebGL2 or 2D).
  - **Isolation: each plugin runs in its own sandboxed frame.** Bundled plugins in `web/visuals/` use the same frame, so there's only one way plugins are run.
    - The host page creates `<iframe sandbox="allow-scripts">` (no `allow-same-origin`, so it gets an opaque origin) loading `idleviz-app://app/plugin-host.html`, which holds the plugin's canvas.
    - The frame's own CSP: `default-src 'none'; script-src idleviz-app:; img-src data: blob:`. No network, no storage. The policy allows no stylesheets either, so the runner sets the canvas layout from script.
    - The frame's origin is opaque, so its runner script and the plugin itself are cross-origin requests. The scheme handler sends `Access-Control-Allow-Origin: *` with every response.
    - Checked in step 7d with a plugin that tries each of these: `fetch` to the web and to the app's own scheme, `window.parent.document`, `localStorage`, cookies and `getUserMedia` were all blocked, its origin was `null`, and it saw no `webkit.messageHandlers`.
    - Each frame, the host posts the audio object to the frame with `postMessage`. A small runner inside the frame copies it into reused arrays and calls the plugin's `frame(audio, time)`.
    - The frame can't reach the overlay DOM, the host's JS, or Swift.
  - **The page has no way to call into Swift.** Don't register any `WKScriptMessageHandler`. Swift talks to the page with `evaluateJavaScript`, and finds out how the page is doing by asking it (see "Loading and failure"). That way neither plugins nor custom presets can reach the app.
  - **Loading and failure:**
    - The runner imports the file with `import()` inside `try/catch` and calls `init`.
    - It wraps `frame` so that a throw, or a frame that takes over ~50 ms for several seconds in a row (3 s as built), reports a failure to the host. The host then removes the frame and moves on to the next preset. A plugin that hasn't started within 5 s counts as failed too.
    - A plugin fades in over the Butterchurn canvas for the blend time and fades out the same way. Butterchurn stops rendering while a plugin fully covers it.
    - **Status check.** About once a second while the window is open, Swift calls `evaluateJavaScript("window.idlevizStatus()")`. It returns the current preset and any new load failures (name + error message), which Swift shows in settings. Treat the reply as untrusted: check its shape and cap string lengths. Settings lists the failures, each with a Reveal button for its file.
    - WebKit may run the frame on the same thread as the host, so a plugin stuck in an endless loop freezes the whole page. If the status check gets no reply for ~3 s, Swift replaces the web view (see "Recovery" under Window) and marks the preset that was last reported as failed, so the new page doesn't hang on it again. The mark is dropped when the file changes.
  - The regex check in `tests/plugin-contract.test.js` is a lint for plugins in the repo, not a security boundary. The sandboxed frame and CSP are what enforce the limits.
  - The settings window shows a warning above the Presets rows that custom plugins and presets (see "Page security") are code and should only come from sources the user trusts.
  - Same license caveat as presets: imported plugins stay on the user's machine and are never bundled.
  - Settings window shows plugin load failures alongside preset failures, and includes plugins in favorites and the blocklist.
- Alternative considered: native **libprojectM** (open-source Milkdrop engine, C++/OpenGL). Larger ecosystem, but OpenGL is deprecated on macOS and it needs a native rendering layer under the window, so Butterchurn is the simpler start.

---

## File layout

```
IdleViz.xcodeproj                # app target (bundle ID com.xauno.IdleViz), synchronized with IdleViz/; web/ is an explicit folder so it's copied with its subfolders
Config/                          # IdleViz.xcconfig; Local.xcconfig (gitignored) holds DEVELOPMENT_TEAM
IdleViz.command                  # build Release, install to /Applications, open
Package.swift                    # IdleVizCore package (testable logic)
Sources/IdleVizCore/             # open rules, idle/skip rules, dismiss rules, like and skip keys, keep-awake + battery timing, fade times, permission states, brightness and overlay settings, Spotify parsing, content type, FFT/bands, delay detection
tests/IdleVizCoreTests/          # XCTest, runs in CI with swift test (shares tests/ with the JS suite; Package.swift names the path)
IdleViz/
├─ App/
│  ├─ AppDelegate.swift          # wires everything together at launch, open rules
│  ├─ MenuBarView.swift          # the popup: Settings…, hotkey row, error rows
│  ├─ MenuBarIcon.swift          # icon flash, yellow icon
│  ├─ Permissions.swift          # check Automation and audio, run the welcome window's prompts, open System Settings pages
│  ├─ SettingsWindow.swift       # separate settings UI (one scrolling page, per mockups.html)
│  ├─ LaunchAtLogin.swift        # the launch at login switch (SMAppService)
│  ├─ DisplayOptions.swift       # brightness and overlay switch → page
│  ├─ PresetControls.swift       # the preset rows in settings, and the favorites and blocklist sheets
│  ├─ PresetController.swift     # preset settings in UserDefaults → page; preset list and last-shown preset from the page
│  ├─ WelcomeWindow.swift        # explains and triggers the two permission prompts
│  ├─ Triggers.swift             # idle, hotkey, URL scheme, open rules
│  ├─ DismissWatcher.swift       # closes on input; hands the like and skip keys to the app instead
│  ├─ KeepAwake.swift            # display-sleep assertion + time limit
│  ├─ PowerSource.swift          # plugged in / battery, change notifications
│  ├─ WindowController.swift     # key-capable borderless window, fades in and out
│  ├─ PageView.swift             # WKWebView, nowPlaying and audio frames, status check and recovery, media capture denied
│  ├─ AppSchemeHandler.swift     # serves idleviz-app://app/ with the CSP header
│  ├─ SpotifyInfo.swift          # launch/quit tracking, notifications, AppleScript, artwork
│  ├─ SpotifyAudioTap.swift      # process tap on Spotify only, rebuilt when its processes or the output device change
│  ├─ AudioPump.swift            # while open: tap → IdleVizCore analysis → page, 60×/s; tap health check
│  ├─ AudioDelay.swift           # per-device delay, device lookups, Detect delay (mic, ~5 s), manual delay test
│  ├─ BeepTest.swift             # plays the manual delay test's beeps at exact host times
│  ├─ PresetLibrary.swift        # scan/watch custom preset folder, import, send list to page
│  ├─ FolderWatcher.swift        # FSEvents wrapper
│  ├─ MilkConverter.swift        # hidden page that converts .milk files
│  ├─ Info.plist                 # LSUIElement, NSAppleEventsUsageDescription, NSAudioCaptureUsageDescription, NSMicrophoneUsageDescription
│  └─ IdleViz.entitlements       # hardened runtime + apple-events + audio-input, no sandbox
├─ web/
│  ├─ index.html
│  ├─ plugin-host.html           # sandboxed frame that runs one custom JS plugin
│  ├─ plugin-runner.js           # inside the frame: loads the plugin, runs it on posted frames
│  ├─ plugin-runner-core.js      # DOM-free part of the runner (tested with Vitest)
│  ├─ plugin-frame.js            # host side: creates the frame, posts audio, handles failures
│  ├─ converter.html / converter.js  # hidden page for .milk conversion
│  ├─ overlay.css / overlay.js   # nowPlaying(), progress interpolation, states
│  ├─ overlay-state.js           # DOM-free overlay logic (tested with Vitest)
│  ├─ fonts/                     # Figtree + OFL license
│  ├─ visualizer.js              # Butterchurn wrapper: canvas, render loop, preset changes, context-loss rebuild
│  ├─ visualizer-state.js        # DOM-free visualizer logic (preset list, shuffle, timing, render size; tested with Vitest)
│  ├─ audio-frame.js             # DOM-free decoder for the packed audio frame from Swift (tested with Vitest)
│  ├─ vendor/                    # butterchurn.min.js + preset packs from npm, unchanged, with licenses and checksums
│  └─ visuals/                   # bundled plugin modules only (aurora.js); the plugin contract test loads every file here
└─ design/reference/             # Spotify TV app reference photo (gitignored, local only)
```

## Build order

Each step is one pull request. At the end of each step, update the README (Roadmap table, Features, Installation, Usage) and add tests for the step, as described in `CONTRIBUTING.md`.

1. **Open/close shell:** Xcode project + `IdleVizCore` package, signing, CI building the app with `xcodebuild`. Menu-bar app (Settings… + hotkey row, settings window stubbed), hotkey, fullscreen black window on the main display, dismiss on any input, focus returned to the previous app, the debug no-dismiss switch. Compare the feel side by side with a real macOS screensaver, and note how often activation is refused.
2. **Audio spike (throwaway):** prove the riskiest part before building on it. A process tap on Spotify gets real audio, Swift turns it into levels, and they reach the page ~60×/s and drive Butterchurn with one preset. Check `render({ audioLevels })`, the permission prompt, and CPU/GPU use. The spike code stays on a branch and isn't merged; this step's PR only writes the findings (what worked, what had to change) into `project.md`. Done: see "Audio spike findings (step 2)" and the `spike/audio-tap` branch.
3. **SpotifyInfo:** launch/quit tracking, notification + AppleScript (on its own thread, with a timeout), artwork download, content type and ad context, printed to the console. Confirm it never launches Spotify. Test songs, paused, podcasts, music ads, podcast ads, local files and Spotify Connect.
4. **Open rules + icon flash** wired to the hotkey.
5. **Overlay page** matched to the reference screenshots, served from `idleviz-app://` with the CSP, over a placeholder animated gradient (no audio needed yet), covering every state in the table plus the missing-artwork placeholder.
6. **Idle trigger**, with the skip rules (locked screen, another app keeping the display awake).
7. **Visualizer**, split into five PRs:
   - **7a.** Butterchurn in the page with bundled presets and fake audio. Move `aurora.js` to `web/visuals/`. Done.
   - **7b.** Real audio: process tap (with process-list changes), analysis in Swift, automatic gain, silence rules, web view recovery. Done.
   - **7c.** Preset controls (mode, shuffle, timing, blend, favorites, blocklist). Done.
   - **7d.** Custom preset folder: `.json` loading, folder watching, `.js` plugins in sandboxed frames (with the audio, CSP and status-check rules), Import/Open/Reload controls in settings, then `.milk` conversion with caching and failure handling. Done.
   - **7e.** Audio delay: per-device delay line, the settings slider, and **Detect delay** with the microphone. Test with built-in speakers, Bluetooth headphones and speakers, and AirPlay if available. Done. Tested with the built-in speakers, AirPlay and Bluetooth headphones; no Bluetooth speaker was available.
8. **Polish**, split into three PRs:
   - **8a.** Keep awake with its time limit setting (and the idle rule to wait for input after the limit), different times on battery, and the window fades. Done.
   - **8b.** Permissions: welcome window, yellow icon and popup error rows for missing permissions. Done. The macOS prompts themselves weren't triggered, since both permissions were already granted on the test Mac.
   - **8c.** Brightness slider, Show Spotify overlay switch and launch at login in settings. Done. The launch at login switch wasn't flipped during testing.

## Later

- Multi-display on the Mac: one window per `NSScreen`, visualizer on each (or mirrored), overlay on main only or all, and handle display changes while open (`NSApplication.didChangeScreenParametersNotification`). The Windows app has this already (W9 in [docs/windows.md](docs/windows.md)), with the settings and the page calls (`setRenderWidthCap`, `setOverlayRegions`) the Mac version should reuse.

## Requirements

- macOS 26 or later. The app is only built for the macOS it runs on (personal use), which avoids fallbacks for older versions.
- Spotify desktop app
- To build: full Xcode (not just the Command Line Tools) and a free Apple Development certificate (Xcode's personal team)
