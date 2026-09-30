# Glass for Windows

Mirror a live region of one monitor as an always-on-top overlay on another, and click through
it to the real thing underneath. Built for playing two WoW characters at once by hand: the
healer's party frames float on the tank's monitor.

One input produces one action on one character. Glass never broadcasts, duplicates or
automates input.

This is the Windows port of [Glass for macOS](https://github.com/Ridou/glass), feature for
feature. The deliberate differences are listed in [HANDOFF.md](HANDOFF.md).

## Using it

Download `Glass-Windows.zip`, extract it and run `Glass.exe`. [README.txt](README.txt) is the
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
- `python3 tools/pe.py dist/Glass/Glass.exe` shows the subsystem, icon, manifest and version.

The self-test covers layout only. Clicks and keys need testing on real Windows with two WoW
clients running.

[HANDOFF.md](HANDOFF.md) has the design, threading, and Wine setup notes.
