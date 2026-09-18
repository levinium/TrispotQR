#!/usr/bin/env bash
#
# Builds the Mac downloads, on a Mac:
#
#   bash tools/package-mac.sh [output directory]
#
# Writes, for Apple silicon and Intel:
#
#   TrispotQR-mac-arm64.zip  TrispotQR-mac-arm64.zip.sha256
#   TrispotQR-mac-x64.zip    TrispotQR-mac-x64.zip.sha256
#
# each holding "Trispot QR.app". The release workflow runs this; it is a script rather than
# workflow steps so a Mac build can also be made by hand the same way.
#
# Only a Mac can do this, because only a Mac can sign a Mac app. The signature is ad hoc
# (there is no Apple developer account), which Apple silicon requires before it will run the
# code at all. Gatekeeper still asks each user to allow the app once; the README says how.

set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
out="${1:-$root/dist-mac}"
project="$root/src/TrispotQR.Desktop/TrispotQR.Desktop.csproj"
macos="$root/src/TrispotQR.Desktop/macOS"
app="Trispot QR.app"

version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$project" | head -n 1)
if [ -z "$version" ]; then
  echo "No <Version> found in $project." >&2
  exit 1
fi
echo "Packaging Trispot QR $version for Mac into $out"

rm -rf "$out"
mkdir -p "$out"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# macOS wants an .icns holding every size, made from the one 1024 px master that
# tools/New-AppIcon.ps1 draws on Apple's icon grid.
iconset="$work/AppIcon.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$macos/AppIcon-1024.png" --out "$iconset/icon_${size}x${size}.png" > /dev/null
  double=$((size * 2))
  sips -z "$double" "$double" "$macos/AppIcon-1024.png" --out "$iconset/icon_${size}x${size}@2x.png" > /dev/null
done
iconutil -c icns "$iconset" -o "$work/AppIcon.icns"

for arch in arm64 x64; do
  bundle="$out/$arch/$app"

  # Not single-file: a Mac app keeps its libraries inside the bundle, and single-file would
  # unpack them to a temporary folder on every launch instead.
  dotnet publish "$project" -c Release -r "osx-$arch" --self-contained true \
    -p:DebugType=none -o "$work/$arch" --nologo

  mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
  cp -R "$work/$arch/." "$bundle/Contents/MacOS/"
  sed "s/__VERSION__/$version/g" "$macos/Info.plist" > "$bundle/Contents/Info.plist"
  cp "$work/AppIcon.icns" "$bundle/Contents/Resources/AppIcon.icns"

  codesign --force --deep --sign - "$bundle"
  codesign --verify --deep --strict "$bundle"

  # ditto rather than zip: it keeps the bundle's permissions and signature intact.
  ditto -c -k --keepParent "$bundle" "$out/TrispotQR-mac-$arch.zip"
  (cd "$out" && shasum -a 256 "TrispotQR-mac-$arch.zip" > "TrispotQR-mac-$arch.zip.sha256")
done

ls -la "$out"/*.zip
