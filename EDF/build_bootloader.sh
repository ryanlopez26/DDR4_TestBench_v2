cd build
MACHINE=zynqmp-zcu104-sdt-full bitbake -c cleansstate bitstream-manual xilinx-bootbin
MACHINE=zynqmp-zcu104-sdt-full bitbake xilinx-bootbin