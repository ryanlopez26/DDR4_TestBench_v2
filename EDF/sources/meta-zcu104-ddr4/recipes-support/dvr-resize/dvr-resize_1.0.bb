SUMMARY = "Grow the /dvr FAT32 partition to fill the SD card on first boot"
LICENSE = "MIT"
LIC_FILES_CHKSUM = "file://${COMMON_LICENSE_DIR}/MIT;md5=0835ade698e0bcf8506ecda2f7b4f302"

SRC_URI = " \
    file://dvr-grow \
    file://dvr-grow.service \
"

S = "${WORKDIR}"

inherit systemd

SYSTEMD_SERVICE:${PN} = "dvr-grow.service"

RDEPENDS:${PN} = " \
    dosfstools \
    util-linux-sfdisk \
    util-linux-partx \
    util-linux-blkid \
    util-linux-findmnt \
"

do_install() {
    install -d ${D}${sbindir}
    install -m 0755 ${WORKDIR}/dvr-grow ${D}${sbindir}/dvr-grow

    install -d ${D}${systemd_system_unitdir}
    install -m 0644 ${WORKDIR}/dvr-grow.service ${D}${systemd_system_unitdir}/
}
