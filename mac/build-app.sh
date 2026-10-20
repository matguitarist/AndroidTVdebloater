#!/bin/bash
# Builds "Android TV Debloater.app" and AndroidTVDebloater-mac.zip in mac/build.
#   ./build-app.sh                 this Mac's architecture
#   ARCHS="arm64 x86_64" ./build-app.sh   universal (needs Xcode)
# The version comes from the Windows project, so both apps always match.
set -euo pipefail
cd "$(dirname "$0")"

VERSION="${VERSION:-$(sed -nE 's|.*<Version>(.*)</Version>.*|\1|p' ../src/AndroidTVDebloater/AndroidTVDebloater.csproj | head -1)}"
ARCHS="${ARCHS:-$(uname -m)}"
ARCH_FLAGS=()
for a in $ARCHS; do ARCH_FLAGS+=(--arch "$a"); done

swift build -c release "${ARCH_FLAGS[@]}"
BIN_DIR="$(swift build -c release "${ARCH_FLAGS[@]}" --show-bin-path)"

APP="build/Android TV Debloater.app"
rm -rf build
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_DIR/AndroidTVDebloater" "$APP/Contents/MacOS/AndroidTVDebloater"
cp -R Resources/en.lproj Resources/fr.lproj Resources/AppIcon.icns "$APP/Contents/Resources/"
cp ../shared/catalog.json "$APP/Contents/Resources/catalog.json"
sed "s/__VERSION__/$VERSION/g" Info.plist > "$APP/Contents/Info.plist"

# Ad-hoc signature: required to run on Apple silicon. Not notarized (needs a paid Apple account).
codesign --force --sign - --timestamp=none "$APP"

(cd build && ditto -c -k --keepParent "Android TV Debloater.app" AndroidTVDebloater-mac.zip)
echo "Built $APP ($VERSION, $ARCHS)"
