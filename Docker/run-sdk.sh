#!/bin/bash
# ============================================================
# run-sdk.sh
# Starts the EDF Docker container configured for cross-
# compilation using the EDF SDK (AArch64 toolchain).
#
# Use this shell for:
#   - Cross-compiling F Prime flight software
#   - Building FpgaMgr, TestMgr and other application code
#   - Targeting AArch64 (Cortex-A53) binaries
#
# DO NOT use for BitBake builds — the SDK conflicts with
# oe-init-build-env. Use run-yocto.sh for that instead.
#
# Usage:
#   ./run-sdk.sh [working-directory]
#
# Example:
#   ./run-sdk.sh
#   ./run-sdk.sh /home/edf/projects/MemTester
# ============================================================

# ── Configuration ─────────────────────────────────────────────
IMAGE_NAME="edf-env"
HOST_TOOLS="/tools"
HOST_PROJECTS=".."
WORK_DIR="${1:-/home/edf/projects}"

# ── SDK environment setup ─────────────────────────────────────
SDK_ENV="/tools/edf/sdk/environment-setup-cortexa72-cortexa53-amd-linux"

# ── Check image exists ────────────────────────────────────────
if ! docker image inspect "$IMAGE_NAME" &>/dev/null; then
    echo "[ERROR] Docker image '${IMAGE_NAME}' not found."
    echo "        Build it first with: docker build -t ${IMAGE_NAME} ."
    exit 1
fi

# ── Check SDK exists ──────────────────────────────────────────
if [ ! -f "${HOST_TOOLS}/edf/sdk/environment-setup-cortexa72-cortexa53-amd-linux" ]; then
    echo "[ERROR] EDF SDK not found at: ${HOST_TOOLS}/edf/sdk/"
    echo "        Install it with:"
    echo "        ./amd-cortexa53-common_meta-edf-app-sdk*.sh -d /tools/edf/sdk -y"
    exit 1
fi

# ── Start container ───────────────────────────────────────────
echo "[INFO] Starting EDF SDK cross-compilation environment..."
echo "[INFO] Tools  : ${HOST_TOOLS}"
echo "[INFO] Projects: ${HOST_PROJECTS}"
echo "[INFO] SDK    : ${SDK_ENV}"
echo ""

docker run -it --rm \
    -v "${HOST_TOOLS}:/tools" \
    -v "${HOST_PROJECTS}:/home/edf/projects" \
    -w "${WORK_DIR}" \
    --hostname "sdk-build" \
    "$IMAGE_NAME" \
    bash --norc --noprofile -c "
        export HOME=/home/edf
        export USER=edf
        export TERM=xterm-256color
        export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin

        # Source EDF SDK
        if [ -f '${SDK_ENV}' ]; then
            source '${SDK_ENV}'
            SDK_OK=1
        else
            echo '[ERROR] SDK environment file not found: ${SDK_ENV}'
            SDK_OK=0
        fi

        # Print welcome
        echo ''
        echo '============================================'
        echo ' EDF SDK Cross-Compilation Environment'
        echo ' Target: AArch64 (Cortex-A53/A72)'
        echo '============================================'
        if [ \"\$SDK_OK\" = '1' ]; then
            echo ' CC  : '\"\$CC\"
            echo ' CXX : '\"\$CXX\"
            echo ' Sysroot: '\"\$SDKTARGETSYSROOT\"
            echo ''
            echo ' Quick test:'
            echo '   echo \"int main(){return 0;}\" > /tmp/t.c'
            echo '   \$CC /tmp/t.c -o /tmp/t && file /tmp/t'
            echo ''
            echo ' Cross-compile F Prime:'
            echo '   \$CXX -std=c++17 FpgaMgr.cpp TestMgr.cpp -o memtester'
        else
            echo ' [WARNING] SDK failed to load'
        fi
        echo '============================================'
        echo ''

        exec bash --norc --noprofile -i
    "
