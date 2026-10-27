#!/usr/bin/env bash
#
# Cross-compile the ZCU104 Rust server for aarch64-unknown-linux-gnu using
# a one-shot Ubuntu Docker container. Output lands in
#   target/aarch64-unknown-linux-gnu/release/
# on the host, owned by the host user.
#
# Usage:
#   ./build-cross.sh                            # cargo build --release ...
#   ./build-cross.sh cargo test --target ...    # any other cargo invocation
#   ./build-cross.sh sh                         # interactive shell in the container
#
# Requires: Docker (Docker Desktop on Mac/Windows, docker.io on Linux). On
# Windows, run from WSL or Git Bash — pure cmd.exe won't expand $(id -u).

set -euo pipefail

# --------------------------------------------------------------------------
# Configuration

IMAGE="zcu-rust-cross:latest"
DOCKERFILE_NAME="Dockerfile.cross"

# Where the Rust project lives. By default, the script assumes it sits next to
# the project's Cargo.toml. If you put the script in a subdirectory (e.g.
# zcu_app/scripts/build-cross.sh), point PROJECT_DIR at the right parent.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR"

# --------------------------------------------------------------------------
# Sanity checks

if ! command -v docker >/dev/null 2>&1; then
    echo "error: docker not found on PATH" >&2
    exit 1
fi

if [[ ! -f "$PROJECT_DIR/Cargo.toml" ]]; then
    echo "error: no Cargo.toml in $PROJECT_DIR" >&2
    echo "       Adjust PROJECT_DIR in this script if your layout differs." >&2
    exit 1
fi

if [[ ! -f "$SCRIPT_DIR/$DOCKERFILE_NAME" ]]; then
    echo "error: $DOCKERFILE_NAME not found next to this script" >&2
    exit 1
fi

# --------------------------------------------------------------------------
# Build the image (cached — only rebuilds if Dockerfile.cross changed).
# Piping the Dockerfile over stdin avoids sending the project as build context,
# which would be slow and unnecessary since the Dockerfile has no COPY steps.

echo "==> Building image $IMAGE"
docker build -t "$IMAGE" - < "$SCRIPT_DIR/$DOCKERFILE_NAME"

# --------------------------------------------------------------------------
# Run the cross-compile.
#
# --user      : files created in target/ are owned by the host user, not root.
# -e HOME     : when --user is a UID without an /etc/passwd entry, $HOME is
#               unset; some tools complain. /tmp is always writable.
# -v project  : bind-mount the source tree so the build output lands on the host.
# -v cargo-*  : named volumes for the cargo registry and git caches. Persist
#               between runs — huge speedup on warm builds.

echo "==> Cross-compiling for aarch64-unknown-linux-gnu"
docker run --rm -it \
    --user "$(id -u):$(id -g)" \
    -e HOME=/tmp \
    -v "$PROJECT_DIR:/work" \
    -v zcu-cargo-registry:/opt/cargo/registry \
    -v zcu-cargo-git:/opt/cargo/git \
    "$IMAGE" "$@"

# --------------------------------------------------------------------------
# Copy the built binary into the project root as `zcu_server`.
#
# Cargo's output lands in $OUT_DIR with no file extension — the .d files are
# dependency info and .rlib files are static libs, so the binary is the file
# without a dot in its name. Grab the first such file.

OUT_DIR="$PROJECT_DIR/target/aarch64-unknown-linux-gnu/release"
DEST="$PROJECT_DIR/zcu_server"

SRC_BIN=$(find "$OUT_DIR" -maxdepth 1 -type f ! -name '*.*' 2>/dev/null | head -n 1)

if [[ -z "$SRC_BIN" ]]; then
    echo "error: couldn't find a compiled binary in $OUT_DIR" >&2
    echo "       (build succeeded but no extensionless executable was produced)" >&2
    exit 1
fi

cp -f "$SRC_BIN" "$DEST"

# stat flags differ between GNU coreutils and BSD/macOS — try both.
BIN_SIZE=$(stat -c%s "$DEST" 2>/dev/null || stat -f%z "$DEST" 2>/dev/null || echo "?")
echo
echo "==> Built: $DEST  (${BIN_SIZE} bytes)"
