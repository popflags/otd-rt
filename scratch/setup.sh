#!/usr/bin/env bash
# Rebuilds the throwaway tooling the analyses need (the Claude scratchpad under /tmp gets wiped on reboot / date change).
# Usage: scratch/setup.sh [DIR]   (default DIR: $HOME/.cache/otd-rt-tools)
set -euo pipefail
DIR=${1:-$HOME/.cache/otd-rt-tools}
mkdir -p "$DIR"
cd "$DIR"
# .NET 8 SDK (the system dotnet on this machine is runtime-only; `dotnet build` there fails with "SDK not found")
[ -x "$DIR/dotnet/dotnet" ] || { curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh; bash dotnet-install.sh --channel 8.0 --install-dir "$DIR/dotnet"; }
# python-evtx for scratch/win_check.py
[ -d "$DIR/evtx-lib/Evtx" ] || python3 -m pip install --quiet --target "$DIR/evtx-lib" python-evtx
# ILSpy CLI to decompile third-party OTD plugins (8.2 targets .NET 6: run it with DOTNET_ROLL_FORWARD=Major)
[ -x "$DIR/tools/ilspycmd" ] || "$DIR/dotnet/dotnet" tool install --tool-path "$DIR/tools" ilspycmd --version 8.2.0.7535
cat <<MSG
dotnet:   $DIR/dotnet/dotnet        (build: \$DIR/dotnet/dotnet build -c Release tools/RtTool)
evtx lib: $DIR/evtx-lib             (python3 scratch/win_check.py System.evtx --lib $DIR/evtx-lib --since '2026-10-20')
ilspy:    DOTNET_ROOT=$DIR/dotnet DOTNET_ROLL_FORWARD=Major $DIR/tools/ilspycmd -o OUT plugin.dll -r PLUGIN_DIR
MSG
