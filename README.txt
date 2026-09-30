GLASS FOR WINDOWS
=================

Glass mirrors a region of your screen - your healer's party frames, say - as a small
always-on-top window on another monitor. Clicking the mirror clicks the real thing
underneath, then hands focus straight back.

It is built for playing two WoW characters at once by hand. Each click or key you make
does one thing on one character. Glass never broadcasts, repeats or automates input.


BEFORE YOU START
----------------
* You need Windows 10 (version 2004 or later) or Windows 11, 64-bit.

* Extract the whole zip first (right-click it > Extract All). Don't run Glass.exe from
  inside the zip.

* Glass isn't signed, so the first time you open it Windows may say "Windows protected
  your PC". Click "More info", then "Run anyway". Some antivirus programs are also wary
  of anything that watches the keyboard, which Glass does for mouseover keys (below).

* In WoW's graphics options, set Display Mode to "Windowed (Fullscreen)". Glass can't see
  a game running in exclusive fullscreen.

* The first launch can take a few seconds. Later ones are quicker.


FIRST RUN
---------
1. Open Glass.exe. Every screen dims.
2. Drag a rectangle around what you want to mirror, such as the party frames on your
   healer's monitor. Press Esc instead to quit.
3. The mirror appears. From then on Glass lives in the tray, by the clock. You may need
   to click the ^ arrow there to see its icon.
     - Right-click the tray icon for the menu.
     - Double-click it, or open Glass.exe again, for Settings.
4. To move the mirror, press Ctrl+Alt+L to unlock it (orange edge), drag it where you
   want it, then press Ctrl+Alt+L again to lock it (faint green edge).


USING IT
--------
Locked (green edge)     Clicks go through to the game. Every mouse button, with any of
                        Shift, Ctrl and Alt, arrives exactly as you pressed it, so
                        Clique binds work.
Unlocked (orange edge)  A drag moves the mirror. Nothing clicks through.

* A click is sent when you RELEASE the mouse button.

* Mouseover keys: while the pointer is over the locked mirror, the number row (1 to 0,
  - and =) acts on the frame you're hovering. That's for Clique mouseover binds or an
  MMO mouse's side buttons. Everywhere else those keys work as usual. You can turn this
  off in Settings > Overlay.

* The header: move the pointer near the mirror and a small "..." pill appears above it.
  Click the pill for Settings, the lock, << (collapse) and the four presets.

* Presets (duo, 5, 10, 20) each remember a region, one for each group size. Click one to
  switch to it, or Alt-click it to record it again. Clicking an empty one records it.

Shortcuts (change them in Settings > Shortcuts):

  Ctrl+Alt+1 to 4   switch to preset duo / 5 / 10 / 20
  Ctrl+Alt+P        pick a new region
  Ctrl+Alt+H        show or hide the mirror
  Ctrl+Alt+L        lock or unlock the mirror
  Ctrl+Alt+B        header: dots on hover / always shown / hidden

Keyboards with an AltGr key (German, French, Polish and others) treat Ctrl+Alt as AltGr.
If a shortcut swallows a character you type, such as @ or an accented letter, change it.

Settings > WoW lists game settings that help when you play two characters. Tick the ones
you want, copy the commands, and paste them into WoW's chat or into a macro. Glass never
changes the game itself.


IF SOMETHING DOESN'T WORK
-------------------------
* Clicks or keys do nothing: if WoW runs as administrator, Glass has to as well
  (right-click Glass.exe > Run as administrator). Glass warns you when it spots this.

* Mouseover keys stopped working: use tray menu > Reinstall Keyboard Hook.

* The mouse wheel: in Windows Settings > Mouse, leave "Scroll inactive windows" on. It's
  on unless you turned it off. Glass copes if it's off, but it's built for it being on.

* The mirror and its header don't show up in screenshots, OBS or Discord screen share.
  That's deliberate: it's how Glass keeps itself out of its own mirror.

* To start over, use tray menu > Reset Everything. Or quit Glass and delete
  %APPDATA%\Glass\settings.json.

When you report a problem, please send the log with it. It's here, or use tray menu >
Open Log:

  %LOCALAPPDATA%\Glass\Glass.log

It records what Glass did and how long each step took. It never records the keys you
press away from the mirror.

To quit, use tray menu > Quit Glass. For command-line options, run Glass.exe --help from
a Command Prompt.
