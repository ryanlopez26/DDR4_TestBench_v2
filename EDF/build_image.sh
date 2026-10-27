
cd build

# Copy built server executable into the needed bblayer
cp -f ../../zcu_app/zcu_server ../sources/meta-zcu104-ddr4/recipes-apps/zcu-server/files/zcu-server

#Ensure clean version of zcu server is installed
bitbake -c cleansstate zcu-server

# Build the image
MACHINE=zynqmp-zcu104-sdt-full bitbake core-image-minimal