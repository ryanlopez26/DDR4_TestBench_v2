SUMMARY = "Manually-supplied PL bitstream for ZCU104 (bypasses sdt-artifacts)"
DESCRIPTION = "Takes a System_wrapper.bit dropped in files/ and installs it \
where xilinx-bootbin expects to find the PL bitstream. Provides virtual/bitstream \
in place of the upstream bitstream_1.0.bb recipe."
LICENSE = "CLOSED"

PROVIDES = "virtual/bitstream"

# Bitstream is machine-specific (built against a specific FPGA design). Without
# this, the recipe gets built once for the tune (cortexa72-cortexa53) and shared
# across machines, which puts it in the wrong sysroot for xilinx-bootbin to find.
PACKAGE_ARCH = "${MACHINE_ARCH}"

SRC_URI = "file://System_wrapper.bit"
S = "${WORKDIR}"

# This recipe has no sources to compile or configure — it's just a file-copy.
INHIBIT_DEFAULT_DEPS = "1"
DEPENDS = ""
do_compile[noexec] = "1"
do_configure[noexec] = "1"

# `inherit deploy` is what defines DEPLOYDIR and registers do_deploy properly.
# Without it, ${DEPLOYDIR} expands empty and `install -d` fails with no operand.
inherit deploy

# xilinx-bootbin reads BIF_PARTITION_IMAGE[bitstream], which (in meta-xilinx-core
# 1.0) resolves to ${RECIPE_SYSROOT}/boot/bitstream/download-${MACHINE}.bit.
# Install our manually-supplied bitstream at exactly that path so the upstream
# recipe finds it during its do_configure.
do_install() {
    install -d ${D}/boot/bitstream
    install -m 0644 ${WORKDIR}/System_wrapper.bit \
                    ${D}/boot/bitstream/download-${MACHINE}.bit
}

FILES:${PN} = "/boot/bitstream/download-${MACHINE}.bit"

# Make the file visible in dependent recipes' sysroots.
SYSROOT_DIRS += "/boot/bitstream"

# Also deploy a copy for inspection / SD-image fallback paths.
do_deploy() {
    install -d ${DEPLOYDIR}
    install -m 0644 ${WORKDIR}/System_wrapper.bit ${DEPLOYDIR}/System_wrapper.bit
}
addtask deploy after do_install before do_build

# Skip QA checks that don't apply to a prebuilt binary blob.
INSANE_SKIP:${PN} += "already-stripped arch"
