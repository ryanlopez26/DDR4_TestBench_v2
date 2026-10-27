FROM ubuntu:22.04

# ──────────────────────────────────────────
# Environment
# ──────────────────────────────────────────
ENV DEBIAN_FRONTEND=noninteractive
ENV LANG=en_US.UTF-8
ENV LANGUAGE=en_US:en
ENV LC_ALL=en_US.UTF-8

# ──────────────────────────────────────────
# Fix default shell (required by Yocto)
# ──────────────────────────────────────────
RUN ln -sf /bin/bash /bin/sh

# ──────────────────────────────────────────
# Locale (required by EDF SDK installer)
# ──────────────────────────────────────────
RUN apt-get update && \
    apt-get install -y locales && \
    locale-gen en_US.UTF-8 && \
    update-locale LANG=en_US.UTF-8

# ──────────────────────────────────────────
# System dependencies
# ──────────────────────────────────────────
RUN dpkg --add-architecture i386 && \
    apt-get update && \
    apt-get install -y \
        # Core build tools
        gawk wget git diffstat unzip texinfo \
        gcc g++ build-essential chrpath socat cpio \
        make cmake ninja-build \
        # Python
        python3 python3-pip python3-pexpect \
        python3-git python3-jinja2 python3-subunit \
        python3-yaml python3-jsonschema \
        python3-setuptools python3-wheel \
        # BitBake specific
        python3-ply python3-progressbar \
        # Compression / archive
        xz-utils zstd liblz4-tool file \
        # System utilities
        debianutils iputils-ping iproute2 \
        lsb-release sudo curl rsync \
        # 32-bit support
        zlib1g:i386 \
        # Display / ncurses (menuconfig)
        libncurses5-dev libncursesw5-dev \
        libncurses5 libtinfo-dev \
        xterm screen \
        # SDL / graphics
        libegl1-mesa libsdl1.2-dev mesa-common-dev \
        # Security / ACL
        libacl1 libssl-dev libselinux1 \
        # Build system tools
        autoconf automake libtool \
        flex bison libglib2.0-dev \
        # Embedded tools
        u-boot-tools device-tree-compiler \
        bc gnupg pax \
        # ── SD card image generation ──────────────────────────
        # dosfstools  → mkfs.vfat for FAT32 BOOT partition
        # mtools      → mcopy for copying files into FAT images
        # bmap-tools  → bmaptool for fast image flashing
        # util-linux  → losetup, fdisk, mount for partition ops
        # parted      → partition table inspection
        fdisk \
        dosfstools \
        mtools \
        bmap-tools \
        util-linux \
        mount \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*
    

# ──────────────────────────────────────────
# Python packages
# ──────────────────────────────────────────
RUN pip3 install \
    pyyaml \
    jinja2 \
    pexpect \
    gitpython \
    kas \
    jsonschema \
    ply \
    progressbar2

# ──────────────────────────────────────────
# Non-root user matching host UID
# Run: id -u on your host to get UID
# ──────────────────────────────────────────
ARG HOST_UID=1000
RUN useradd -ms /bin/bash -u ${HOST_UID} edf && \
    echo "edf ALL=(ALL) NOPASSWD:ALL" >> /etc/sudoers

# ──────────────────────────────────────────
# Directory structure
# /tools        → AMD tools + EDF (mounted from host)
# ~/projects    → project workspace (mounted from host)
# ──────────────────────────────────────────
RUN mkdir -p /tools && \
    mkdir -p /home/edf/projects && \
    chown -R edf:edf /home/edf

VOLUME ["/tools", "/home/edf/projects"]

# ──────────────────────────────────────────
# Switch to non-root user
# ──────────────────────────────────────────
USER edf
WORKDIR /home/edf

# ──────────────────────────────────────────
# Shell environment
# ──────────────────────────────────────────
COPY --chown=edf:edf edf-env.sh /home/edf/.edf-env.sh
RUN echo 'source /home/edf/.edf-env.sh' >> /home/edf/.bashrc && \
    echo 'source /home/edf/.edf-env.sh' >> /home/edf/.bash_profile && \
    echo 'source /home/edf/.edf-env.sh' >> /home/edf/.profile

# Apply to login shells as well
RUN cp /home/edf/.bashrc /home/edf/.bash_profile && \
    cp /home/edf/.bashrc /home/edf/.profile

CMD ["/bin/bash", "--login"]