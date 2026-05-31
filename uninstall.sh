#!/usr/bin/env bash
set -euo pipefail

MOD_NAME="KrispyMPL"
KSP_GAMEDATA="$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program/GameData/$MOD_NAME"

if [[ -d "$KSP_GAMEDATA" ]]; then
    rm -rf "$KSP_GAMEDATA"
    echo "Removed: $KSP_GAMEDATA"
else
    echo "Not installed: $KSP_GAMEDATA"
fi
