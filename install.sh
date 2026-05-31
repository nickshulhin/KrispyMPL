#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
MOD_NAME="KrispyMPL"
KSP_BASE="$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
KSP_GAMEDATA="$KSP_BASE/GameData/$MOD_NAME"

echo "==> Building $MOD_NAME..."
dotnet build "$SCRIPT_DIR/$MOD_NAME/$MOD_NAME.csproj" -c Release

echo "==> Installing to KSP GameData..."
rm -rf "$KSP_GAMEDATA"
mkdir -p "$KSP_GAMEDATA"
cp "$SCRIPT_DIR/$MOD_NAME/bin/Release/net472/$MOD_NAME.dll" "$KSP_GAMEDATA/"
cp "$SCRIPT_DIR/GameData/$MOD_NAME/$MOD_NAME.version"         "$KSP_GAMEDATA/"

echo "==> Installed:"
ls -lh "$KSP_GAMEDATA/"
echo
echo "  Restart KSP to load the plugin."
