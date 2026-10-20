#!/bin/bash
# Builds UnbloatMyTV-x86_64.AppImage (and its .zsync for AppImageUpdate) in linux/build.
# Needs: the .NET 10 SDK, and appimagetool (APPIMAGETOOL=path, downloaded if missing).
set -euo pipefail
cd "$(dirname "$0")"

TOOL="${APPIMAGETOOL:-build/appimagetool}"
APPDIR="build/AppDir"
rm -rf "$APPDIR" build/*.AppImage build/*.zsync
mkdir -p "$APPDIR/usr/bin" build

dotnet publish ../src/AndroidTVDebloater -c Release -r linux-x64 -o build/publish
cp build/publish/UnbloatMyTV "$APPDIR/usr/bin/UnbloatMyTV"
cp AppRun "$APPDIR/AppRun"
chmod +x "$APPDIR/AppRun" "$APPDIR/usr/bin/UnbloatMyTV"
cp unbloatmytv.desktop "$APPDIR/"
cp ../docs/icon.png "$APPDIR/unbloatmytv.png"

if [ ! -x "$TOOL" ]; then
  curl -sL -o "$TOOL" https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage
  chmod +x "$TOOL"
fi

# The update information lets AppImageUpdate and AppImage managers update it from GitHub too.
ARCH=x86_64 "$TOOL" --appimage-extract-and-run -n \
  -u "gh-releases-zsync|matguitarist|UnbloatMyTV|latest|UnbloatMyTV-x86_64.AppImage.zsync" \
  "$APPDIR" build/UnbloatMyTV-x86_64.AppImage
# appimagetool writes the .zsync into the current folder.
mv -f UnbloatMyTV-x86_64.AppImage.zsync build/ 2>/dev/null || true
test -f build/UnbloatMyTV-x86_64.AppImage.zsync
echo "Built linux/build/UnbloatMyTV-x86_64.AppImage"
