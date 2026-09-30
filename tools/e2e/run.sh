#!/bin/sh
# End-to-end check of dist/Glass/Glass.exe in the headless Wine prefix: two stand-in game
# windows, the real Glass mirroring one onto the other, and synthesized clicks, keys, wheel and
# hotkeys. Expect "ALL PASSED". Publish Glass first (dotnet publish -c Release -o dist/Glass).
# Nothing can reach the Mac's screen: the prefix uses the null graphics driver.
set -e
cd "$(dirname "$0")"
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet publish -c Release -o /tmp/glass-e2e -v:q >/dev/null
P="$HOME/Library/Caches/glass-wine/prefix"
W="$HOME/Library/Caches/glass-wine/Wine Devel.app/Contents/Resources/wine/bin"
WINEPREFIX="$P" "$W/wineserver" -k 2>/dev/null || true
rm -rf "$P/drive_c/glass-e2e"
../wine.sh 240 /tmp/glass-e2e/E2E.exe drive 'Z:\Users\'"$USER"'\Projects\glass-windows\dist\Glass\Glass.exe' 'C:\glass-e2e' "$@" || true
WINEPREFIX="$P" "$W/wineserver" -k 2>/dev/null || true
echo "results: $P/drive_c/glass-e2e/"
tail -1 "$P/drive_c/glass-e2e/e2e.txt"
