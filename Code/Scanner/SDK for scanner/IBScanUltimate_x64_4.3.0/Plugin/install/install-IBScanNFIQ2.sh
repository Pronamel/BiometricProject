#!/usr/bin/env bash
set -euo pipefail

###############################################################################
# install-IBScanNFIQ2.sh
#
# Layout:
#   <ROOT>/
#     install/install-IBScanNFIQ2.sh
#     lib/libIBScanNFIQ2.so
#
# Behavior:
#   - Copies required .so files from ../lib to /usr/lib using sudo
###############################################################################

# 1) Pre-check
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
LIB_DIR="$ROOT_DIR/lib"
DEST_DIR="/usr/lib"

if [[ ! -d "$LIB_DIR" ]]; then
  echo "[ERROR] Library directory not found: $LIB_DIR"
  exit 1
fi

# 2) Required libraries
#    (Easy to add or remove items from this list)
REQUIRED_LIBS=(
  "libIBScanNFIQ2.so"
  # Add more libraries here if needed, for example:
  # "libAnotherLibrary.so"
)

# 3) Copy libraries to /usr/lib
echo "[INFO] Installing libraries"
echo "       Source      : $LIB_DIR"
echo "       Destination : $DEST_DIR"
echo

copied=0
missing=0

for lib in "${REQUIRED_LIBS[@]}"; do
  src="$LIB_DIR/$lib"
  if [[ -e "$src" ]]; then
    echo "[COPY] $lib -> $DEST_DIR"
    sudo cp -av "$src" "$DEST_DIR/"
    ((copied++))
  else
    echo "[MISSING] $lib (not found: $src)"
    ((missing++))
  fi
done

# 4) Completion / result message
echo
if [[ $missing -eq 0 ]]; then
  echo "[DONE] Installation completed successfully. Copied: $copied"
  exit 0
else
  echo "[DONE] Installation completed with missing files. Copied: $copied, Missing: $missing"
  exit 2
fi

