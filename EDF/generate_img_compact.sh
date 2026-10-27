#!/bin/bash
# ============================================================
# generate_img_compact.sh
# Generates a minimal-size flashable sdcard image.
#
# Unlike generate_img.sh (which produces a fixed ~6.1GB image
# matching the original SD card's 2GB+4GB partition layout),
# this version sizes both partitions to exactly fit their contents:
#
#   Partition 1: ~64-128MB FAT32 (BOOT - boot files + FAT32 headroom)
#   Partition 2: exact rootfs size from WIC (no padding)
#   Total:       typically ~1-1.5GB depending on rootfs contents
#
# Trade-off vs generate_img.sh:
#   + Image is 4-5x smaller — faster to write, flash, and distribute
#   + No resize2fs needed on first boot (rootfs already fills its partition)
#   - No spare space for adding files at runtime — if you need that,
#     either grow the partition manually later or use generate_img.sh
#
# Usage:
#   sh generate_img_compact.sh
#
# Output:
#   /home/edf/projects/EDF/sdcard_compact.img        (flash with Etcher)
#   /home/edf/projects/EDF/sdcard_compact.img.bmap   (for bmaptool)
# ============================================================

set -e

# ── Colours ───────────────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

log_info()    { echo -e "${BLUE}[INFO]${NC}  $1"; }
log_success() { echo -e "${GREEN}[OK]${NC}    $1"; }
log_warn()    { echo -e "${YELLOW}[WARN]${NC}  $1"; }
log_error()   { echo -e "${RED}[ERROR]${NC} $1"; exit 1; }

# ── Configuration ─────────────────────────────────────────────
BUILD_DIR="/home/edf/projects/EDF/build"
OUTPUT_DIR="/home/edf/projects/EDF"
MACHINE="zynqmp-zcu104-sdt-full"
IMAGE_NAME="core-image-minimal"
OUTPUT_IMG="${OUTPUT_DIR}/sdcard_compact.img"
OUTPUT_BMAP="${OUTPUT_DIR}/sdcard_compact.img.bmap"

DEPLOY="${BUILD_DIR}/tmp/deploy/images/${MACHINE}"

# BOOT partition starts at sector 8 (leaves room for MBR + a bit of slack).
# The other partition variables are computed in calculate_compact_sizes()
# after we know the actual boot-file and rootfs sizes.
BOOT_START=8

# mtools env var — skip geometry checks on image files
export MTOOLS_SKIP_CHECK=1

# ── Check required tools ──────────────────────────────────────
check_tools() {
    log_info "Checking required tools..."

    MISSING=0
    for tool in dd sfdisk mkfs.vfat mcopy mdir fdisk; do
        if ! command -v "$tool" >/dev/null 2>&1; then
            log_warn "Missing: ${tool}"
            MISSING=1
        fi
    done

    [ "$MISSING" = "1" ] && \
        log_error "Install missing tools:
        Ubuntu/Debian: sudo apt-get install -y dosfstools mtools fdisk util-linux
        Arch:          sudo pacman -S dosfstools mtools util-linux"

    command -v bmaptool >/dev/null 2>&1 && \
        HAVE_BMAPTOOL=1 || HAVE_BMAPTOOL=0

    log_success "Tools OK"
}

# ── Locate build outputs ──────────────────────────────────────
find_outputs() {
    log_info "Locating build outputs..."

    # WIC image (source of rootfs partition)
    WIC_IMAGE=$(ls "${DEPLOY}/${IMAGE_NAME}"*.rootfs.wic \
                   "${DEPLOY}/${IMAGE_NAME}"*.wic \
                   2>/dev/null | grep -v qemu | head -1)

    [ -n "$WIC_IMAGE" ] && [ -f "$WIC_IMAGE" ] || \
        log_error "WIC image not found in ${DEPLOY}/
        Run: MACHINE=${MACHINE} bitbake ${IMAGE_NAME}"

    # BOOT.BIN
    BOOT_BIN=$(ls "${DEPLOY}/BOOT.BIN" \
                  "${DEPLOY}/BOOT-${MACHINE}"*.bin \
                  "${DEPLOY}/boot.bin" \
                  2>/dev/null | head -1)

    [ -n "$BOOT_BIN" ] && [ -f "$BOOT_BIN" ] || \
        log_error "BOOT.BIN not found in ${DEPLOY}/
        Run: MACHINE=${MACHINE} bitbake xilinx-bootbin"

    # Kernel
    KERNEL="${DEPLOY}/Image"
    [ -f "$KERNEL" ] || \
        log_error "Kernel Image not found in ${DEPLOY}/"

    # Device tree — find non-empty file
    DTB=$(ls "${DEPLOY}/system.dtb" \
             "${DEPLOY}/${MACHINE}-system.dtb" \
             "${DEPLOY}/${MACHINE}-system-"*.dtb \
             2>/dev/null | head -1)

    [ -n "$DTB" ] || \
        log_error "system.dtb not found in ${DEPLOY}/"

    DTB_SIZE=$(stat -L -c%s "$DTB" 2>/dev/null || echo 0)
    if [ "$DTB_SIZE" -eq 0 ]; then
        log_warn "system.dtb symlink is empty — searching for real DTB..."
        DTB=$(ls -S "${DEPLOY}/${MACHINE}-system-"*.dtb 2>/dev/null | head -1)
        [ -n "$DTB" ] && [ -f "$DTB" ] || \
            log_error "Could not find a non-empty system.dtb"
    fi

    # boot.scr (optional)
    BOOTSCR=""
    [ -f "${DEPLOY}/boot.scr" ] && BOOTSCR="${DEPLOY}/boot.scr"

    log_success "All build outputs found"
    echo ""
    log_info "  WIC image : ${WIC_IMAGE}"
    log_info "  BOOT.BIN  : ${BOOT_BIN} ($(du -sh ${BOOT_BIN} | cut -f1))"
    log_info "  Kernel    : ${KERNEL} ($(du -sh ${KERNEL} | cut -f1))"
    log_info "  DTB       : ${DTB} ($(du -sh ${DTB} | cut -f1))"
    [ -n "$BOOTSCR" ] && log_info "  boot.scr  : ${BOOTSCR}"
}

# ── Get rootfs partition info from WIC ────────────────────────
get_wic_rootfs_info() {
    log_info "Reading WIC partition layout..."

    WIC_FDISK=$(fdisk -l "${WIC_IMAGE}" 2>/dev/null)
    echo "$WIC_FDISK"
    echo ""

    WIC_ROOTFS_START=$(echo "$WIC_FDISK" | \
        awk '/Linux/{
            if ($2 == "*") print $3
            else print $2
        }' | head -1)

    WIC_ROOTFS_SECTORS=$(echo "$WIC_FDISK" | \
        awk '/Linux/{
            if ($2 == "*") print $5
            else print $4
        }' | head -1)

    if [ -z "$WIC_ROOTFS_START" ] || [ -z "$WIC_ROOTFS_SECTORS" ]; then
        log_error "Could not parse rootfs partition from WIC image"
    fi

    log_info "WIC rootfs: start sector=${WIC_ROOTFS_START}, sectors=${WIC_ROOTFS_SECTORS}"
}

# ── NEW: Compute compact partition sizes ──────────────────────
# Run after find_outputs + get_wic_rootfs_info so we know how big
# everything is before we decide on partition sizes.
calculate_compact_sizes() {
    log_info "Computing compact partition sizes..."

    # Sum the boot files (BOOT.BIN + kernel + DTB + optional boot.scr).
    BOOT_FILES_BYTES=$(stat -c%s "${BOOT_BIN}" "${KERNEL}" "${DTB}" | \
                       awk '{s+=$1} END {print s}')
    if [ -n "$BOOTSCR" ]; then
        BOOT_FILES_BYTES=$(( BOOT_FILES_BYTES + $(stat -c%s "$BOOTSCR") ))
    fi

    BOOT_FILES_MB=$(( BOOT_FILES_BYTES / 1048576 + 1 ))   # round up

    # 50% headroom over the actual files for FAT32 overhead and any
    # late-stage additions; minimum 64MB because mkfs.vfat -F 32 wants
    # at least ~33MB and we want a comfortable safety margin.
    BOOT_MB=$(( BOOT_FILES_MB * 3 / 2 + 8 ))
    [ "$BOOT_MB" -lt 64 ] && BOOT_MB=64

    # Round up to the next 4MB boundary so partitions stay aligned.
    BOOT_MB=$(( (BOOT_MB + 3) / 4 * 4 ))

    BOOT_SECTORS=$(( BOOT_MB * 2048 ))            # 2048 sectors per MB at 512 B/sector
    ROOTFS_START=$(( BOOT_START + BOOT_SECTORS ))
    ROOTFS_SECTORS="${WIC_ROOTFS_SECTORS}"        # exact WIC size — no padding
    TOTAL_SECTORS=$(( ROOTFS_START + ROOTFS_SECTORS ))

    BOOT_OFFSET_BYTES=$(( BOOT_START * 512 ))

    ROOTFS_MB=$(( ROOTFS_SECTORS / 2048 ))
    TOTAL_MB=$(( TOTAL_SECTORS / 2048 ))

    log_success "Partition layout computed"
    log_info "  Boot files total : $(( BOOT_FILES_BYTES / 1048576 ))MB"
    log_info "  BOOT partition   : ${BOOT_MB}MB"
    log_info "  rootfs partition : ${ROOTFS_MB}MB (exact WIC size, no expansion)"
    log_info "  Total image      : ${TOTAL_MB}MB"
}

# ── Check disk space ──────────────────────────────────────────
check_space() {
    NEEDED_MB=$(( TOTAL_SECTORS / 2048 + 128 ))   # image + scratch for temp boot file
    AVAILABLE_MB=$(df "${OUTPUT_DIR}" | awk 'NR==2 {print int($4/1024)}')

    [ "$AVAILABLE_MB" -ge "$NEEDED_MB" ] || \
        log_error "Insufficient space: ${AVAILABLE_MB}MB available, ~${NEEDED_MB}MB needed"

    log_info "Disk space OK: ${AVAILABLE_MB}MB available"
}

# ── Step 1: Create blank image ────────────────────────────────
create_blank_image() {
    log_info "[1/5] Creating blank image (${TOTAL_MB}MB sparse)..."

    rm -f "${OUTPUT_IMG}"

    dd if=/dev/zero of="${OUTPUT_IMG}" \
        bs=512 count=0 seek="${TOTAL_SECTORS}" 2>/dev/null

    log_success "Blank image created ($(du -sh ${OUTPUT_IMG} | cut -f1) sparse)"
}

# ── Step 2: Create partition table ───────────────────────────
create_partitions() {
    log_info "[2/5] Creating partition table..."
    log_info "      BOOT:   sector ${BOOT_START} → $(( BOOT_START + BOOT_SECTORS - 1 )) ($(( BOOT_SECTORS / 2048 ))MB FAT32)"
    log_info "      rootfs: sector ${ROOTFS_START} → $(( ROOTFS_START + ROOTFS_SECTORS - 1 )) ($(( ROOTFS_SECTORS / 2048 ))MB Linux)"

    sfdisk "${OUTPUT_IMG}" << EOF
label: dos
unit: sectors

start=${BOOT_START},    size=${BOOT_SECTORS},   type=c, bootable
start=${ROOTFS_START},  size=${ROOTFS_SECTORS},  type=83
EOF

    log_success "Partition table written"
    fdisk -l "${OUTPUT_IMG}"
}

# ── Step 3: Format BOOT partition as FAT32 ───────────────────
format_boot_fat32() {
    log_info "[3/5] Formatting BOOT partition as FAT32..."

    BOOT_TMP=$(mktemp)
    trap "rm -f ${BOOT_TMP}" EXIT

    dd if=/dev/zero of="${BOOT_TMP}" \
        bs=512 count="${BOOT_SECTORS}" 2>/dev/null

    mkfs.vfat -F 32 -n "boot" "${BOOT_TMP}"

    dd if="${BOOT_TMP}" of="${OUTPUT_IMG}" \
        bs=512 seek="${BOOT_START}" conv=notrunc 2>/dev/null

    rm -f "${BOOT_TMP}"
    trap - EXIT

    log_success "BOOT partition formatted as FAT32"
}

# ── Step 4: Copy rootfs from WIC ─────────────────────────────
copy_rootfs() {
    log_info "[4/5] Copying rootfs from WIC image..."
    log_info "      Source: WIC sector ${WIC_ROOTFS_START} (${WIC_ROOTFS_SECTORS} sectors)"
    log_info "      Dest  : sdcard_compact.img sector ${ROOTFS_START}"
    log_info "      Note  : partition is sized exactly to the rootfs — no resize2fs needed"

    dd if="${WIC_IMAGE}" of="${OUTPUT_IMG}" \
        bs=512 \
        skip="${WIC_ROOTFS_START}" \
        seek="${ROOTFS_START}" \
        count="${WIC_ROOTFS_SECTORS}" \
        conv=notrunc \
        status=progress

    log_success "Rootfs copied"
}

# ── Step 5: Write boot files via mtools ──────────────────────
write_boot_files() {
    log_info "[5/5] Writing ZCU104 boot files to BOOT partition..."
    log_info "      Using mtools — no mount or loop devices needed"
    echo ""

    MTOOLS_IMG="${OUTPUT_IMG}@@${BOOT_OFFSET_BYTES}"

    log_info "  Writing BOOT.BIN..."
    mcopy -i "${MTOOLS_IMG}" -o "${BOOT_BIN}" ::BOOT.BIN
    log_success "  BOOT.BIN  ($(du -sh ${BOOT_BIN} | cut -f1))"

    log_info "  Writing Image (kernel)..."
    mcopy -i "${MTOOLS_IMG}" -o "${KERNEL}" ::Image
    log_success "  Image     ($(du -sh ${KERNEL} | cut -f1))"

    log_info "  Writing system.dtb..."
    mcopy -i "${MTOOLS_IMG}" -o "${DTB}" ::system.dtb
    log_success "  system.dtb ($(du -sh ${DTB} | cut -f1))"

    if [ -n "$BOOTSCR" ]; then
        log_info "  Writing boot.scr..."
        mcopy -i "${MTOOLS_IMG}" -o "${BOOTSCR}" ::boot.scr
        log_success "  boot.scr"
    fi

    echo ""
    log_info "  BOOT partition contents:"
    mdir -i "${MTOOLS_IMG}" :: | sed 's/^/    /'
}

# ── Generate bmap ─────────────────────────────────────────────
generate_bmap() {
    log_info "Generating bmap file for fast flashing..."

    if [ "$HAVE_BMAPTOOL" = "1" ]; then
        bmaptool create "${OUTPUT_IMG}" > "${OUTPUT_BMAP}"
        log_success "bmap generated: ${OUTPUT_BMAP}"
    else
        log_warn "bmaptool not found — skipping bmap generation"
        log_warn "Install: sudo apt-get install -y bmap-tools"
    fi
}

# ── Print summary ─────────────────────────────────────────────
print_summary() {
    echo ""
    echo "============================================"
    echo " Compact SD Card Image Ready"
    echo "============================================"
    echo ""
    log_success "Image : ${OUTPUT_IMG}"
    echo "        Size  : $(du -sh ${OUTPUT_IMG} | cut -f1)  (sparse)"
    echo "        Apparent : ${TOTAL_MB}MB"
    [ -f "$OUTPUT_BMAP" ] && \
        log_success "bmap  : ${OUTPUT_BMAP}"
    echo ""
    echo " Partition layout:"
    fdisk -l "${OUTPUT_IMG}" | grep -E "img[0-9]|Device"
    echo ""
    echo " Both partitions are exactly sized to their contents."
    echo " No resize2fs needed after first boot."
    echo ""
    echo " Flash options:"
    echo ""
    echo " 1. Balena Etcher (Windows/macOS/Linux):"
    echo "    Open Etcher → Select sdcard_compact.img → Flash"
    echo ""
    echo " 2. bmaptool (fastest, Linux):"
    echo "    sudo bmaptool copy ${OUTPUT_IMG} /dev/sdX"
    echo ""
    echo " 3. dd (fallback):"
    echo "    sudo dd if=${OUTPUT_IMG} of=/dev/sdX \\"
    echo "        bs=4M status=progress conv=fsync"
    echo ""
    echo " ZCU104 boot mode (SW6):"
    echo "    1=OFF 2=OFF 3=OFF 4=ON  → SD card boot"
    echo ""
    echo " Serial console: 115200 baud, /dev/ttyUSB1"
    echo "============================================"
}

# ── Main ──────────────────────────────────────────────────────
echo ""
echo "============================================"
echo " ZCU104 DDR4 TestBench — COMPACT SD Image"
echo "============================================"
echo ""

check_tools
find_outputs
get_wic_rootfs_info
calculate_compact_sizes      # ← computes BOOT_SECTORS, ROOTFS_*, TOTAL_SECTORS
check_space                  # ← runs after sizes are known
create_blank_image
create_partitions
format_boot_fat32
copy_rootfs
write_boot_files
generate_bmap
print_summary