#!/bin/sh
# Run a Windows program in the headless Wine prefix, with a time limit.
#
#   tools/wine.sh [SECONDS] PROGRAM [ARGS...]      e.g. tools/wine.sh 120 dist/Glass/Glass.exe --list
#
# The prefix uses the null graphics driver and has winemac.drv disabled, so nothing can open a
# Mac window or take focus from the game being played on this machine. Keep it that way.
# Set WINEDEBUG=err+all (or unset it to Wine's default) when a crash leaves no output.

limit=180
case "$1" in *[!0-9]*|'') ;; *) limit=$1; shift ;; esac

WINE="$HOME/Library/Caches/glass-wine/Wine Devel.app/Contents/Resources/wine/bin/wine"
export WINEPREFIX="$HOME/Library/Caches/glass-wine/prefix"
export WINEDEBUG="${WINEDEBUG--all}"
export WINEDLLOVERRIDES="mscoree=;mshtml=;winemac.drv=d;winedbg.exe=d"   # a crash exits instead of waiting in winedbg
export MVK_CONFIG_LOG_LEVEL=0        # MoltenVK prints its whole feature list at every start

# macOS has no timeout(1); an alarm survives exec and ends the process when it fires.
exec perl -e 'alarm shift; exec @ARGV or die "exec: $!\n"' "$limit" "$WINE" "$@"
