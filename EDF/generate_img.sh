#!/bin/bash
# ============================================================
# generate_img.sh
# Generates a flashable sdcard.img matching the original
# ZCU104 SD card partition layout:
#
#   Partition 1: 2GB  FAT32  (BOOT - kernel, DTB, BOOT.BIN)
#   Partition 2: 4GB  ext4   (Linux rootfs)
#   Total:       ~6.1GB
#
# Uses mtools to write FAT32 files without mount or loop
# devices. Works inside Docker without --privileged.
#
# Usage:
#   sh generate_img.sh
#
# Output:
#   /home/edf/projects/EDF/sdcard.img        (flash with Etcher)
#   /home/edf/projects/EDF/sdcard.img.bmap   (for bmaptool)
#
# Flash options:
#   Balena Etcher: Open → Select sdcard.img → Flash
#   bmaptool:      sudo bmaptool copy sdcard.img /dev/sdX
#   dd:            sudo dd if=sdcard.img of=/dev/sdX bs=4M status=progress
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
OUTPUT_IMG="${OUTPUT_DIR}/sdcard.img"
OUTPUT_BMAP="${OUTPUT_DIR}/sdcard.img.bmap"

DEPLOY="${BUILD_DIR}/tmp/deploy/images/${MACHINE}"

# ── Partition layout (matching original SD card) ───────────────
#   MBR/header     : sector 0-7      (8 sectors)
#   BOOT (FAT32)   : sector 8        to 4194311  (2GB  = 4194304 sectors)
#   rootfs (ext4)  : sector 4194312  to 12582919 (4GB  = 8388608 sectors)
BOOT_START=8
BOOT_SECTORS=4194304          # 2GB
ROOTFS_START=4194312
ROOTFS_SECTORS=8388608        # 4GB
TOTAL_SECTORS=12582920        # ~6.1GB total

BOOT_OFFSET_BYTES=$(( BOOT_START * 512 ))

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

    # If symlink resolves to empty, find the real timestamped one
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

# ── Check disk space ──────────────────────────────────────────
check_space() {
    # Need ~6.2GB for the output image
    NEEDED_MB=6300
    AVAILABLE_MB=$(df "${OUTPUT_DIR}" | awk 'NR==2 {print int($4/1024)}')

    [ "$AVAILABLE_MB" -ge "$NEEDED_MB" ] || \
        log_error "Insufficient space: ${AVAILABLE_MB}MB available, ~${NEEDED_MB}MB needed"

    log_info "Disk space OK: ${AVAILABLE_MB}MB available"
}

# ── Get rootfs partition info from WIC ────────────────────────
get_wic_rootfs_info() {
    log_info "Reading WIC partition layout..."

    WIC_FDISK=$(fdisk -l "${WIC_IMAGE}" 2>/dev/null)
    echo "$WIC_FDISK"
    echo ""

    # Get rootfs (Linux) partition start and size from WIC
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

# ── Step 1: Create blank image ────────────────────────────────
create_blank_image() {
    log_info "[1/5] Creating blank image ($(( TOTAL_SECTORS * 512 / 1024 / 1024 ))MB)..."
    log_info "      This matches the original SD card partition layout"

    # Remove old image if exists
    rm -f "${OUTPUT_IMG}"

    # Create sparse file — fast, only allocates space as data is written
    dd if=/dev/zero of="${OUTPUT_IMG}" \
        bs=512 count=0 seek="${TOTAL_SECTORS}" 2>/dev/null

    log_success "Blank image created ($(du -sh ${OUTPUT_IMG} | cut -f1) sparse)"
}

# ── Step 2: Create partition table ───────────────────────────
create_partitions() {
    log_info "[2/5] Creating partition table (matching original layout)..."
    log_info "      BOOT:   sector ${BOOT_START} → $(( BOOT_START + BOOT_SECTORS - 1 )) (2GB FAT32)"
    log_info "      rootfs: sector ${ROOTFS_START} → $(( ROOTFS_START + ROOTFS_SECTORS - 1 )) (4GB Linux)"

    # Use sfdisk for scriptable partition creation
    sfdisk "${OUTPUT_IMG}" << EOF
label: dos
unit: sectors

start=${BOOT_START},    size=${BOOT_SECTORS},   type=c, bootable
start=${ROOTFS_START},  size=${ROOTFS_SECTORS},  type=83
EOF

    log_success "Partition table written"

    # Verify
    fdisk -l "${OUTPUT_IMG}"
}

# ── Step 3: Format BOOT partition as FAT32 ───────────────────
format_boot_fat32() {
    log_info "[3/5] Formatting BOOT partition as FAT32..."

    # Extract the BOOT region into a temporary file for formatting
    BOOT_TMP=$(mktemp)
    trap "rm -f ${BOOT_TMP}" EXIT

    # Create a blank file the size of the BOOT partition
    dd if=/dev/zero of="${BOOT_TMP}" \
        bs=512 count="${BOOT_SECTORS}" 2>/dev/null

    # Format as FAT32
    mkfs.vfat -F 32 -n "boot" "${BOOT_TMP}"

    # Write formatted FAT32 back into the correct offset of the image
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
    log_info "      Dest  : sdcard.img sector ${ROOTFS_START}"
    log_info "      Note  : rootfs will be smaller than 4GB partition"
    log_info "              Run 'resize2fs /dev/mmcblk0p2' on ZCU104 to expand"

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

    # mtools image spec: image@@byte_offset
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
    echo " SD Card Image Ready"
    echo "============================================"
    echo ""
    log_success "Image : ${OUTPUT_IMG}"
    echo "        Size  : $(du -sh ${OUTPUT_IMG} | cut -f1)"
    [ -f "$OUTPUT_BMAP" ] && \
        log_success "bmap  : ${OUTPUT_BMAP}"
    echo ""
    echo " Partition layout:"
    fdisk -l "${OUTPUT_IMG}" | grep -E "img[0-9]|Device"
    echo ""
    echo " Flash options:"
    echo ""
    echo " 1. Balena Etcher (Windows/macOS/Linux):"
    echo "    Open Etcher → Select sdcard.img → Flash"
    echo ""
    echo " 2. bmaptool (fastest, Linux):"
    echo "    sudo bmaptool copy ${OUTPUT_IMG} /dev/sdX"
    echo ""
    echo " 3. dd (fallback):"
    echo "    sudo dd if=${OUTPUT_IMG} of=/dev/sdX \\"
    echo "        bs=4M status=progress conv=fsync"
    echo ""
    echo " After first boot — expand rootfs to fill 4GB:"
    echo "    resize2fs /dev/mmcblk0p2"
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
echo " ZCU104 DDR4 TestBench SD Card Generator"
echo "============================================"
echo ""

check_tools
find_outputs
check_space
get_wic_rootfs_info
create_blank_image
create_partitions
format_boot_fat32
copy_rootfs
write_boot_files
generate_bmap
print_summary
