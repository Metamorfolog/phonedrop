#!/bin/bash
# Sestaví PhoneDrop.app (universal: Apple Silicon + Intel) a DMG do mac/dist/.
#   ./build.sh [verze]      verze výchozí podle windows/PhoneDrop/PhoneDrop.csproj
# Podpis je ad-hoc (bez Apple Developer účtu): při prvním spuštění pravé tlačítko › Otevřít.
set -euo pipefail
cd "$(dirname "$0")"

VER="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' ../windows/PhoneDrop/PhoneDrop.csproj)}"
APP="dist/PhoneDrop.app"

echo "PhoneDrop $VER"
swift build -c release --arch arm64 --arch x86_64
BIN="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)/PhoneDrop"

rm -rf dist
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/PhoneDrop"
sed "s/__VERSION__/$VER/g" Info.plist > "$APP/Contents/Info.plist"
printf 'APPL????' > "$APP/Contents/PkgInfo"

# Ikona z webu (512 px) → .icns
ICONSET="$(mktemp -d)/PhoneDrop.iconset"
mkdir -p "$ICONSET"
SRC=../website/public/assets/icon.png
for s in 16 32 128 256; do
    sips -z $s $s "$SRC" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
    sips -z $((s * 2)) $((s * 2)) "$SRC" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
cp "$SRC" "$ICONSET/icon_512x512.png"
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/PhoneDrop.icns"

codesign --force --deep --sign - --options runtime "$APP"
codesign --verify --strict "$APP"

# DMG s odkazem na Aplikace
STAGE="$(mktemp -d)"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
hdiutil create -volname "PhoneDrop $VER" -srcfolder "$STAGE" -ov -format UDZO "dist/PhoneDrop-$VER-mac.dmg" >/dev/null
(cd dist && ditto -c -k --keepParent PhoneDrop.app "PhoneDrop-$VER-mac.zip")

lipo -info "$APP/Contents/MacOS/PhoneDrop"
ls -lh dist
