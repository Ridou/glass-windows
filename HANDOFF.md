# Glass for Windows — handoff (2026-10-01, session 9)

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
| 3 | Console output identical to the Mac | ✅ session 2 | `tools/cmddiff/run.sh` prints `BYTE-IDENTICAL` (rechecked in session 3) |
| 4 | Publish; PE checked | ✅ session 2 | `python3 tools/pe.py dist/Glass/Glass.exe`: subsystem 2, RT_ICON x10, GROUP_ICON, VERSION, MANIFEST |
| 5 | Headless Wine prefix | ✅ session 2 | `~/Library/Caches/glass-wine/prefix`, graphics driver = `null` |
| 6 | Wine smoke test | ✅ session 3 | `--help`, `--list`, `--selftest` pass; every screenshot reviewed at 96 and 144 dpi; 3 layout bugs fixed |
| 7 | `README.txt` for the friend | ✅ session 3 | `README.txt`: ASCII, CRLF, 103 lines, published beside the exe |
| 8 | Zip plus ignore rules | ✅ session 3 | `dist/Glass-Windows.zip`, 60 MB: `Glass/Glass.exe`, `Glass/README.txt`. Ignore rules in `.gitignore` |
| 9 | Final self-review | ✅ session 3 | P/Invoke layouts, thread affinity, exit flushing; 2 small fixes |
| 10 | Move to `Ridou/glass-windows` | ✅ session 3 | First commit pushed to `main`; the old copy in the Mac repo is removed |
| 11 | Second review + end-to-end test | ✅ session 4 | Two independent reviews; 13 fixes; `tools/e2e/run.sh`: 46 ok, 0 failed, 4 skipped (focus; Wine can't judge it) |
| 12 | Help report for remote diagnosis | ✅ session 4 | Settings > Help > Copy Report; tray item; `--report`; e2e checks it |
| 13 | First live report: one monitor | ✅ session 5 | Friend's report: one 3440x1440 monitor, two WowB clients stacked, Alt+Tab. Window mode built (1.1.0) |
| 14 | Live test of window mode | ⏳ the friend | Needs Windows and two game clients. Ask for `Glass.log` back |
| 15 | Ship the look | ✅ session 9 | 1.3.0 tagged; the theme had been on `main` unreleased since `a1cf9bd` |
| 16 | Polish the look to match the Mac | ✅ session 9 | 1.3.1; three real gaps fixed, four claims disproved by measuring |

Zip SHA-256 of the exe inside: `f0a3c330b6ea0e6c35c77bfe6230686d43ec2232e1deaf0c46c159f238056290` (session 4, with the Help tab).
If you change any source, republish and rezip; the zip is only as fresh as its last build.

## Next

1. **Releases are built by GitHub Actions** (`.github/workflows/release.yml`) and published only
   if the self-test passes. Steps:
   1. Bump `<Version>` and `<FileVersion>` in `Glass.csproj` and the version in `app.manifest`.
   2. Commit and push, then:
      ```sh
      git tag -a vX.Y.Z -m "What changed (these become the release notes)"
      git push origin vX.Y.Z
      ```
   3. The workflow checks that the tag matches `<Version>`, builds on `windows-latest`, runs
      `--selftest`, tries the e2e on real Windows (informational), then publishes the release
      with `Glass-Windows.zip`. Test output is attached as a workflow artifact. Ask the user
      before tagging, since the repo is public.

   Other ways to run and check it:
   - **Without releasing:** `gh workflow run Release -R Ridou/glass-windows`, then `gh run watch`.
   - **Download link:** `README.md`'s big badge goes to `releases/latest/download/Glass-Windows.zip`.
     The name `Glass-Windows.zip` must never change.
   - **Update check:** Glass reads `api.github.com/…/releases/latest` 5 s after starting
     (`Updates.cs`) and shows a balloon if newer. Settings > Help shows the version, with
     Check for Updates and Download the Latest. The e2e passes `--no-update-check`.
   - v1.0.0 to v1.2.0 were uploaded by hand from this Mac; later releases come from the workflow.
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

## What session 9 did, part two (1.3.1: polish measured against the Mac)

**Lesson: measure the look, do not eyeball it.** Of seven differences written down from a
side-by-side read of the two renders, sampling the pixels disproved four. Converting both PNGs
with `sips -s format bmp` and reading them with a twenty-line BMP reader settled every one of
them in seconds. Do that before changing a colour.

| Claimed from the screenshots | Measured | Verdict |
|---|---|---|
| Windows marble is washed out | Mac content lum 47.5, Windows 43.8 | **False** -- Windows is darker |
| Body and hint text too dim | intro fg rgb(95,94,94) vs the Mac's rgb(235,230,217); contrast 2.98 vs 10.10 | **True**, and the worst of them |
| No alternating row bands | flat: banded row rgb(47,39,29) = plain row | **True** |
| Inset border too faint | both call the same `DrawInset` from the window chrome | **False** -- a 1x vs 2x artifact |
| Plates carry no icons | the Mac passes two SF Symbols, Windows passes none | **True** |
| Selected side tab barely differs | Windows delta +21.8 lum, the Mac's **-3.9** | **False** -- Windows separates them *better* |
| Heading undersized | Windows 4.99% of window height, the Mac 3.00% | **False** -- Windows is larger |

### What was actually fixed

1. **The text colours were never moved onto the palette.** `SettingsForm` still held
   `Secondary = (96,96,96)` and `Tertiary = (128,128,128)` from before the look landed, so every
   explanation on every page drew in flat grey at about a third of the Mac's contrast. Both
   constants are gone; each call site now names the role it wants -- `Theme.Body` for page
   prose, `Theme.Hint` for the quieter line under a control, `Theme.Yellow` for the live status
   lines (the Mac uses `WoW.yellow` for both the mirror and overlay status).
   - Intro line contrast **2.98 -> 6.82**, colour now rgb(230,225,213) against the Mac's
     rgb(235,230,217). The rest of the gap is 1x antialiasing against the Mac's 2x, not colour.
   - Found on the way: `presetLabels[n].Font = new Font("Consolas", …)` did nothing at all --
     `ThemeLabel` paints with `TextFont`, never `Font`. It is `Theme.Narrow` now, as the Mac is.
2. **Row bands.** `ThemeBand` was fully written and never used. It could not simply go *behind*
   a row either: every themed control paints its own stretch of marble through
   `Backdrop.Paint`, so a band underneath is painted straight over by each label and button.
   - `ThemeBand` is a `Panel` now and the row's controls go **inside** it, and `Backdrop.Paint`
     re-applies the tint for anything nested in one. So the row reads as a single band however
     many controls stand on it.
   - It must never be `Enabled = false` again: that would disable the whole row.
   - Banded row is now rgb(61,48,29) -- **pixel-identical to the Mac**.
3. **Plate icons.** The Mac's mirror choice carries `display` (blue) and `macwindow` (green).
   Windows has no SF Symbols, so `PlateGlyph` and `ThemeArt.DrawTile` draw the two to the same
   reading, on the same ground and bevel as a macro icon. Nested figures on the default
   alternate fill give a window frame with a solid title bar in one path the gradient crosses.
   - **Gotcha:** measure a plate's label with `Theme.Measure`, which uses the same
     `StringFormat` the text is drawn with. Measuring with `GenericTypographic` under-measures
     and the label is silently trimmed to an ellipsis ("The game windo").

Verified: build 0/0, `--selftest ALL PASSED`, no `CLIPPED` in any `settings-*.txt`, and
`tools/e2e/run.sh` **screen, window and window-front all passed** -- which matters here, because
the preset buttons now sit inside a container that did not exist before.

**None of this ports back to the Mac**; it is Windows catching up to a look the Mac already had.

### Working on the look from a Mac

Both sides render headless, with no Windows machine and no game running:

    /Applications/Glass.app/Contents/MacOS/Glass --snapshot DIR     # the reference
    tools/wine.sh 240 dist/Glass/Glass.exe --selftest DIR           # every settings page
    tools/wine.sh 120 dist/Glass/Glass.exe --theme-sample DIR       # just the primitives

## What session 9 did (1.3.0: release the look that was never released)

**The look was finished in session 8 and then never shipped.** The friend downloaded
`releases/latest/download/Glass-Windows.zip`, saw plain WinForms, and asked why it looked basic.

- `v1.2.1` points at `0a645a8` (Sep 30, 23:25). The theme landed in `a1cf9bd` (Oct 1, 02:21),
  about three hours later, and no tag was ever cut after it. `git ls-tree v1.2.1 src/` has **no**
  `Theme*.cs` at all, so the published zip could only ever look plain.
- **The Store submission is fine.** Both MSIX runs that produced an artifact (`35edc1d`,
  `b6fcac5`) contain all four `Theme*.cs` files, so the package in certification already has the
  new look. Only the GitHub zip was stale.
- **Version collision, now fixed.** Themed `main` still said `1.2.1`, the same version as the
  unthemed zip: two different binaries claiming one version. Bumped to **1.3.0** in
  `Glass.csproj` (`Version`, `FileVersion`) and `app.manifest`.
  - After any look change, check `grep -rn '1\.2\.1' Glass.csproj app.manifest` comes back
    clean, and that `python3 tools/pe.py dist/Glass/Glass.exe` reports the new `ProductVersion`.
- Verified before tagging: `dotnet build -c Release --no-incremental` 0 warnings 0 errors;
  `--selftest` under Wine `ALL PASSED`; PE `ProductVersion 1.3.0`.

### The look, measured against the Mac

Rendered both sides and compared: the Mac with `/Applications/Glass.app/Contents/MacOS/Glass
--snapshot DIR`, Windows with `tools/wine.sh 240 dist/Glass/Glass.exe --selftest DIR` and
`--theme-sample DIR`. Both run headless, so **the whole look can be worked on from the Mac**
with no Windows machine and no game running.

The frame, marble, side tabs, red and grey buttons and gold type all carried over and are
recognisably the same product. What is still behind the Mac, in the order it is worth fixing:

1. **Contrast.** Body and hint text read much dimmer than the Mac's; "Record a region for each
   group size" nearly vanishes into the marble. This is most of why it still reads as cheaper.
2. **The marble is washed out** — flatter and lighter than the Mac's darker, richer stone.
3. **No row bands.** `ThemeBand` exists but the Regions preset list does not use it; the Mac
   tints alternating rows.
4. **The inset border is faint** next to the Mac's recessed bronze rim.
5. **Plate buttons have no icons.** The Mac's "The screen" / "The game window" carry monitor
   glyphs; the Windows plates are bare text.
6. **Macro icons are flat**, and `dim: true` is barely distinguishable from `dim: false`, so a
   selected side tab does not stand out the way it does on the Mac.
7. **Heading scale** — "Regions" is smaller relative to the window than the Mac's.

Parity items that are *not* look bugs:
- The tray menu is plain on **both** sides (the Mac's `StatusMenu` is an ordinary `NSMenu`), so
  leave it alone.
- The region picker is unthemed on both.
- Three `MessageBox.Show` calls remain system dialogs (`App.cs` x2, `Program.cs`). The Mac
  replaced those with a themed popup (`WoW.popup`, `glass.swift:3002`). This is a real gap.

**Nothing here ports back to the Mac** — it is Windows catching up to a look the Mac already has.

## What session 8 did (1.2.1: downloads, updates, CI on real Windows)

- **Download.** `README.md` opens with a large shields.io badge showing the newest version,
  linking to `releases/latest/download/Glass-Windows.zip`. The Mac README links it too.
- **Updates.** `Updates.cs` asks GitHub 5 s after start; a tray balloon appears if a newer
  version is out. Help shows "You have X", with Check for Updates and Download the Latest,
  and the report flags an outdated copy.
- **CI.** `.github/workflows/release.yml` runs on `windows-latest`:
  - A `v*` tag must match `<Version>`, and the release is published from the tag's notes
    only after `--selftest` passes.
  - Running it by hand builds and tests without publishing.
  - It also runs the **e2e on real Windows**, where no-activate is honoured, so the focus
    checks count. v1.2.1 is the first release built this way, and all three scenarios pass
    there.
- **Found by real Windows: focus after a shift-click.** Releasing Shift goes to the clicked
  client, which then owns "last input", so `SetForegroundWindow` and `AttachThreadInput`
  were both refused.
  - `Wnd.Focus` now tries `SetForegroundWindow`, then `AttachThreadInput`, then
    `SwitchToThisWindow`, then an **Alt tap**. The Alt tap is the documented unlock and is
    what worked; it waits up to 1 s for Shift to be released first, because Alt+Shift can
    switch layouts.
  - Each method gets 60 ms to settle before the next is tried, and the log says `via …`.
  - Trade-off: when the Alt tap is used, the clicked client receives a stray Alt
    `SYSKEYDOWN`.
- **Safety.** A brought-forward click refuses, with a beep, unless the target is at the spot
  (after raising it). The Mac has the same rule.

## What session 7 did (1.2.0, and the Mac tested live)

- **The setting is now a plain choice.** "What the mirror shows" is "The game window" or "The
  screen"; Auto is gone.
  - **Windows defaults to the game window.** The Mac defaults to the screen, as it always
    worked.
  - An old saved "auto" reads as the platform's default. `--mirror window|screen`.
- **The Mac was tested live on the user's Mac** (two displays, macOS 27), with
  `~/Projects/glass/e2e/run.sh old`. It uses stand-ins, the real Glass, real events and
  screenshots.
  - Previous release c100083, screen: 17 ok, 4 failed (focus never came back after a click).
  - New build, screen: 24 ok, 0 failed.
  - New build, window: 26 ok, 0 failed. **Window capture is proven:** the mirror showed the
    covered Priest.
- **What the Mac run found and fixed** (details in the Mac repo's `.bb/AGENTS.md`):
  - **Focus.** Cooperative activation ignores `activate()`, so Glass now falls back to
    SkyLight's `_SLPSSetFrontProcessWithOptions`. `frontmostPID()` uses `GetFrontProcess`.
  - **Keys.** The key decision uses `event.location`.
  - **Clicks.** Window-mode clicks fire on release, after `stepAside`.
- **Windows parity check for those:**
  - Focus on Windows already uses `AttachThreadInput`, and `GetForegroundWindow` is always
    fresh.
  - The LL hook runs at once on its own thread, so `GetCursorPos` there is the press-time
    position.
  - Windows clicks already fire on release.
  - So nothing needed porting back.
- **Harness lesson:** an early Mac run clicked and typed into a Brave window that happened to
  be in front. The Mac driver now stops before any input unless one of its own windows is on
  top at that spot. Keep that rule in any live harness.

## What session 6 did (1.1.1, and Mac parity)

- **Standing rule from the user:** every change to Glass for Windows should be considered for
  Glass for Mac (`~/Projects/glass/glass.swift`), and vice versa. It is recorded in the Mac
  repo's `.bb/AGENTS.md` and in memory.
- **Windows 1.1.1.** In "Bring it forward" mode, `Forward.StepAside` makes the mirror and header
  click-through (layered and transparent) for the moment of the click. The click then passes
  to the client brought forward beneath them, even where the mirror covers the spot.
  - `Overlay.Covers` no longer refuses anything in window mode, in either click mode.
  - A Wine control run (`E2E.exe probe-transparent`) confirmed Wine honours a click-through
    style set after creation.
  - `window-front` now runs with the mirror over the region.
  - A click whose spot is held by another window logs `click …: X is over the spot, not Y`.
- **Mac port of 1.1.0** (in the Mac repo, built but untested live):
  - Window mode: `mirrorsWindows()`, `bindWindow`, and `SCContentFilter(desktopIndependentWindow:)`
    with a window-local `sourceRect`; `Saved.mirrorMode` and `boundApp`; `--mirror`.
  - Clicks and the wheel on a covered window bring its app forward (`activateAndWait`),
    because the Mac has no posted mouse events. The mirror steps aside with
    `ignoresMouseEvents`. Keys go to `mirrorPID` via `postToPid`.
  - An ✕ quit badge appears when unlocked.
  - The App Translocation warning is the Mac's equivalent of the zip warning.
  - Ported fixes: a held key belongs to its first owner (`passedKeys`), Glass's own clicks are
    tagged and ignored, and a mirror over its own region is refused in screen mode.
  - Settings > Regions: "What the mirror shows".
  - The Help/report tab is **not** ported yet; it is the remaining parity item.
  - Built ad-hoc here: this session has no Developer ID (`security find-identity`: 0), so it
    must not be installed from here, or macOS drops the TCC grants.

## What session 5 did (1.1.0: one monitor)

The friend's first report (Glass 1.0.0), the run and the changes it led to:
- **The setup:** one 3440x1440 monitor, two `WowB.exe` (`_classic_beta_`) clients, both full
  screen at 0,0; he Alt+Tabs between them. He ran Glass from inside the zip
  (`Temp\Rar$EX…`).
- **What went wrong:** screen capture showed only the client in front, and clicks landed on it
  (`focus stayed on WowB 10212`).
- **"No way to quit":** the tray is hidden in the overflow, and the Settings X only hides it.
- **The report fix:** "Glass was briefly slow to respond" was a false positive, because
  "stalled" matched "installed". Patterns now match case-sensitively, and "thread stalled" is
  the pattern.
- **Window mode** (`App.WindowMode`: Auto = one monitor; Settings > Regions > "What the mirror
  shows"; `--mirror screen|window|auto` for one run):
  - **Binding.** `App.Bind` ties the region to a window: the one under the region when
    picked, or `--window`. Otherwise the current one is kept while it lives; if it has
    closed, it rebinds to a window of `Saved.BoundExe` covering the region that is *not* in
    front. `App.Target` holds the window, and `HookState.Target` carries it to the hook.
  - **Capture.** `WindowCapture` (`src/WindowCapture.cs`) uses Windows.Graphics.Capture
    through CsWinRT: the TFM is now `net8.0-windows10.0.22621.0`, with
    `SupportedOSPlatformVersion` at 19041. D3D11 goes through vtable function pointers. It
    crops into the same DIB the overlay draws from.
    - It falls back to BitBlt of the screen when unavailable, reported in
      `Capture.WindowProblem`.
    - The cursor is not captured. The yellow border shows on Windows 10, because
      `IsBorderRequired` is Windows 11 only.
    - **Untested on real Windows: Wine has no WGC.** Check the report for "capture: running
      from window …" and a rising frame count.
  - **Input.** `Forward.HiddenClick` has two modes, set by `Saved.HiddenClicks` (Settings:
    "Clicks on a covered window"):
    - "post" (the default) warps the cursor so `GetCursorPos` agrees, then posts the move,
      down and up to the covered window. No focus change.
    - "front" brings the window forward, SendInput-clicks, and hands focus back.
    - Keys (post) and the wheel also go to `App.Target`. `Overlay.Covers` is skipped in post
      mode, because the mirror on one monitor usually overlaps the region.
    - **Open question for the live test: does the game act on posted clicks while covered?** If
      not, the friend switches to "Bring it forward".
- **Quit.** Unlocked, the mirror draws an X in its top-right corner (`OverlayForm.CloseBox`),
  which quits. Settings > Help > Quit Glass also quits.
- **Zip warning.** Running from `Temp` with `Rar$`, `.zip` or `7z` in the path shows a
  one-time "Extract Glass first" balloon and a report finding.
- **Settings.** Radio rows are in their own panels (`SettingsForm.Choice`). Sharing a
  container made them one group, which cleared each other.
- **E2E.** `tools/e2e/run.sh` runs three scenarios: `screen`, plus `window` and `window-front`,
  where stacked stand-ins cover the Priest with the Warrior. All pass. Window mode checks the
  click, shift+right-click, key and wheel to the covered Priest, none to the Warrior, and the X
  quitting.
- The version is 1.1.0.

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
  3. **Cursor clipped** (the game's "lock cursor to window"). `SetCursorPos` stopped at the played
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
    - Every game window: pid, rect, display, elevated, focus.
    - Glass's live state: mirror, capture frames, hook, modes, taken shortcuts.
    - `settings.json` verbatim, and the last 400 log lines (topped up from `Glass.old.log`).
  - Every warp click's log line now ends with a focus trace, for example `-> WowClassic 4812;
    WowClassic 4812 took focus in 12ms; focus back to WowClassic 5120 ok in 3ms`. Focus is the
    main thing Wine couldn't prove, so read this first in the friend's report.
- **Not changed, noted:**
  - Posted-message coordinates assume the game is per-monitor DPI aware.
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
  1. **Game tab list.** It was anchored on all four sides while its page was still at the default
     200×100, so it grew to about 1380×1020. The command column and Copy buttons ended up off the
     right edge, and Copy Ticked / Copy as Macro were covered.
     - Fix: the anchor is removed. The window has a fixed size.
  2. **Game command column.** A `Label` wraps at the space after `/run`, which left "/run" alone on
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
- **Smoke test results.**
  - The log is clean. The only warning is the expected Wine one: `SetWindowDisplayAffinity` can't
    exclude the overlay from capture under Wine.
  - Settings at 150% on a 1080p laptop would be about 30 px taller than the work area. Only empty
    margin goes under the taskbar and the buttons stay visible, so it's left alone.

## The Microsoft Store (MSIX)

Why at all: an unsigned zip from GitHub meets **"Windows protected your PC"** every time,
and it cannot be bought off. A certificate does *not* fix it. Microsoft removed EV's instant
SmartScreen pass in 2024 and says so in writing, and reputation needs "several weeks and
hundreds of clean installs from a wide audience", which one friend will never produce. A
Store MSIX is the only route with no warning, and it is the **free** one: Microsoft re-signs
every MSIX with its own certificate, so no code signing certificate is bought, and Microsoft
hosts it. (The Store's other route, an EXE/MSI link under policy 10.2.9, requires a
certificate you buy *and* hosting you provide, and still does not clear SmartScreen.)

What was checked, against Store Policies v7.20: **there is no policy against keyboard hooks,
input injection, automation, macros, or third-party game tooling.** The words do not appear.
The nearest precedent, [Switcher3way](https://apps.microsoft.com/detail/9MXFXL7GG3C5), is
live and does exactly what Glass does: a system-wide `WH_KEYBOARD_LL` hook plus `SendInput`
into other applications, self-contained .NET MSIX, `runFullTrust`. It took four submissions
and **all three rejections were ordinary functionality bugs, none about the hook**. Their
lesson, and ours: *verify in the flavour that ships*.

### Building it

    Actions > MSIX > Run workflow          # artifact: Glass-<version>.msix

`.github/workflows/msix.yml` publishes with `-p:GlassMsix=true`, runs the self-test on the
binary that goes in the package, lays out `msix/AppxManifest.xml` plus `msix/Assets/`, and
calls `makeappx`. The package is deliberately **unsigned**: the Store signs it. To sideload
for testing, sign it with your own certificate and put that certificate in
`LocalMachine\TrustedPeople` *and* `LocalMachine\Root`, or double-clicking the `.msix`
fails with `0x800B010A`.

Set these repository variables first, from Partner Center > Product management > View app
identity details. They must match byte for byte, case included, or ingestion rejects the
package: `MSIX_IDENTITY_NAME`, `MSIX_PUBLISHER`, `MSIX_PUBLISHER_DISPLAY`.

`msix/Assets/` is generated by `./makeicon-win.swift --msix msix/Assets`, the same drawing as
`Glass.ico`, so the tile and the taskbar icon cannot drift. It also writes
`StoreListing300x300.png`, which is for Partner Center, not the package; the workflow drops
it before packing.

### What the packaged build does differently

- **No update check.** `Packaged.Is` (`src/Packaged.cs`, `GetCurrentPackageFullName`) is
  false in the zip build and true in the package. Packaged, `Updates.Available` is false, the
  check never runs and both Help buttons are hidden. Policy 10.1.5 bars steering acquisition
  outside the Store, and a Store-signed package cannot replace itself in any case.
- **Not single-file.** `GlassMsix=true` turns `PublishSingleFile` off; see `Glass.csproj` for
  why. 245 files, ~172 MB laid out, which packs down.
- **Settings may move.** Windows can redirect `%APPDATA%\Glass` writes into the package's own
  store, and **Microsoft's own documentation contradicts itself** about when it does that for
  a full-trust app. Rather than guess, the report and the log both print `packaged:` and the
  resolved `settings:` path. **Read that on the first packaged run** and decide then whether
  a migration is needed. Note that uninstalling a packaged app also removes its redirected
  writes, which a zip install would have kept.

Hazards that turned out not to apply: Glass has no "start with Windows" (so no `StartupTask`),
sets no AUMID, assumes nothing about the working directory, uses neither `Assembly.Location`
nor `Marshal.GetHINSTANCE`, and already holds a single-instance mutex.

### Only you can do these

1. **Reserve the name** at <https://aka.ms/submitwindowsapp>. Store names are unique across
   all publishers and reservations are invisible from the storefront, so **"Glass" is very
   likely taken**. Have alternatives ready. Whatever it becomes, put it in the manifest
   variables above.
2. **Register**, free, and *only* at <https://storedeveloper.microsoft.com> — Microsoft says
   this is the only entry point for the free flow, and Partner Center, Xbox or Visual Studio
   all land you on the legacy one that still charges $19. Government ID plus a selfie, minutes.
   Choose **Individual**; it cannot be converted to a Company account later.
3. **First submission by hand**, including the IARC age questionnaire, which no API can do.
   Only later updates can be automated (`msstore-cli`, and note it needs Microsoft Entra ID
   credentials, not your personal account -- whether a free individual account can attach a
   tenant at all is unverified, so test that before building a pipeline).
4. **Privacy policy**: mandatory. Policy 10.5.1 requires one of Win32 products outright,
   regardless of what the code does. Paste [PRIVACY.md](PRIVACY.md) in, or link it.

### Text to paste at submission

**runFullTrust justification** (Submission options; modelled on the one that was accepted):

> Glass mirrors a region of one window into an always-on-top overlay and forwards input to
> the real window underneath. It installs a low-level keyboard hook (WH_KEYBOARD_LL) to act
> on the number row while the pointer is over the mirror, and uses SendInput to deliver that
> click or key to the window being mirrored. Neither works inside an app container. Only
> twelve keys are ever acted on and only while the pointer is over the overlay; no keystroke
> is stored or transmitted, and the app sends nothing over the network.

**Notes for certification** (2,000 characters; a reviewer who searches the subject will find
2020 headlines about banned broadcasting software, so the distinction goes in front of them):

> Glass shows a copy of part of one window on another monitor and passes clicks and the
> number keys through to the original. One physical input produces exactly one action in
> exactly one window. It never broadcasts one input to several windows, never repeats or
> replays input, and has no macros, scripting or automation.
>
> To see it work without any game: open two copies of Notepad, run Glass, press Ctrl+Alt+P
> and drag a box around the first one's text area. A mirror of that area appears; clicking
> the mirror puts the caret in the real Notepad underneath.
>
> Glass installs a global keyboard hook and injects input, which is what the runFullTrust
> justification covers. Nothing typed is recorded or transmitted; see the privacy policy.

**Listing**: keep the title generic and do not name any game in it, in the keywords, or in a
screenshot. Policy 11.2 is notice-and-takedown: Microsoft removes on a rights holder's
complaint without weighing it. Screenshots must be of Glass's own windows.

## Wine notes (learned the hard way)

- Use only `tools/wine.sh`; the prefix must stay headless (null driver, `winemac.drv` disabled),
  because the user plays the game while you test.
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
./tools/cmddiff/run.sh                            # BYTE-IDENTICAL
tools/wine.sh 300 dist/Glass/Glass.exe --selftest 'C:\glass-selftest'   # ALL PASSED
./tools/e2e/run.sh                                # ALL PASSED (focus checks skip under Wine)
cd dist && rm -f Glass-Windows.zip && zip -r -X -q Glass-Windows.zip Glass
```

- Icon: `swiftc -O makeicon-win.swift -o .makeicon-win && ./.makeicon-win Glass.ico` (10 sizes).
- Size: WinForms can't be trimmed, so the self-contained single file carries the whole desktop
  runtime. Native libraries self-extract to `%TEMP%\.net\Glass\…` on first run. That's normal
  .NET behaviour.
- `tools/cmddiff/`: `harness.swift` is the real settings code cut from `glass.swift`, with a stub
  `Saved`. `cs/` is the real `GameSettings.cs` with stubs; it uses Swift's defaults for the two
  "(Mac)" settings. If `glass.swift`'s settings section changes, regenerate `harness.swift` by cutting
  three spans:
  - `enum WoWRole` … up to `extension Saved {`
  - `/// The ticked settings, in the shape Core.lua reads` … up to `/// Install or update`
  - `/// Just the commands for one setting` … up to `// MARK: - Settings window`


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
  and the console text. No hotkeys, no capture. It does install the keyboard hook briefly.
- **Research:**
  - LL hooks are silently removed on timeout (at most 1 s on Windows 10 1709+).
  - AltSnap watches for hook removal with Raw Input.
  - Keys can be posted to a background game window.
  - Publishers ban input broadcasting, not playing two clients by hand.
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
  - Game setting details are shown under each checkbox.
  - The text is worded for any player rather than "Warrior/Priest".
  - The tray menu is right-click (the Windows convention); double-click opens Settings.
- **Wording:** "Alt-click" instead of ⌥-click.
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
- `src/GameSettings.cs`: the settings table, console commands and macro chunks.
- `src/Layered.cs`: LayeredSurface, LayeredForm, and `Look` (colours, round rects, MDL2 icon
  font).
- Windows: `src/Picker.cs`, `Header.cs`, `Overlay.cs`, `GpuBoost.cs`, `Tray.cs`,
  `SettingsForm.cs` (which includes `OneLineLabel`).
- `src/App.cs`, `src/Program.cs`, `src/SelfTest.cs` (which includes `Render` and `Layout`),
  `src/Report.cs` (the help report).
- `msix/`: `AppxManifest.xml` and `Assets/` for the Store package; built by
  `.github/workflows/msix.yml`.
- `src/Theme.cs`, `src/ThemeIcons.cs`, `src/ThemeControls.cs`, `src/ThemeSample.cs`: the look.
- `src/Packaged.cs`: whether this copy is the Store build.
- `PRIVACY.md`: the privacy policy, and the Store requires one.
- `README.md`: the repo's front page.
- `README.txt`: for the friend. It is ASCII with CRLF line endings; keep it that way (after an
  edit, run `perl -pi -e 's/\r?\n/\r\n/' README.txt`).
- `tools/`: `wine.sh`, `pe.py`, `cmddiff/`, `e2e/`.
- `Glass.csproj`, `app.manifest`, `Glass.ico`, `makeicon-win.swift`, `.gitignore`.
