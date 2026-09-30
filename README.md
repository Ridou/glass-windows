# Glass for Windows

<p align="center">
  <a href="https://github.com/Ridou/glass-windows/releases/latest/download/Glass-Windows.zip">
    <img alt="Download Glass for Windows" src="https://img.shields.io/github/v/release/Ridou/glass-windows?label=%E2%AC%87%20Download%20Glass%20for%20Windows&style=for-the-badge&color=2ea043">
  </a>
  <br>
  <sub>Always the newest version · <a href="https://github.com/Ridou/glass-windows/releases">all releases and what changed</a></sub>
</p>

Mirror a live region of one monitor as an always-on-top overlay on another, and click through
it to the real thing underneath. Built for playing two WoW characters at once by hand: the
healer's party frames float on the tank's monitor, or on one monitor with Alt+Tab.

One input produces one action on one character. Glass never broadcasts, duplicates or
automates input.

This is the Windows port of [Glass for macOS](https://github.com/Ridou/glass), kept feature for
feature in step with it. The deliberate differences are listed in [HANDOFF.md](HANDOFF.md).

## Download and update

1. **[Download Glass-Windows.zip](https://github.com/Ridou/glass-windows/releases/latest/download/Glass-Windows.zip)**.
   This link always gets the newest version.
2. Right-click the zip > **Extract All**, then run `Glass.exe` from the extracted folder.

Glass checks for a newer version when it starts and tells you if there is one. You can also
check in Settings > Help. To update, download again, quit Glass, and replace the old folder.
Your settings are kept, because they live in `%APPDATA%\Glass`, not next to `Glass.exe`.

## Using it

Extract the zip (right-click > Extract All) and run `Glass.exe`. [README.txt](README.txt) is the
player's guide that ships in the zip. `Glass.exe --help` lists the command-line options.

Requires Windows 10 version 2004 or later, or Windows 11, 64-bit. WoW must be in Windowed
(Fullscreen) mode.

## Building

C# WinForms on .NET 8. The build is a single self-contained `Glass.exe`, so nothing needs
installing on the player's machine. It builds on macOS as well as Windows.

```sh
dotnet publish -c Release -o dist/Glass       # dist/Glass/Glass.exe + README.txt
cd dist && zip -r -X Glass-Windows.zip Glass
```

Checks, all runnable from a Mac:

- `tools/wowdiff/run.sh` compares the WoW console commands and addon config byte for byte
  against the Mac build's Swift. It prints `BYTE-IDENTICAL`.
- `tools/wine.sh 300 dist/Glass/Glass.exe --selftest 'C:\glass-selftest'` renders every window
  to PNG in a headless Wine prefix and runs the self-checks. It prints `ALL PASSED`.
- `tools/e2e/run.sh` runs the real Glass.exe against two stand-in game windows. It sends
  clicks, keys, the wheel and hotkeys, and checks that each input reached exactly one of them,
  at the right place. It prints `ALL PASSED`.
- `python3 tools/pe.py dist/Glass/Glass.exe` shows the subsystem, icon, manifest and version.

Wine ignores no-activate windows, so the end-to-end test skips its focus checks there. Focus
handling, and capture on a real GPU, still need a test on Windows with two WoW clients.

[HANDOFF.md](HANDOFF.md) has the design, threading, and Wine setup notes.
