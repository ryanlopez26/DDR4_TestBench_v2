#!/bin/bash
# ============================================================
# setup-edf-tools.sh
# Recreates the /tools/edf directory with all required
# Yocto layers and EDF SDK for the ZCU104 DDR4 TestBench.
#
# Run this on a new machine or after /tools/edf is wiped
# to restore the full EDF build environment.
#
# Usage:
#   chmod +x setup-edf-tools.sh
#   ./setup-edf-tools.sh
#
# Requirements:
#   - Git installed
#   - Internet access
#   - ~20GB free space in /tools/edf for layers
#   - EDF SDK installer downloaded separately
#     (requires AMD account)
#
# After running this script:
#   1. Install EDF SDK manually:
#      ./amd-cortexa53-common_meta-edf-app-sdk*.sh \
#          -d /tools/edf/sdk -y
#   2. Start Docker container:
#      ./Docker/run-yocto.sh
#   3. Run bitbake build
# ============================================================

set -e  # Exit on any error

# ── Configuration ─────────────────────────────────────────────
EDF_ROOT="/tools/edf"
YOCTO_BRANCH="scarthgap"
XILINX_BRANCH="rel-v2025.2"

# Colours for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Colour

# ── Helper functions ──────────────────────────────────────────
log_info()    { echo -e "${BLUE}[INFO]${NC}  $1"; }
log_success() { echo -e "${GREEN}[OK]${NC}    $1"; }
log_warn()    { echo -e "${YELLOW}[WARN]${NC}  $1"; }
log_error()   { echo -e "${RED}[ERROR]${NC} $1"; exit 1; }

clone_or_update() {
    local url="$1"
    local branch="$2"
    local dest="$3"
    local name="$4"

    if [ -d "$dest/.git" ]; then
        log_warn "${name} already exists — checking branch..."
        current=$(git -C "$dest" rev-parse --abbrev-ref HEAD 2>/dev/null || echo "unknown")
        if [ "$current" = "$branch" ]; then
            log_info "Pulling latest ${name} (${branch})..."
            git -C "$dest" pull --ff-only || \
                log_warn "Could not pull ${name} — continuing with existing state"
        else
            log_warn "${name} is on branch '${current}', expected '${branch}' — skipping pull"
        fi
    else
        log_info "Cloning ${name} (${branch})..."
        git clone -b "$branch" "$url" "$dest"
        log_success "Cloned ${name}"
    fi
}

check_disk_space() {
    local required_gb=20
    local available_gb
    available_gb=$(df /tools 2>/dev/null | awk 'NR==2 {print int($4/1024/1024)}' || echo "unknown")

    if [ "$available_gb" = "unknown" ]; then
        log_warn "Could not check disk space — ensure at least ${required_gb}GB free in /tools"
    elif [ "$available_gb" -lt "$required_gb" ]; then
        log_error "Insufficient disk space: ${available_gb}GB available, ${required_gb}GB required"
    else
        log_info "Disk space OK: ${available_gb}GB available"
    fi
}

# ── Start ─────────────────────────────────────────────────────
echo ""
echo "============================================"
echo " EDF Tools Setup"
echo " Target: ${EDF_ROOT}"
echo " Yocto branch: ${YOCTO_BRANCH}"
echo " Xilinx branch: ${XILINX_BRANCH}"
echo "============================================"
echo ""

# ── Prerequisites ─────────────────────────────────────────────
log_info "Checking prerequisites..."

command -v git >/dev/null 2>&1 || \
    log_error "git not found. Install with: sudo pacman -S git"

check_disk_space

# ── Create directory structure ────────────────────────────────
log_info "Creating ${EDF_ROOT}..."
sudo mkdir -p "${EDF_ROOT}"
sudo chown $(whoami):$(whoami) "${EDF_ROOT}"

mkdir -p "${EDF_ROOT}/downloads"
mkdir -p "${EDF_ROOT}/sstate-cache"
mkdir -p "${EDF_ROOT}/sdk"

log_success "Directory structure created"

# ── Clone Yocto core ─────────────────────────────────────────
echo ""
log_info "=== Cloning Yocto core layers ==="

clone_or_update \
    "https://git.yoctoproject.org/poky" \
    "${YOCTO_BRANCH}" \
    "${EDF_ROOT}/poky" \
    "poky"

# ── Clone meta-arm ────────────────────────────────────────────
clone_or_update \
    "https://git.yoctoproject.org/meta-arm" \
    "${YOCTO_BRANCH}" \
    "${EDF_ROOT}/meta-arm" \
    "meta-arm"

# ── Clone meta-openembedded ───────────────────────────────────
echo ""
log_info "=== Cloning OpenEmbedded layers ==="

clone_or_update \
    "https://github.com/openembedded/meta-openembedded.git" \
    "${YOCTO_BRANCH}" \
    "${EDF_ROOT}/meta-openembedded" \
    "meta-openembedded"

# ── Clone meta-virtualization ─────────────────────────────────
clone_or_update \
    "https://git.yoctoproject.org/meta-virtualization" \
    "${YOCTO_BRANCH}" \
    "${EDF_ROOT}/meta-virtualization" \
    "meta-virtualization"

# ── Clone Xilinx / AMD layers ─────────────────────────────────
echo ""
log_info "=== Cloning AMD/Xilinx layers ==="

clone_or_update \
    "https://github.com/Xilinx/meta-xilinx.git" \
    "${XILINX_BRANCH}" \
    "${EDF_ROOT}/meta-xilinx" \
    "meta-xilinx"

clone_or_update \
    "https://github.com/Xilinx/meta-xilinx-tools.git" \
    "${XILINX_BRANCH}" \
    "${EDF_ROOT}/meta-xilinx-tools" \
    "meta-xilinx-tools"

clone_or_update \
    "https://github.com/Xilinx/meta-amd-edf.git" \
    "${XILINX_BRANCH}" \
    "${EDF_ROOT}/meta-amd-edf" \
    "meta-amd-edf"

clone_or_update \
    "https://github.com/Xilinx/meta-amd-adaptive-socs.git" \
    "${XILINX_BRANCH}" \
    "${EDF_ROOT}/meta-amd-adaptive-socs" \
    "meta-amd-adaptive-socs"

# ── Verify gen-machineconf ────────────────────────────────────
echo ""
log_info "=== Verifying gen-machineconf ==="

if [ -f "${EDF_ROOT}/gen-machine-conf/gen-machineconf" ]; then
    log_success "gen-machineconf found (standalone clone)"
elif [ -f "${EDF_ROOT}/meta-xilinx/meta-xilinx-core/gen-machine-conf/gen-machineconf" ]; then
    log_success "gen-machineconf found (inside meta-xilinx)"
else
    log_warn "gen-machineconf not found in expected locations"
    log_info "Cloning gen-machine-conf standalone..."
    clone_or_update \
        "https://github.com/Xilinx/gen-machine-conf.git" \
        "main" \
        "${EDF_ROOT}/gen-machine-conf" \
        "gen-machine-conf"
fi

# ── Verify ZCU104 template ────────────────────────────────────
echo ""
log_info "=== Verifying ZCU104 template ==="

TEMPLATE="${EDF_ROOT}/meta-amd-adaptive-socs/meta-amd-adaptive-socs-bsp/conf/machineyaml/zynqmp-zcu104-sdt-full.yaml"

if [ -f "$TEMPLATE" ]; then
    log_success "ZCU104 template found: ${TEMPLATE}"
else
    log_warn "ZCU104 template not found at expected path"
    log_info "Searching for template..."
    find "${EDF_ROOT}" -name "*zcu104*yaml" 2>/dev/null | \
        head -5 | while read f; do log_info "  Found: $f"; done
fi

# ── Check SDK ─────────────────────────────────────────────────
echo ""
log_info "=== Checking EDF SDK ==="

SDK_ENV="${EDF_ROOT}/sdk/environment-setup-cortexa72-cortexa53-amd-linux"

if [ -f "$SDK_ENV" ]; then
    log_success "EDF SDK found: ${SDK_ENV}"
else
    log_warn "EDF SDK not installed at ${EDF_ROOT}/sdk/"
    echo ""
    echo "  Install the SDK manually:"
    echo "  1. Download from AMD:"
    echo "     https://www.xilinx.com/support/download/index.html"
    echo "     → Embedded Design Tools → EDF → amd-cortexa53-common_meta-edf-app-sdk"
    echo ""
    echo "  2. Run installer:"
    echo "     chmod +x amd-cortexa53-common_meta-edf-app-sdk*.sh"
    echo "     ./amd-cortexa53-common_meta-edf-app-sdk*.sh -d ${EDF_ROOT}/sdk -y"
    echo ""
fi

# ── Summary ───────────────────────────────────────────────────
echo ""
echo "============================================"
echo " Setup Complete"
echo "============================================"
echo ""
echo " Layer summary:"
echo ""

for layer in poky meta-arm meta-openembedded meta-virtualization \
             meta-xilinx meta-xilinx-tools meta-amd-edf \
             meta-amd-adaptive-socs gen-machine-conf; do
    dir="${EDF_ROOT}/${layer}"
    if [ -d "${dir}/.git" ]; then
        branch=$(git -C "$dir" rev-parse --abbrev-ref HEAD 2>/dev/null || echo "unknown")
        commit=$(git -C "$dir" rev-parse --short HEAD 2>/dev/null || echo "unknown")
        echo -e "  ${GREEN}✓${NC} ${layer} (${branch} @ ${commit})"
    else
        echo -e "  ${RED}✗${NC} ${layer} (not found)"
    fi
done

echo ""
echo " Cache directories:"
echo "  DL_DIR    : ${EDF_ROOT}/downloads"
echo "  SSTATE_DIR: ${EDF_ROOT}/sstate-cache"
echo ""
echo " Next steps:"
echo "  1. Install EDF SDK if not already done"
echo "  2. Run: ./Docker/run-yocto.sh"
echo "  3. Source build environment:"
echo "     source /tools/edf/poky/oe-init-build-env \\"
echo "         /home/edf/projects/EDF/build/"
echo "  4. Build:"
echo "     MACHINE=zynqmp-zcu104-sdt-full bitbake xilinx-bootbin"
echo "============================================"
