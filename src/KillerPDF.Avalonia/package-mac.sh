#!/usr/bin/env bash
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/KillerPDF.csproj" | head -1)"
out="$root/artifacts/mac"
app="$out/KillerPDF.app"
rm -rf "$out" && mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
dotnet publish "$here/KillerPDF.Avalonia.csproj" -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=false -o "$out/publish"
cp -R "$out/publish/." "$app/Contents/MacOS/"
sed "s/__VERSION__/$version/g" "$here/packaging/Info.plist" > "$app/Contents/Info.plist"
iconset="$out/KillerPDF.iconset" && mkdir -p "$iconset"
for s in 16 32 128 256 512; do
  sips -z $s $s "$root/Resources/kp-icon.png" --out "$iconset/icon_${s}x${s}.png" >/dev/null
  sips -z $((s*2)) $((s*2)) "$root/Resources/kp-icon.png" --out "$iconset/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/KillerPDF.icns"
codesign --force --deep --sign - "$app"
dmg="$out/KillerPDF-$version-osx-arm64.dmg"
hdiutil create -volname KillerPDF -srcfolder "$app" -ov -format UDZO "$dmg" >/dev/null
du -sh "$app" "$dmg"
