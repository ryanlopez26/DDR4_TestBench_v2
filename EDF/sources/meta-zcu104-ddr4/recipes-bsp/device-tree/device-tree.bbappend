FILESEXTRAPATHS:prepend := "${THISDIR}/files:"

SRC_URI:append = " file://pl-ddr4.dtsi"

do_configure:append() {
    DTS_FILE="${B}/arch/arm64/boot/dts/xilinx/system-top.dts"
    if [ -f "$DTS_FILE" ]; then
        echo '#include "pl-ddr4.dtsi"' >> "$DTS_FILE"
    fi
}
