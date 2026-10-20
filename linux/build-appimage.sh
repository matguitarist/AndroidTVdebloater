#!/bin/bash
# Builds AndroidTVDebloater-x86_64.AppImage (and its .zsync for AppImageUpdate) in linux/build.
# Needs: the .NET 10 SDK, and appimagetool (APPIMAGETOOL=path, downloaded if missing).
set -euo pipefail
cd "$(dirname "$0")"

TOOL="${APPIMAGETOOL:-build/appimagetool}"
APPDIR="build/AppDir"
rm -rf "$APPDIR" build/*.AppImage build/*.zsync
mkdir -p "$APPDIR/usr/bin" build

dotnet publish ../src/AndroidTVDebloater -c Release -r linux-x64 -o build/publish
cp build/publish/AndroidTVDebloater "$APPDIR/usr/bin/AndroidTVDebloater"
cp AppRun "$APPDIR/AppRun"
chmod +x "$APPDIR/AppRun" "$APPDIR/usr/bin/AndroidTVDebloater"
cp androidtvdebloater.desktop "$APPDIR/"
cp ../docs/icon.png "$APPDIR/androidtvdebloater.png"

if [ ! -x "$TOOL" ]; then
  curl -sL -o "$TOOL" https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage
  chmod +x "$TOOL"
fi

# The update information lets AppImageUpdate and AppImage managers update it from GitHub too.
ARCH=x86_64 "$TOOL" --appimage-extract-and-run -n \
  -u "gh-releases-zsync|matguitarist|AndroidTVdebloater|latest|AndroidTVDebloater-x86_64.AppImage.zsync" \
  "$APPDIR" build/AndroidTVDebloater-x86_64.AppImage
echo "Built linux/build/AndroidTVDebloater-x86_64.AppImage"
