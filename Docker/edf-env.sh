#!/bin/bash
# ── Vivado ──────────────────────────────────────────────
VIVADO_SETTINGS=/tools/vivado/2025.2/Vivado/settings64.sh
if [ -f "$VIVADO_SETTINGS" ]; then
    source "$VIVADO_SETTINGS"
fi

# ── EDF SDK ──────────────────────────────────────────────
EDF_SDK=/tools/edf/sdk/environment-setup-cortexa72-cortexa53-amd-linux
if [ -f "$EDF_SDK" ]; then
    source "$EDF_SDK"
fi

# ── gen-machineconf ──────────────────────────────────────
GEN_CONF=/tools/edf/gen-machine-conf/
if [ -d "$GEN_CONF" ]; then
    export PATH=$GEN_CONF:$PATH
fi

# ── BitBake ──────────────────────────────────────────────
BITBAKE_BIN=/tools/edf/poky/bitbake/bin
if [ -d "$BITBAKE_BIN" ]; then
    export PATH=$BITBAKE_BIN:$PATH
    export PYTHONPATH=/tools/edf/poky/bitbake/lib:$PYTHONPATH
fi