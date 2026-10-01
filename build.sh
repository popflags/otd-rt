#!/usr/bin/env bash
# Builds the plugin and the replay/calibration tool, runs the tests, and puts the plugin in dist/RapidTrigger.
set -euo pipefail
cd "$(dirname "$0")"

dotnet test tests/RapidTrigger.Tests
dotnet build src/RapidTrigger -c Release
dotnet build src/AnglePreservingSensitivity -c Release
dotnet build tools/RtTool -c Release

rm -rf dist
mkdir -p dist/RapidTrigger
cp src/RapidTrigger/bin/Release/net8.0/RapidTrigger.dll dist/RapidTrigger/
mkdir -p dist/AnglePreservingSensitivity
cp src/AnglePreservingSensitivity/bin/Release/net8.0/AnglePreservingSensitivity.dll dist/AnglePreservingSensitivity/
mkdir -p dist/RtTool
cp tools/RtTool/bin/Release/net8.0/rt.dll tools/RtTool/bin/Release/net8.0/rt.runtimeconfig.json dist/RtTool/

pack() {
    if command -v zip >/dev/null; then
        (cd dist && zip -qr "$1.zip" "$1")
    else
        (cd dist && python3 -m zipfile -c "$1.zip" "$1")
    fi
}
pack RapidTrigger
pack AnglePreservingSensitivity
pack RtTool

echo
echo "Plugin:  dist/RapidTrigger/RapidTrigger.dll  (zip: dist/RapidTrigger.zip)"
echo "Plugin:  dist/AnglePreservingSensitivity/AnglePreservingSensitivity.dll  (zip: dist/AnglePreservingSensitivity.zip)"
echo "Install: copy each plugin folder into ~/.config/OpenTabletDriver/Plugins/ and restart the daemon"
echo "Tool:    dotnet dist/RtTool/rt.dll --help  (zip: dist/RtTool.zip, runs anywhere with the .NET 8 runtime)"
