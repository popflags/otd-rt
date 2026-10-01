#!/usr/bin/env bash
# Builds the plugin and the replay/calibration tool, runs the tests, and puts the plugin in dist/RapidTrigger.
set -euo pipefail
cd "$(dirname "$0")"

dotnet test tests/RapidTrigger.Tests
dotnet build src/RapidTrigger -c Release
dotnet build tools/RtTool -c Release

rm -rf dist
mkdir -p dist/RapidTrigger
cp src/RapidTrigger/bin/Release/net8.0/RapidTrigger.dll dist/RapidTrigger/
(cd dist && zip -qr RapidTrigger.zip RapidTrigger)

echo
echo "Plugin:  dist/RapidTrigger/RapidTrigger.dll  (zip: dist/RapidTrigger.zip)"
echo "Install: copy dist/RapidTrigger into ~/.config/OpenTabletDriver/Plugins/ and restart the daemon"
echo "Tool:    dotnet tools/RtTool/bin/Release/net8.0/rt.dll --help"
