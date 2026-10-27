#!/bin/bash
# ============================================================
# run-yocto.sh
# Starts the EDF Docker container configured for Yocto/BitBake
# builds. The EDF SDK is deliberately NOT sourced in this
# environment to avoid conflicts with oe-init-build-env.
#
# Use this shell for:
#   - Running gen-machineconf
#   - Running bitbake builds
#   - Configuring bblayers.conf and local.conf
#   - Managing Yocto layers
#
# Usage:
#   ./run-yocto.sh [project-build-dir]
#
# Example:
#   ./run-yocto.sh
#   ./run-yocto.sh /home/edf/projects/EDF/build-ddr4-testbench
# ============================================================

# ── Configuration ─────────────────────────────────────────────
IMAGE_NAME="edf-env"
HOST_TOOLS="/tools"
HOST_PROJECTS=".."
BUILD_DIR="${1:-/home/edf/projects/EDF/build-ddr4-testbench}"

# ── Inline bashrc for Yocto shell ─────────────────────────────
# SDK is intentionally excluded
YOCTO_BASHRC='
# ── Yocto shell (no SDK) ─────────────────────────────────────

# gen-machineconf
GEN_CONF=/tools/edf/gen-machine-conf
if [ -d "$GEN_CONF" ]; then
    export PATH=$GEN_CONF:$PATH
fi

# BitBake from poky
BITBAKE_BIN=/tools/edf/poky/bitbake/bin
if [ -d "$BITBAKE_BIN" ]; then
    export PATH=$BITBAKE_BIN:$PATH
    export PYTHONPATH=/tools/edf/poky/bitbake/lib:$PYTHONPATH
fi

# Yocto build environment
# Uncomment to auto-source a specific build dir:
# source /tools/edf/poky/oe-init-build-env '"$BUILD_DIR"'

echo ""
echo "============================================"
echo " Yocto / BitBake environment"
echo " EDF SDK is NOT active (by design)"
echo "============================================"
echo " To initialise a build directory:"
echo "   source /tools/edf/poky/oe-init-build-env \\"
echo "       /home/edf/projects/EDF/build-ddr4-testbench/"
echo ""
echo " To run gen-machineconf:"
echo "   gen-machineconf parse-sdt \\"
echo "       --template /tools/edf/meta-amd-adaptive-socs/meta-amd-adaptive-socs-bsp/conf/machineyaml/zynqmp-zcu104-sdt-full.yaml \\"
echo "       --hw-description /home/edf/projects/DDR4_TestBench/sdt_output/ \\"
echo "       -c conf -l conf/local.conf \\"
echo "       --machine-name zynqmp-zcu104-ddr4-custom -g full"
echo ""
echo " To build:"
echo "   MACHINE=zynqmp-zcu104-ddr4-custom bitbake xilinx-bootbin"
echo "   MACHINE=amd-cortexa53-common bitbake edf-linux-disk-image"
echo "============================================"
'

# ── Check image exists ────────────────────────────────────────
if ! docker image inspect "$IMAGE_NAME" &>/dev/null; then
    echo "[ERROR] Docker image '${IMAGE_NAME}' not found."
    echo "        Build it first with: docker build -t ${IMAGE_NAME} ."
    exit 1
fi

# ── Start container ───────────────────────────────────────────
echo "[INFO] Starting Yocto/BitBake environment..."
echo "[INFO] Tools : ${HOST_TOOLS}"
echo "[INFO] Projects: ${HOST_PROJECTS}"
echo ""

docker run -it --rm \
    -v "${HOST_TOOLS}:/tools" \
    -v "${HOST_PROJECTS}:/home/edf/projects" \
    -w "/home/edf/projects/EDF" \
    --hostname "yocto-build" \
    -e "BASH_ENV=/dev/null" \
    "$IMAGE_NAME" \
    bash --norc --noprofile -c "
        export HOME=/home/edf
        export USER=edf
        export TERM=xterm-256color
        # Set up minimal PATH
        export PATH=/tools/edf/gen-machine-conf:/tools/edf/poky/bitbake/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
        export PYTHONPATH=/tools/edf/poky/bitbake/lib:\$PYTHONPATH
        # Print welcome
        echo ''
        echo '============================================'
        echo ' Yocto / BitBake environment'
        echo ' EDF SDK is NOT active (by design)'
        echo '============================================'
        echo ' Initialise build dir:'
        echo '   source /tools/edf/poky/oe-init-build-env \\'
        echo '       /home/edf/projects/EDF/build-ddr4-testbench/'
        echo ''
        echo ' Build commands:'
        echo '   MACHINE=zynqmp-zcu104-ddr4-custom bitbake xilinx-bootbin'
        echo '   MACHINE=amd-cortexa53-common bitbake edf-linux-disk-image'
        echo '============================================'
        echo ''
        # Drop into interactive shell
        exec bash --norc --noprofile -i
    "
