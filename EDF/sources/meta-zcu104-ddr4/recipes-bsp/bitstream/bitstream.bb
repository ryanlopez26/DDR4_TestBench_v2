SUMMARY = "ZCU104 DDR4 Tester PL Bitstream"
LICENSE = "CLOSED"

SRC_URI = "file://system.bit"

FILES:${PN} = "/lib/firmware/system.bit"

do_install() {
    install -d ${D}/lib/firmware
    install -m 0644 ${WORKDIR}/system.bit \
        ${D}/lib/firmware/system.bit
}
