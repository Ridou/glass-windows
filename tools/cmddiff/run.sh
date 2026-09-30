#!/bin/sh
# Diff the Windows build's console output (commands and macro chunks) against the
# macOS build's, compiled from the real sources. Expect "BYTE-IDENTICAL".
# harness.swift embeds the settings code cut from the Mac build's glass.swift (Ridou/glass) at the
# time it was made; regenerate it if that section changes (see HANDOFF.md, Toolchain).
set -e
cd "$(dirname "$0")"
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
swiftc -O harness.swift -o /tmp/glass-harness-swift
/tmp/glass-harness-swift > /tmp/glass-swift.txt
(cd cs && dotnet build -c Release -v:q -o /tmp/glass-harness-cs >/dev/null)
dotnet /tmp/glass-harness-cs/Harness.dll > /tmp/glass-cs.txt
cmp /tmp/glass-swift.txt /tmp/glass-cs.txt && echo BYTE-IDENTICAL
