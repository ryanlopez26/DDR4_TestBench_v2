# from EDF/ (or wherever your sources/ and build/ live)
source sources/poky/oe-init-build-env build/

# now you're inside build/ — paths shift one level deeper
gen-machineconf --soc-family zynqmp \
    --hw-description ../../Vivado/sdt_output \
    --machine-name zynqmp-zcu104-ddr4 \
    --output conf