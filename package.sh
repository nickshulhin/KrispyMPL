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
echo "[1/6] Building KSP plugin..."
dotnet build "$SCRIPT_DIR/KrispyMPL/KrispyMPL.csproj" -c Release
DLL="$SCRIPT_DIR/KrispyMPL/bin/Release/net472/$MOD_NAME.dll"
if [[ ! -f "$DLL" ]]; then
    echo "ERROR: Build failed — $DLL not found"
    exit 1
fi

# ---------------------------------------------------------------------
# Step 2: Assemble release directory
# ---------------------------------------------------------------------
echo "[2/6] Assembling release structure..."
RELEASE_DIR="$SCRIPT_DIR/release"
rm -rf "$RELEASE_DIR"
GAMEDATA_DIR="$RELEASE_DIR/GameData/$MOD_NAME"
mkdir -p "$GAMEDATA_DIR"

cp "$DLL"                                                 "$GAMEDATA_DIR/"
cp "$SCRIPT_DIR/GameData/$MOD_NAME/$MOD_NAME.version"      "$GAMEDATA_DIR/"
cp -r "$SCRIPT_DIR/$MOD_NAME/assets"                       "$GAMEDATA_DIR/" 2>/dev/null || true

# ---------------------------------------------------------------------
# Step 3: Create dist directory and zip
# ---------------------------------------------------------------------
ZIP_NAME="${MOD_NAME}-${VERSION}.zip"
DIST_DIR="$SCRIPT_DIR/dist"
mkdir -p "$DIST_DIR"
ZIP_PATH="$DIST_DIR/$ZIP_NAME"

echo "[3/6] Creating $DIST_DIR/$ZIP_NAME..."
rm -f "$ZIP_PATH"
cd "$RELEASE_DIR"
zip -q -r "$ZIP_PATH" GameData/
cd "$SCRIPT_DIR"

# ---------------------------------------------------------------------
# Step 4: Verify zip contents
# ---------------------------------------------------------------------
echo "[4/6] Verifying zip contents..."
echo "  Archive contents:"
unzip -l "$ZIP_PATH" | tail -n +4 | sed 's/^/    /'

# ---------------------------------------------------------------------
# Step 5: Compute checksums
# ---------------------------------------------------------------------
echo "[5/6] Computing checksums..."
SHA1=$(sha1sum "$ZIP_PATH" | awk '{print $1}')
SHA256=$(sha256sum "$ZIP_PATH" | awk '{print $1}')
SIZE=$(stat --printf="%s" "$ZIP_PATH")

# ---------------------------------------------------------------------
# Step 6: Print results
# ---------------------------------------------------------------------
echo
echo "===================================================================="
echo "  Release v$VERSION"
echo "===================================================================="
echo "  Archive:   dist/$ZIP_NAME"
echo "  Size:      $SIZE bytes"
echo "  SHA1:      $SHA1"
echo "  SHA256:    $SHA256"
echo
echo "  CKAN metadata:"
echo "  {"
echo "    \"version\": \"$VERSION\","
echo "    \"download\": \"https://github.com/nickshulhin/$MOD_NAME/releases/download/alpha/$ZIP_NAME\","
echo "    \"download_size\": $SIZE,"
echo "    \"download_hash\": {"
echo "      \"sha1\": \"${SHA1^^}\","
echo "      \"sha256\": \"${SHA256^^}\""
echo "    },"
echo "    \"download_content_type\": \"application/zip\""
echo "  }"
echo
echo "  GitHub release:"
echo "    gh release create alpha dist/$ZIP_NAME --title \"alpha\" --prerelease"
echo "===================================================================="
