#!/usr/bin/env bash
# Builds the mod inside the Docker build environment defined by ./Dockerfile.
#
# The Slay the Spire 2 install is mounted read-only into the container so the build can
# reference sts2.dll / 0Harmony.dll without them ever being committed to this repository.
# Build output is written to ./artifacts/mods by default.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

IMAGE="${IMAGE:-sts2-the-architect-build}"
STS2_PATH="${STS2_PATH:-$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2}"
OUTPUT_DIR="${OUTPUT_DIR:-$ROOT_DIR/artifacts/mods}"
DOCKER="${DOCKER:-docker}"

if [[ ! -d "$STS2_PATH" ]]; then
    echo "Slay the Spire 2 not found at '$STS2_PATH'." >&2
    echo "Set STS2_PATH to the game install directory and try again." >&2
    exit 1
fi

mkdir -p "$OUTPUT_DIR"

echo "Building image '$IMAGE'"
"$DOCKER" build -t "$IMAGE" "$ROOT_DIR"

echo "Building mod in container (game mounted from '$STS2_PATH')"
"$DOCKER" run --rm \
    --user "$(id -u):$(id -g)" \
    -v "$ROOT_DIR:/mod" \
    -v "$STS2_PATH:/sts2:ro" \
    -v "$OUTPUT_DIR:/out" \
    -v "$IMAGE-nuget:/nuget-cache" \
    -e HOME=/tmp \
    -e STS2_PATH=/sts2 \
    -e STS2_DATA_DIR="${STS2_DATA_DIR:-}" \
    -e MODS_PATH=/out \
    "$IMAGE" "$@"

echo "Mod written to '$OUTPUT_DIR'"
