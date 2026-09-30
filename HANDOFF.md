# Glass for Windows — handoff (2026-09-30, session 4)

The user asked for a Windows `.exe` of Glass, zipped to share with a friend. It should have **all
features exactly as on the Mac**, built from this Mac without live Windows testing. They work in
GSD phases (Research → Plan → Clear context → Execute → Verify) and hand off when context fills.
**Don't commit or push unless asked.**

**This repo:** `~/Projects/glass-windows`, remote `git@github.com:Ridou/glass-windows.git`, branch
`main`. The user wants the Windows port kept separate from the Mac repo
(`~/Projects/glass`, remote `Ridou/glass`), where the Mac source `glass.swift` lives. This
checkout was moved out of `~/Projects/glass/windows/` at the end of session 3. It was pushed to
GitHub, and the old copy is deleted.

## Milestones

| # | Milestone | Status | Evidence |
|---|-----------|--------|----------|
| 1 | Research and design | ✅ session 1 | See "Design decisions" below |
| 2 | Clean compile | ✅ session 2 | `dotnet build -c Release --no-incremental`: 0 errors, 0 warnings (rechecked in session 3) |
| 3 | WoW output identical to the Mac | ✅ session 2 | `tools/wowdiff/run.sh` prints `BYTE-IDENTICAL` (rechecked in session 3) |
| 4 | Publish; PE checked | ✅ session 2 | `python3 tools/pe.py dist/Glass/Glass.exe`: subsystem 2, RT_ICON x10, GROUP_ICON, VERSION, MANIFEST |
| 5 | Headless Wine prefix | ✅ session 2 | `~/Library/Caches/glass-wine/prefix`, graphics driver = `null` |
| 6 | Wine smoke test | ✅ session 3 | `--help`, `--list`, `--selftest` pass; every screenshot reviewed at 96 and 144 dpi; 3 layout bugs fixed |
| 7 | `README.txt` for the friend | ✅ session 3 | `README.txt`: ASCII, CRLF, 103 lines, published beside the exe |
| 8 | Zip plus ignore rules | ✅ session 3 | `dist/Glass-Windows.zip`, 60 MB: `Glass/Glass.exe`, `Glass/README.txt`. Ignore rules in `.gitignore` |
| 9 | Final self-review | ✅ session 3 | P/Invoke layouts, thread affinity, exit flushing; 2 small fixes |
| 10 | Move to `Ridou/glass-windows` | ✅ session 3 | First commit pushed to `main`; the old copy in the Mac repo is removed |
| 11 | Second review + end-to-end test | ✅ session 4 | Two independent reviews; 13 fixes; `tools/e2e/run.sh`: 46 ok, 0 failed, 4 skipped (focus; Wine can't judge it) |
| 12 | Help report for remote diagnosis | ✅ session 4 | Settings > Help > Copy Report; tray item; `--report`; e2e checks it |
| 13 | Live test on real Windows | ⏳ the friend | Needs Windows and two WoW clients. Ask for `Glass.log` back |

Zip SHA-256 of the exe inside: `f0a3c330b6ea0e6c35c77bfe6230686d43ec2232e1deaf0c46c159f238056290` (session 4, with the Help tab).
If you change any source, republish and rezip; the zip is only as fresh as its last build.

## Next

1. **Releases.** The friend downloads from
   `https://github.com/Ridou/glass-windows/releases/latest/download/Glass-Windows.zip`, linked
   from `README.md`. `README.txt` in the zip points at the Releases page for updates. v1.0.0 was
   published in session 4. To ship an update (ask first, since it's public):
   ```sh
   rm -rf dist && dotnet publish -c Release -o dist/Glass && (cd dist && zip -r -X -q Glass-Windows.zip Glass)
   gh release create vX.Y.Z dist/Glass-Windows.zip -R Ridou/glass-windows --title "Glass for Windows X.Y.Z" --notes "..."
   ```
   Keep the asset name `Glass-Windows.zip`, or the "latest" link breaks. Bump `<Version>` in
   `Glass.csproj` too.
2. **Optional, only with the user's OK:** a headless run of the *full app*, for example
   `tools/wine.sh 30 dist/Glass/Glass.exe --region 0,0,320,200`, then read `Glass.log`.
   - It would exercise startup, capture, the overlay, the tray and the hotkeys, which
     `--selftest` doesn't cover.
   - The null driver means nothing can appear on the Mac. The standing rule is still to ask
     before launching the full GUI.
3. The friend's live test on real Windows (milestone 13). Ask for a **report** back (Settings >
   Help > Copy Report, pasted into Discord, where it arrives as message.txt). What Wine
   could not prove, so watch for it in the log:
   - focus handed back to the played client after a click (`focus back ... FAILED`, or
     `had not taken focus after 400ms`);
   - capture on a real GPU: that frames arrive (`capture format BGRA32`), and that the mirror
     leaves itself out (`could not exclude` must not appear);
   - mixed DPI across two monitors, and the wheel with "Scroll inactive windows" on.

## What session 4 did (milestone 11)

The user asked to "review again and validate it all works perfect" before offering it.

- **Two independent read-only reviews** (input path; lifecycle and UI). What was fixed:
  1. **Click loop.** If the mirror sat on its own region, a forwarded click landed back on the
     mirror and forwarded again, forever. The first run opens it at 100,100, where party frames
     are on one monitor. Fix: the overlay and layered windows drop input tagged `GLSS`
     (`GetMessageExtraInfo`), and `OverlayForm.Covers` refuses a click whose target is under
     the mirror or header, with one tray warning.
  2. **Held key crossing the overlay edge.** Its repeats went to whoever the pointer was over
     now and the key-up was swallowed: a spell on the wrong character and a stuck key. Fix: a
     keypress belongs to whoever got its down (`Hooks.swallowed` / `passed`).
  3. **Cursor clipped** (WoW "Lock Cursor to Window"). `SetCursorPos` stopped at the played
     client's edge and the click landed there. Fix: `Forward.PinTo` confirms the cursor
     arrived; otherwise it refuses, logs and beeps. The README says what to turn off.
  4. Focus restore waits up to 400 ms (was 150) for the clicked client to activate.
  5. `ShowWindowAsync` and `SetWindowPos(...ASYNCWINDOWPOS)` replace `ShowWindow` and
     `BringWindowToTop`, which wait on the target's thread.
  6. `Wnd.IsOrdinary` skips layered and transparent (click-through) windows, such as GPU
     overlays.
  7. The number row is matched by scan code 0x02-0x0D, as the Mac does, with the VK as the
     fallback when the scan code is 0. The sideways wheel tilt (`WM_MOUSEHWHEEL`) is
     ignored.
  8. **Wheel routing:** only `MOUSE_POS` (2) follows the pointer. With "Scroll inactive windows"
     off, the overlay never receives the wheel at all, so the README now says to keep it on
     (it used to say "Glass copes").
  9. The position is saved only on `WM_EXITSIZEMOVE` (a user drag). A monitor sleeping no
     longer overwrites it, and `EnsureReachable` returns home when the monitor does.
  10. **Capture:** failed reads (lock screen, UAC) retry quietly with a fresh desktop DC every
      30 failures. `OnDrop` fires only if the display is gone. A restart counts as working only
      once `Capture.Frames` advances. This was an endless restart loop that grew the log.
  11. **Settings:** a failed save is retried. A busy file on load is retried five times. An
      unreadable one is kept as `settings.bad.json`. The snapshot is taken inside the write
      lock.
  12. `--reset/--role/--scale/--bar/--no-bar` are refused while Glass runs (they were silently
      overwritten). Hotkeys that fail to register show a tray warning. Reset Everything
      updates the live mirror.
  13. Deliberate exits log `stopped:`; `fatal:` is kept for crashes.
- **Help report (`src/Report.cs`), asked for by the user so the friend can send diagnostics
  over Discord.**
  - Settings has a new **Help** tab (5th; `TabNames` gained "Help"). Copy Report puts the report
    on the clipboard and in `Desktop\Glass-report.txt`. The tab also shows the full report text,
    so the friend can read what they send. Show Report File selects the file in Explorer, and
    Open Log Folder is there too. The tray has "Copy Report for Help", and `Glass.exe --report`
    covers the case where Glass won't run.
  - Contents:
    - A plain-English **"What looks wrong"** summary drawn from the live state and log patterns.
    - Versions, Windows build, elevation.
    - Displays and DPI.
    - Wheel routing, swapped buttons, keyboard layout.
    - Every WoW window: pid, rect, display, elevated, focus.
    - Glass's live state: mirror, capture frames, hook, modes, taken shortcuts.
    - `settings.json` verbatim, and the last 400 log lines (topped up from `Glass.old.log`).
  - Every warp click's log line now ends with a focus trace, for example `-> WowClassic 4812;
    WowClassic 4812 took focus in 12ms; focus back to WowClassic 5120 ok in 3ms`. Focus is the
    main thing Wine couldn't prove, so read this first in the friend's report.
- **Not changed, noted:**
  - Posted-message coordinates assume WoW is per-monitor DPI aware.
  - `WaitForRelease` waits for any press of that button.
  - Launching from a console ties Glass to that console.
  - The rounded corners (6 px) let a click on the very corner pixel reach the game underneath.
    The Mac is the same.
- **`tools/e2e/`** (new): `run.sh` publishes `E2E.exe` and drives the real
  `dist/Glass/Glass.exe` in the headless prefix.
  - Two stand-in clients, "Priest" (mirrored) and "Warrior" (played, under the overlay), log
    every input they receive.
  - It covers the click mapping, modifiers, corners, keys (posted move first; modifiers; held;
    edge crossing; non-row keys), the wheel, unlocked drag, hotkeys L and H, the cursor clip,
    the mirror over its own region, position saving, second launch, and `--reset` refused.
  - A control window proves Wine ignores `WS_EX_NOACTIVATE`, so focus checks print `skip`
    there. **On real Windows they count.**
  - Wine quirk: tray balloons appear top-left, over the Priest. The driver waits them out.

## What session 3 did (milestones 6–9)

- **`tools/wine.sh [SECONDS] PROGRAM ARGS…`** runs anything in the headless prefix.
  - It applies a time limit through perl, since macOS has no `timeout(1)`.
  - It silences MoltenVK's feature dump (`MVK_CONFIG_LOG_LEVEL=0`).
  - `winedbg.exe=d` makes a crash exit instead of hanging in the debugger.
- **The self-test renders Settings under Wine now.**
  - Wine's `WM_PRINT` ignores `PRF_CHILDREN`, so every Settings PNG was blank.
    `SelfTest.Render` prints each child window itself and clips it to its parent. It is used by
    `SettingsForm.Snapshots()`.
  - `--selftest` also writes `settings-<tab>.txt`: every shown control with its bounds, and for
    fixed-size labels the width its text `needs`. Use it to spot cut-off text.
- **Three real layout bugs, found by the smoke test and fixed.** These are not Wine quirks.
  1. **WoW tab list.** It was anchored on all four sides while its page was still at the default
     200×100, so it grew to about 1380×1020. The command column and Copy buttons ended up off the
     right edge, and Copy Ticked / Copy as Macro were covered.
     - Fix: the anchor is removed. The window has a fixed size.
  2. **WoW command column.** A `Label` wraps at the space after `/run`, which left "/run" alone on
     the first line.
     - Fix: `SettingsForm.OneLineLabel` draws a single line with an end ellipsis, as the Mac
       truncates (`byTruncatingTail`).
  3. **Overlay tab at 150%.** Both TrackBars came out 104 px tall and covered the rows below:
     "Locked — …" showed as "L" and the size hint as "ptures.". Setting `Size` while `AutoSize` was
     still on (the TrackBar default) swapped in the preferred height, which was then scaled.
     - Fix: `AutoSize = false` now comes first in the initializer.
     - Now 390×45 at 144 dpi and 260×30 at 96. Every label fits at both scales.
- **Review (milestone 9).**
  - Struct sizes are right for x64: INPUT 40, MSG 48, KBDLLHOOKSTRUCT 24, RAWINPUTHEADER 24,
    RAWKEYBOARD 16, UPDATELAYEREDWINDOWINFO 80, MONITORINFO 40, BLENDFUNCTION 4.
  - `LoWord`, `HiWord` and `MakeLParam` are signed, so they are correct on a monitor left of the
    primary.
  - Threading matches the design. The hook only reads `Hooks.State`, and all worker threads are
    background threads.
  - Fixes:
    - The `UnhandledException` handler now calls `Log.Flush()`, because the process dies right
      after it.
    - `SystemEvents.SessionEnding` flushes the settings and the log.
    - The Log watchdog comment no longer claims the hook runs on the UI thread.
- **Parity checked against the Mac.**
  - Neither build has a modifier-drag while locked. The Mac README says so; the "⌘-drag works
    either way" line in `glass.swift`'s header comment is stale.
  - The Mac WoW tab has no Install button either: `--install-addon` is command-line only on both.
- **Smoke test results.**
  - The log is clean. The only warning is the expected Wine one: `SetWindowDisplayAffinity` can't
    exclude the overlay from capture under Wine.
  - Settings at 150% on a 1080p laptop would be about 30 px taller than the work area. Only empty
    margin goes under the taskbar and the buttons stay visible, so it's left alone.

## Wine notes (learned the hard way)

- Use only `tools/wine.sh`; the prefix must stay headless (null driver, `winemac.drv` disabled),
  because the user plays WoW while you test.
- Run **`dist/Glass/Glass.exe`**, never `bin/Release/…/Glass.exe`. The build output lacks
  `System.Runtime.dll` and crashes. Publishing takes about 8 s.
- **Changing dpi needs a fresh wineserver**, or controls scale while fonts don't:
  ```sh
  W="$HOME/Library/Caches/glass-wine/Wine Devel.app/Contents/Resources/wine/bin"
  export WINEPREFIX="$HOME/Library/Caches/glass-wine/prefix"
  "$W/wineserver" -k; tools/wine.sh 60 reg add 'HKCU\Control Panel\Desktop' /v LogPixels /t REG_DWORD /d 144 /f
  "$W/wineserver" -w; tools/wine.sh 300 dist/Glass/Glass.exe --selftest 'C:\glass-selftest-144'
  ```
  Set it back to 96 the same way afterwards. The prefix is at 96 now.
- The null driver's screen is 1024×768, so at 144 dpi the Settings window is clamped. Judge the
  layout from `settings-*.txt`, not from the cut-off right edge of the PNG.
- Wine has no Segoe UI, Consolas or Segoe MDL2 Assets. Text metrics are approximate, and the header
  shows its fallback glyphs (⚙ as a box, L/U, …). Real Windows 10/11 has MDL2 (E713 gear, E72E lock,
  E785 unlock, E712 more).
- The self-test output: `$WINEPREFIX/drive_c/glass-selftest/`. The log:
  `$WINEPREFIX/drive_c/users/$USER/AppData/Local/Glass/Glass.log`.

## Toolchain

```sh
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet build -c Release            # 0 warnings expected
rm -rf dist && dotnet publish -c Release -o dist/Glass    # Glass.exe + README.txt
python3 tools/pe.py dist/Glass/Glass.exe
./tools/wowdiff/run.sh                            # BYTE-IDENTICAL
tools/wine.sh 300 dist/Glass/Glass.exe --selftest 'C:\glass-selftest'   # ALL PASSED
./tools/e2e/run.sh                                # ALL PASSED (focus checks skip under Wine)
cd dist && rm -f Glass-Windows.zip && zip -r -X -q Glass-Windows.zip Glass
```

- Icon: `swiftc -O makeicon-win.swift -o .makeicon-win && ./.makeicon-win Glass.ico` (10 sizes).
- Size: WinForms can't be trimmed, so the self-contained single file carries the whole desktop
  runtime. Native libraries self-extract to `%TEMP%\.net\Glass\…` on first run. That's normal
  .NET behaviour.
- `tools/wowdiff/`: `harness.swift` is the real WoW code cut from `glass.swift`, with a stub
  `Saved`. `cs/` is the real `WowSettings.cs` with stubs; it uses Swift's defaults for the two
  "(Mac)" settings. If `glass.swift`'s WoW section changes, regenerate `harness.swift` by cutting
  three spans:
  - `enum WoWRole` … up to `extension Saved {`
  - `/// The ticked settings, in the shape Core.lua reads` … up to `/// Install or update`
  - `/// Just the commands for one setting` … up to `// MARK: - Settings window`
- `addon/Core.lua` and `GlassSetup.toc` are cut byte for byte from `glass.swift` and embedded as
  resources. They are newer than `addon-archive/` because they add `RestartGx`. The addon was
  reinstated in `glass.swift` on 2026-09-19, whatever `.bb/AGENTS.md` still says.

## Design decisions (settled; don't re-derive)

- **Stack:** C# WinForms on .NET 8, a single-file self-contained win-x64 build. PerMonitorV2 is
  set in code. Every coordinate is in physical pixels.
- **Threads:**
  - UI: forms, tray, hotkeys, the 30 ms hover poll.
  - `glass.hooks`: the WH_KEYBOARD_LL hook, a Raw Input watchdog that reinstalls the hook if raw
    input saw a key the hook missed (backing off 5 s → 5 min), and a lag ping.
  - `glass.forward`: a serial worker for every warp, click and key. It calls `PeekMessage` first
    so `AttachThreadInput` works.
  - Capture: a threadpool timer doing a BitBlt into a DIB, then `InvalidateRect(LiveHandle)`.
  - `glass.log`: the only thread that touches the log file or the console.
- **Capture:** BitBlt of the desktop DC. `WDA_EXCLUDEFROMCAPTURE` on the overlay, header, picker
  and boost windows (Windows 10 2004+) keeps Glass out of its own mirror. The Settings window is
  deliberately *not* excluded, so the friend can screenshot it.
- **Clicks:** handled in `OverlayForm.WndProc`.
  - The job waits for the physical button to be released (checking `SM_SWAPBUTTON`), then calls
    `SetCursorPos`.
  - It re-pins the cursor before each `SendInput` down and up, which are tagged `GLSS`. Each
    pin is checked (`PinTo`), and a clipped cursor refuses the input.
  - It then settles, warps back, and restores focus. Restore waits up to 400 ms for the clicked
    client to take focus first.
  - Focus is taken with `AttachThreadInput` on the worker's own thread.
- **Keys:** the hook claims the number row while the pointer is over a visible, locked overlay.
  - Glass's own shortcuts win.
  - Autorepeats and matching key-ups are swallowed.
  - Modifiers come from `GetAsyncKeyState`.
  - Post mode is the default (the Mac's `keysViaPid`): warp, wait `HoverMs` (35), re-pin, post
    `WM_MOUSEMOVE`, then the modifier and key messages.
- **Wheel:** one notch per event, like the Mac. If `SPI_GETMOUSEWHEELROUTING` says the wheel goes
  to the focused window, the wheel is posted to the hovered client instead.
- **Hotkeys:** `RegisterHotKey` on a hidden top-level window with caption `Glass.Main.7c1e0b52`.
  - They're suspended while a shortcut is being recorded.
  - Esc is grabbed while the picker is up.
  - The single-instance mutex is `Local\Glass.7c1e0b52`.
  - A second launch posts `WM_SHOW_SETTINGS`; with `--settings TAB`, the tab is 1-based in
    wParam.
- **Settings:** `%APPDATA%\Glass\settings.json`, with writes debounced 250 ms and done outside
  the lock.
- **Log:** `%LOCALAPPDATA%\Glass\Glass.log`, rotated to `Glass.old.log` past 5 MB. `Log.Write`
  only enqueues.
- **Layered windows:** the header and picker use `LayeredSurface`, a DIB drawn with
  UpdateLayeredWindow(Indirect). The overlay uses LWA_ALPHA plus a round-rect region plus
  StretchBlt in OnPaint.
- **Administrator:** `Forward.CheckReachable` detects a target running elevated while Glass isn't.
  It logs and shows a one-time tray balloon.
- **`--selftest DIR`:** draws every window to PNG without showing it, and writes the layout dumps
  and the WoW text. No hotkeys, no capture. It does install the keyboard hook briefly.
- **Research:**
  - LL hooks are silently removed on timeout (at most 1 s on Windows 10 1709+).
  - AltSnap watches for hook removal with Raw Input.
  - HotkeyNet posts keys to background WoW.
  - Blizzard bans broadcasting, not multiboxing.
  - Exclusive fullscreen can't be captured with BitBlt.

## Deliberate differences from the Mac build (tell the user)

- **Defaults:**
  - GPU boost is off. Windows doesn't park the GPU the way macOS does.
  - The two "(Mac)" graphics settings (`shadowLow`, `secondaryLightingFair`) are off.
- **Clicks** fire on button *release*, not on press.
- There's no CGAssociateMouseAndMouseCursorPosition on Windows, so a client in mouse-look may
  see the camera flick for a frame. Post modes avoid that.
- **Input path:**
  - Clicks and keys re-pin the cursor before they're delivered.
  - Post-mode keys are preceded by a posted `WM_MOUSEMOVE`.
  - The wheel is posted when "Scroll inactive windows" is off.
- **Additions:**
  - A Settings toggle, "Send clicks without moving the cursor" (the Mac's `--pid`, made
    persistent).
  - `--hover MS` and `--help`.
  - Tray items "Open Log" and "Reinstall Keyboard Hook".
  - A first-run tray balloon.
  - A warning when a target runs as administrator.
  - Log rotation.
  - A second launch with `--settings TAB` opens that tab.
  - A Help tab and help report (Copy Report, tray item, `--report`) for remote diagnosis.
  - The header drops below the overlay if there's no room above it.
  - WoW setting details are shown under each checkbox.
  - The text is worded for any player rather than "Warrior/Priest".
  - The tray menu is right-click (the Windows convention); double-click opens Settings.
- **Wording:** "Alt-click" instead of ⌥-click, and `--install-addon` prints full paths.
- **Capture:** the overlay and header are invisible to all screen capture, not only Glass's own.
  That includes screenshots, OBS and Discord.

## Files

- `src/Native.cs`: all P/Invoke.
- `src/Saved.cs`: the settings store.
- `src/Log.cs`: the log and the UI watchdog.
- `src/Screens.cs`: `Display`, `Screens`, and `Wnd` (window at a point, focus, ProcessPath,
  IsElevated).
- `src/Capture.cs`: BitBlt capture.
- `src/Shortcuts.cs`: HeaderMode, Shortcut, Command, Hotkeys.
- `src/Forward.cs`: the worker, warp, clicks, wheel, keys, CheckReachable.
- `src/Hooks.cs`: the hook thread, Claim, RawWatch.
- `src/WowSettings.cs`: the settings table, console commands, macro chunks, addon config and
  install.
- `src/Layered.cs`: LayeredSurface, LayeredForm, and `Look` (colours, round rects, MDL2 icon
  font).
- Windows: `src/Picker.cs`, `Header.cs`, `Overlay.cs`, `GpuBoost.cs`, `Tray.cs`,
  `SettingsForm.cs` (which includes `OneLineLabel`).
- `src/App.cs`, `src/Program.cs`, `src/SelfTest.cs` (which includes `Render` and `Layout`),
  `src/Report.cs` (the help report).
- `addon/`: Core.lua and GlassSetup.toc, from glass.swift.
- `README.md`: the repo's front page.
- `README.txt`: for the friend. It is ASCII with CRLF line endings; keep it that way (after an
  edit, run `perl -pi -e 's/\r?\n/\r\n/' README.txt`).
- `tools/`: `wine.sh`, `pe.py`, `wowdiff/`, `e2e/`.
- `Glass.csproj`, `app.manifest`, `Glass.ico`, `makeicon-win.swift`, `.gitignore`.
