#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
MOD_NAME="KrispyMPL"

# ---------------------------------------------------------------------
# Read version from the KSP-AVC .version file
# ---------------------------------------------------------------------
VERSION_FILE="$SCRIPT_DIR/GameData/$MOD_NAME/$MOD_NAME.version"
VERSION=$(python3 -c "
import json
with open('$VERSION_FILE') as f:
    v = json.load(f)['VERSION']
    print(f\"{v['MAJOR']}.{v['MINOR']}.{v['PATCH']}.{v['BUILD']}\")
")

echo "===================================================================="
echo "  $MOD_NAME Release Script"
echo "  Version: $VERSION"
echo "===================================================================="

# ---------------------------------------------------------------------
# Step 1: Build the plugin
# ---------------------------------------------------------------------
echo
echo "[1/5] Building KSP plugin..."
dotnet build "$SCRIPT_DIR/KrispyMPL/KrispyMPL.csproj" -c Release
DLL="$SCRIPT_DIR/KrispyMPL/bin/Release/net472/$MOD_NAME.dll"
if [[ ! -f "$DLL" ]]; then
    echo "ERROR: Build failed — $DLL not found"
    exit 1
fi

# ---------------------------------------------------------------------
# Step 2: Assemble release directory
# ---------------------------------------------------------------------
echo "[2/5] Assembling release structure..."
RELEASE_DIR="$SCRIPT_DIR/release"
rm -rf "$RELEASE_DIR"
GAMEDATA_DIR="$RELEASE_DIR/GameData/$MOD_NAME"
mkdir -p "$GAMEDATA_DIR"

cp "$DLL"                                "$GAMEDATA_DIR/"
cp "$SCRIPT_DIR/GameData/$MOD_NAME/$MOD_NAME.version" "$GAMEDATA_DIR/"

# ---------------------------------------------------------------------
# Step 3: Create zip
# ---------------------------------------------------------------------
ZIP_NAME="${MOD_NAME}-${VERSION}.zip"
ZIP_PATH="$SCRIPT_DIR/$ZIP_NAME"

echo "[3/5] Creating $ZIP_NAME..."
rm -f "$ZIP_PATH"
cd "$RELEASE_DIR"
zip -q -r "$ZIP_PATH" GameData/
cd "$SCRIPT_DIR"

# ---------------------------------------------------------------------
# Step 4: Compute checksums
# ---------------------------------------------------------------------
echo "[4/5] Computing checksums..."
SHA1=$(sha1sum "$ZIP_PATH" | awk '{print $1}')
SHA256=$(sha256sum "$ZIP_PATH" | awk '{print $1}')
SIZE=$(stat --printf="%s" "$ZIP_PATH")

# ---------------------------------------------------------------------
# Step 5: Print results
# ---------------------------------------------------------------------
echo
echo "===================================================================="
echo "  Release v$VERSION created"
echo "===================================================================="
echo "  File:      $ZIP_NAME"
echo "  Size:      $SIZE bytes"
echo "  SHA1:      $SHA1"
echo "  SHA256:    $SHA256"
echo
echo "  CKAN snippet:"
echo "  {"
echo "    \"version\": \"$VERSION\","
echo "    \"download\": \"https://github.com/nickshulhin/$MOD_NAME/releases/download/v$VERSION/$ZIP_NAME\","
echo "    \"download_size\": $SIZE,"
echo "    \"download_hash\": {"
echo "      \"sha1\": \"${SHA1^^}\","
echo "      \"sha256\": \"${SHA256^^}\""
echo "    },"
echo "    \"download_content_type\": \"application/zip\""
echo "  }"
echo
echo "===================================================================="
echo "  Usage:"
echo "    gh release create v$VERSION $ZIP_NAME --title \"v$VERSION\""
echo "===================================================================="
