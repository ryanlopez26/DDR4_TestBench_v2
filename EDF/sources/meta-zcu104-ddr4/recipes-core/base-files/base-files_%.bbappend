# Mountpoint and fstab entry for the /dvr data partition created by
# wic/zcu104-ddr4-dvr.wks (partition 3, FAT32, label DVR).
#
# Mounted by LABEL, not device node: the SD controller enumerates as mmcblk0 or
# mmcblk1 depending on slot and DT ordering, so /dev/mmcblk0p3 is not portable.

DVR_MOUNTPOINT ?= "/dvr"
DVR_LABEL ?= "DVR"

# FAT carries no permission bits, so ownership and mode are set here at mount
# time. umask=0022 is root-writable, world-readable; loosen to umask=0002 plus a
# gid= if zcu-server ever stops running as root. flush writes back dirty pages
# sooner, which matters for a card that gets yanked and carried to a Windows box.
# nofail keeps an absent or Windows-corrupted partition out of emergency mode.
DVR_MOUNT_OPTIONS ?= "rw,nofail,noatime,uid=0,gid=0,umask=0022,shortname=mixed,utf8,flush,x-systemd.device-timeout=15"

do_install:append() {
    install -d ${D}${DVR_MOUNTPOINT}

    printf '\n# DDR4 tester data volume (FAT32, readable on a Windows host)\n' \
        >> ${D}${sysconfdir}/fstab
    printf 'LABEL=%s\t%s\tvfat\t%s\t0\t2\n' \
        "${DVR_LABEL}" "${DVR_MOUNTPOINT}" "${DVR_MOUNT_OPTIONS}" \
        >> ${D}${sysconfdir}/fstab
}

FILES:${PN} += "${DVR_MOUNTPOINT}"
