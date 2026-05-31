#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
VERSION="0.1.0"
RELEASE_DIR="$SCRIPT_DIR/release"
GAMEDATA_DIR="$RELEASE_DIR/GameData/KrispyMPL"

echo "==> Building KSP plugin..."
dotnet build "$SCRIPT_DIR/KrispyMPL/KrispyMPL.csproj" -c Release

echo "==> Packaging release v$VERSION..."
rm -rf "$RELEASE_DIR"
mkdir -p "$GAMEDATA_DIR"

cp "$SCRIPT_DIR/KrispyMPL/bin/Release/net472/KrispyMPL.dll" "$GAMEDATA_DIR/"
cp "$SCRIPT_DIR/KrispyMPL.version" "$GAMEDATA_DIR/KrispyMPL.version"

ZIP_NAME="KrispyMPL-v$VERSION.zip"
cd "$RELEASE_DIR"
zip -r "../$ZIP_NAME" GameData/
cd "$SCRIPT_DIR"

echo "==> Created $ZIP_NAME"
ls -lh "$ZIP_NAME"
