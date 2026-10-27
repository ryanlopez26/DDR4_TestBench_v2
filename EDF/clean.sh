cd build
MACHINE=zynqmp-zcu104-sdt-full bitbake -c cleansstate xilinx-bootbin
MACHINE=zynqmp-zcu104-sdt-full bitbake -c cleansstate core-image-minimal