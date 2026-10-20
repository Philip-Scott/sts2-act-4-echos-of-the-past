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
REUSE_IMAGE="${REUSE_IMAGE:-false}"
RUN_TESTS=false

if [[ "${1:-}" == "--help" ]]; then
    echo "Usage: [DOCKER=podman] [REUSE_IMAGE=true] [IMAGE=name] [OUTPUT_DIR=path] scripts/docker-build.sh [--test] [arguments]"
    echo "--test runs both C# test projects instead of building the mod; arguments go to dotnet run."
    echo "CONTAINER_HOME defaults to OUTPUT_DIR.container-home, outside the runtime bundle."
    echo "REUSE_IMAGE=true requires a local image and never builds or pulls it."
    exit 0
fi
if [[ "${1:-}" == "--test" ]]; then
    RUN_TESTS=true
    shift
fi
if [[ "$REUSE_IMAGE" != true && "$REUSE_IMAGE" != false ]]; then
    echo "REUSE_IMAGE must be true or false." >&2
    exit 1
fi

if [[ ! -d "$STS2_PATH" ]]; then
    echo "Slay the Spire 2 not found at '$STS2_PATH'." >&2
    echo "Set STS2_PATH to the game install directory and try again." >&2
    exit 1
fi

if [[ "$REUSE_IMAGE" == true ]]; then
    "$DOCKER" image inspect "$IMAGE" >/dev/null
else
    echo "Building image '$IMAGE'"
    "$DOCKER" build -t "$IMAGE" "$ROOT_DIR"
fi
mkdir -p "$OUTPUT_DIR"
OUTPUT_DIR="$(cd "$OUTPUT_DIR" && pwd)"
CONTAINER_HOME="${CONTAINER_HOME:-$OUTPUT_DIR.container-home}"
mkdir -p "$CONTAINER_HOME"
CONTAINER_HOME="$(cd "$CONTAINER_HOME" && pwd)"
if [[ "$CONTAINER_HOME/" == "$OUTPUT_DIR/"* ]]; then
    echo "CONTAINER_HOME must be outside OUTPUT_DIR so caches cannot enter the runtime bundle." >&2
    exit 1
fi
USER_ARGS=(--user "$(id -u):$(id -g)")
CACHE_ARGS=(-v "${IMAGE//[^a-zA-Z0-9_.-]/_}-nuget:/nuget-cache")
if [[ "$(basename "$DOCKER")" == podman ]]; then
    USER_ARGS=(--userns=keep-id --security-opt label=disable)
    CACHE_ARGS=(-e NUGET_PACKAGES=/container-home/.nuget/packages)
fi

COMMAND=("$IMAGE" "$@")
if [[ "$RUN_TESTS" == true ]]; then
    COMMAND=(--entrypoint /bin/bash "$IMAGE" -c '
        set -euo pipefail
        data="${STS2_DATA_DIR:-/sts2/data_sts2_linuxbsd_x86_64}"
        for assembly in sts2.dll 0Harmony.dll GodotSharp.dll; do
            if [[ ! -f "$data/$assembly" ]]; then
                echo "Missing game assembly: $data/$assembly. Check STS2_PATH and STS2_DATA_DIR." >&2
                exit 1
            fi
        done
        for project in tests/CorruptedPlayer/CorruptedPlayer.Tests.csproj tests/Architect/Architect.Tests.csproj; do
            dotnet run --project "$project" -p:Sts2Path=/sts2 "-p:Sts2DataDir=$data" "$@"
        done
    ' -- "$@")
    echo "Running C# tests in container (game mounted from '$STS2_PATH')"
else
    echo "Building mod in container (game mounted from '$STS2_PATH')"
fi
"$DOCKER" run --rm --pull=never \
    "${USER_ARGS[@]}" \
    "${CACHE_ARGS[@]}" \
    -v "$ROOT_DIR:/mod" \
    -v "$STS2_PATH:/sts2:ro" \
    -v "$OUTPUT_DIR:/out" \
    -v "$CONTAINER_HOME:/container-home" \
    -e HOME=/container-home \
    -e DOTNET_CLI_HOME=/container-home \
    -e STS2_PATH=/sts2 \
    -e STS2_DATA_DIR="${STS2_DATA_DIR:-}" \
    -e MODS_PATH=/out \
    "${COMMAND[@]}"

if [[ "$RUN_TESTS" == true ]]; then
    echo "C# tests passed."
else
    echo "Mod written to '$OUTPUT_DIR'"
fi
