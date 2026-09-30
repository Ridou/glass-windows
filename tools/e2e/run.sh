#!/bin/sh
# End-to-end check of dist/Glass/Glass.exe in the headless Wine prefix: stand-in game windows,
# the real Glass, and synthesized clicks, keys, wheel and hotkeys. Three scenarios:
#   screen        two monitors' worth: the mirrored client and the played one side by side
#   window        one monitor: the played client covers the mirrored one; input is posted
#   window-front  the same, with clicks that bring the covered client forward
# Expect "ALL PASSED" for each. Publish Glass first (dotnet publish -c Release -o dist/Glass).
# Nothing can reach the Mac's screen: the prefix uses the null graphics driver.
# Wine has no Windows.Graphics.Capture, so window capture falls back to the screen there.
set -e
cd "$(dirname "$0")"
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet publish -c Release -o /tmp/glass-e2e -v:q >/dev/null
P="$HOME/Library/Caches/glass-wine/prefix"
W="$HOME/Library/Caches/glass-wine/Wine Devel.app/Contents/Resources/wine/bin"
GLASS='Z:\Users\'"$USER"'\Projects\glass-windows\dist\Glass\Glass.exe'
for scenario in screen window window-front; do
    mode=drive; [ "$scenario" = screen ] || mode="drive-$scenario"
    WINEPREFIX="$P" "$W/wineserver" -k 2>/dev/null || true
    rm -rf "$P/drive_c/glass-e2e-$scenario"
    ../wine.sh 300 /tmp/glass-e2e/E2E.exe $mode "$GLASS" "C:\\glass-e2e-$scenario" "$@" >/dev/null 2>&1 || true
    echo "$scenario: $(tail -1 "$P/drive_c/glass-e2e-$scenario/e2e.txt" 2>/dev/null || echo 'no result')"
done
WINEPREFIX="$P" "$W/wineserver" -k 2>/dev/null || true
echo "results: $P/drive_c/glass-e2e-*/e2e.txt"
