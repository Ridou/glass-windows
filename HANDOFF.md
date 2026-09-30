# Glass for Windows — handoff (2026-09-30, session 3)

The user asked for a Windows `.exe` of Glass, zipped to share with a friend. It should have **all
features exactly as on the Mac**, built from this Mac without live Windows testing. They work in
GSD phases (Research → Plan → Clear context → Execute → Verify) and hand off when context fills.
**Don't commit or push unless asked.**

**This repo:** `~/Projects/glass-windows`, remote `git@github.com:Ridou/glass-windows.git`, branch
`main`. The user wants the Windows port kept separate from the Mac repo
(`~/Projects/glass`, remote `Ridou/glass`), where the Mac source `glass.swift` lives. This
checkout was copied from `~/Projects/glass/windows/` at the end of session 3. It builds and
passes every check here. **Nothing is committed or pushed yet**, and the old copy in the Mac
repo has not been removed.

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
| 10 | Move to `Ridou/glass-windows` | 🟡 session 3 | Copied and verified here; commit, push and removing the old copy await the user's go |
| 11 | Live test on real Windows | ⏳ the friend | Needs Windows and two WoW clients. Ask for `Glass.log` back |

Zip SHA-256 of the exe inside: `156b5c42a0cd4d047071a6538e4eef5cbd814029bec0f271dfddf12e3fa041d5`.
If you change any source, republish and rezip; the zip is only as fresh as its last build.

## Next

1. **With the user's go:** make the first commit here and push `main`. The files are: sources,
   `README.md`, `README.txt`, `HANDOFF.md`, `tools/`, `addon/`, `Glass.ico` (needed to build) and
   `.gitignore`. Build outputs are ignored.
2. **With the user's go:** delete `~/Projects/glass/windows/`. The Mac repo's `git status` is then
   clean again. Its `.gitignore` was never changed.
3. Optionally, attach `dist/Glass-Windows.zip` to a GitHub release rather than committing it. This
   is outward-facing, so ask first.
4. **Optional, only with the user's OK:** a headless run of the *full app*, for example
   `tools/wine.sh 30 dist/Glass/Glass.exe --region 0,0,320,200`, then read `Glass.log`.
   - It would exercise startup, capture, the overlay, the tray and the hotkeys, which
     `--selftest` doesn't cover.
   - The null driver means nothing can appear on the Mac. The standing rule is still to ask
     before launching the full GUI.
5. The friend's live test on real Windows (milestone 11). Ask for `Glass.log` back.

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
  - It re-pins the cursor before each `SendInput` down and up, which are tagged `GLSS`.
  - It then settles, warps back, and restores focus. Restore waits up to 150 ms for the clicked
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
- `src/App.cs`, `src/Program.cs`, `src/SelfTest.cs` (which includes `Render` and `Layout`).
- `addon/`: Core.lua and GlassSetup.toc, from glass.swift.
- `README.md`: the repo's front page.
- `README.txt`: for the friend. It is ASCII with CRLF line endings; keep it that way (after an
  edit, run `perl -pi -e 's/\r?\n/\r\n/' README.txt`).
- `tools/`: `wine.sh`, `pe.py`, `wowdiff/`.
- `Glass.csproj`, `app.manifest`, `Glass.ico`, `makeicon-win.swift`, `.gitignore`.
