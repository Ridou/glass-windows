# Privacy

**Glass sends nothing about you anywhere. It has no account, no analytics, no telemetry and
no crash reporting, and it never transmits anything you type.**

Everything below can be checked against the source in this repository. Glass is a few
thousand lines in [`src/`](src/), and the places that matter are named so you can go and look
rather than take this on trust.

## The keyboard

Glass watches the keyboard. It has to: the point of mouseover keys is that pressing `3` while
the pointer is over the mirror casts your healer's third spell instead of your tank's. Windows
only offers one way to do that, a low-level keyboard hook, and that hook is shown every
keystroke on the system. So the honest question is not whether Glass *sees* your keys — it is
what it does with them. The answer:

- **Only twelve keys are ever acted on:** `1 2 3 4 5 6 7 8 9 0 - =`, the number row. Anything
  else is handed straight back to Windows without being examined further. The check is the
  second line of `Claim` in [`src/Hooks.cs`](src/Hooks.cs): a key that is not in the number row
  returns immediately.
- **Even those twelve are only acted on while the pointer is over the locked mirror.**
  Everywhere else they go to the game exactly as they always did.
- **Nothing you type is stored.** Not letters, not chat, not passwords, not your account name.
  There is no buffer of keystrokes anywhere in Glass, and nothing is written to disk as you
  type.
- **With mouseover keys turned off, Glass claims nothing at all** — every key, number row
  included, goes to the game untouched.

To be exact about one thing, because it is the sort of detail a privacy page is tempted to
skate over: the hook itself is installed for as long as Glass is running, not only while
mouseover keys are switched on. Turning the setting off stops Glass acting on keys; it does
not currently uninstall the hook. It sees them and passes them on.

## The one thing that leaves your PC

Glass makes exactly one network request, ever:

    GET https://api.github.com/repos/Ridou/glass-windows/releases/latest

It happens a few seconds after Glass starts, and it asks GitHub one question: what is the
newest released version? The reply is a version number, which is why Glass can tell you an
update is out. The request carries no body — only a `User-Agent` of `Glass-Windows/<version>`,
which GitHub requires, and an `Accept` header. It is in
[`src/Updates.cs`](src/Updates.cs), and it is the only use of `HttpClient` in the whole
program. You can confirm that for yourself:

```
findstr /s /i "HttpClient WebClient Socket HttpWebRequest" src\*.cs
```

One hit, in `Updates.cs`. Starting Glass with `--no-update-check` stops even that, and then
Glass makes no network requests at all.

**Glass does not download or install its own updates.** When a new version exists it opens the
releases page in your browser and you decide.

## What is written to your disk, and only your disk

| Where | What |
|---|---|
| `%APPDATA%\Glass\settings.json` | Your settings: regions, overlay position, hotkeys |
| `%LOCALAPPDATA%\Glass\Glass.log` | A running log of what Glass did, for when something breaks |

The log records Glass's own decisions — that it started capturing, that focus came back after
a click, that the keyboard hook was reinstalled. It also records **which of the twelve number
row keys it forwarded and to which program**, as lines like `key 3 -> the game client`, because when a
keypress lands on the wrong character that line is the only way to find out why. That is the
full extent of any keystroke ever reaching disk: twelve possible keys, only the ones Glass
itself forwarded, on your machine only.

Neither file is ever uploaded. **Settings > Help > Make a Report** gathers the recent log into
a text file on your Desktop so you can send it to someone if you are asking for help — that is
you choosing to share it, by hand, and you can read it first.

## Screen capture

Glass captures the screen region you pick, to draw the mirror. Frames are held in memory just
long enough to draw and are then dropped. **No captured frame is ever written to disk**, and
nothing captured leaves your PC.

The single exception is a developer flag, `--selftest <folder>`, which Glass's build uses to
check its own layout: it saves pictures of Glass's *own* windows — the settings window, the
region picker, the header bar — into the folder you name, and exits. It captures no game and
no screen content, and normal use never reaches it.

## Input

Glass sends clicks and the twelve number keys to the window under the region you are
mirroring. One input from you produces exactly one action on exactly one character. Glass
never broadcasts one input to several clients, never repeats or replays input, and has no
macros, scripting or automation of any kind.

## Contact

Questions, or something here that does not match what the code does:
<https://github.com/Ridou/glass-windows/issues>
