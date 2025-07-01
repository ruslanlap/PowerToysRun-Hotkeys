#!/bin/bash

set -e

rm -rf ./Hotkeys/Publish
rm -rf ./Hotkeys/Community.PowerToys.Run.Plugin.Hotkeys/obj
rm -rf ./Hotkeys-x64.zip
rm -rf ./Hotkeys-ARM64.zip
rm -rf ./Hotkeys/Community.PowerToys.Run.Plugin.Hotkeys/bin

PROJECT_PATH="Hotkeys/Community.PowerToys.Run.Plugin.Hotkeys/Community.PowerToys.Run.Plugin.Hotkeys.csproj"
OUT_ROOT="./Hotkeys/Community.PowerToys.Run.Plugin.Hotkeys/bin"
DEST_DIR="./Hotkeys/Publish"

echo "🛠️  Building for x64..."
dotnet publish "$PROJECT_PATH" -c Release -r win-x64

echo "🛠️  Building for ARM64..."
dotnet publish "$PROJECT_PATH" -c Release -r win-arm64

echo "📂 Copying published files to $DEST_DIR..."
rm -rf "$DEST_DIR"
mkdir -p "$DEST_DIR"

# ⚠️ Зверни увагу на цю зміну
PUBLISH_X64=$(find "$OUT_ROOT" -type d -path '*win-x64/publish' | head -n 1)

echo "ℹ️  Using publish folder: $PUBLISH_X64"
cp -r "$PUBLISH_X64"/* "$DEST_DIR"
# include shortcuts directory for search data
cp -r ./Hotkeys/Shortcuts "$DEST_DIR/Shortcuts"

echo "📦 Zipping results..."
ZIP_X64="./Hotkeys-x64.zip"
(cd "$DEST_DIR" && zip -r "../$(basename "$ZIP_X64")" .)

echo "✅ Done! Created:"
echo " - $ZIP_X64"
