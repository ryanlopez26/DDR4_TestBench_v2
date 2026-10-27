SUMMARY = "Static network config for ZCU104"
LICENSE = "CLOSED"

SRC_URI = "file://eth0-static.network"

S = "${WORKDIR}"

RDEPENDS:${PN} = "systemd"

FILES:${PN} = "${sysconfdir}/systemd/network/eth0-static.network"

do_install() {
    install -d ${D}${sysconfdir}/systemd/network
    install -m 0644 ${WORKDIR}/eth0-static.network \
        ${D}${sysconfdir}/systemd/network/
}

pkg_postinst_ontarget:${PN}() {
    systemctl enable systemd-networkd.service
}