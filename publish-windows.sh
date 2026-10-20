#!/usr/bin/env bash
# Build the single-file Windows exe into ./publish (works from macOS/Linux too).
set -euo pipefail
cd "$(dirname "$0")"
dotnet publish src/AndroidTVDebloater -c Release -r win-x64 -o publish
echo "Built publish/UnbloatMyTV.exe"
