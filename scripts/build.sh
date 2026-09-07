#!/usr/bin/env bash
# Builds The Architect mod with a single command.
#
# The mod compiles against sts2.dll and 0Harmony.dll, which ship with the game and
# cannot be redistributed. Point the script at a Slay the Spire 2 install (or at a
# folder containing those assemblies) using the options/environment variables below.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT="$ROOT_DIR/TheArchitect.csproj"

CONFIGURATION="${CONFIGURATION:-Debug}"
STS2_PATH="${STS2_PATH:-}"
STS2_DATA_DIR="${STS2_DATA_DIR:-}"
MODS_PATH="${MODS_PATH:-}"
GODOT_BIN="${GODOT_BIN:-}"
PUBLISH=false
EXTRA_ARGS=()

usage() {
    cat <<'EOF'
Usage: scripts/build.sh [options] [-- <extra dotnet args>]

Options:
  -c, --configuration <name>  Build configuration (default: Debug).
  -s, --sts2-path <path>      Slay the Spire 2 install directory.
  -d, --sts2-data-dir <path>  Directory containing sts2.dll and 0Harmony.dll.
                              Overrides the directory derived from --sts2-path.
  -m, --mods-path <path>      Directory the built mod is copied into
                              (default: the "mods" folder of the game install).
  -g, --godot <path>          Godot/MegaDot 4.5.1 mono executable, required for --publish.
  -p, --publish               Also export the Godot .pck (requires --godot).
  -h, --help                  Show this help.

Environment variables: CONFIGURATION, STS2_PATH, STS2_DATA_DIR, MODS_PATH, GODOT_BIN.
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -c|--configuration) CONFIGURATION="$2"; shift 2 ;;
        -s|--sts2-path) STS2_PATH="$2"; shift 2 ;;
        -d|--sts2-data-dir) STS2_DATA_DIR="$2"; shift 2 ;;
        -m|--mods-path) MODS_PATH="$2"; shift 2 ;;
        -g|--godot) GODOT_BIN="$2"; shift 2 ;;
        -p|--publish) PUBLISH=true; shift ;;
        -h|--help) usage; exit 0 ;;
        --) shift; EXTRA_ARGS+=("$@"); break ;;
        *) echo "Unknown option: $1" >&2; usage >&2; exit 1 ;;
    esac
done

MSBUILD_ARGS=()
[[ -n "$STS2_PATH" ]] && MSBUILD_ARGS+=("-p:Sts2Path=$STS2_PATH")
[[ -n "$STS2_DATA_DIR" ]] && MSBUILD_ARGS+=("-p:Sts2DataDir=$STS2_DATA_DIR")
[[ -n "$GODOT_BIN" ]] && MSBUILD_ARGS+=("-p:GodotPath=$GODOT_BIN")

if [[ -n "$MODS_PATH" ]]; then
    mkdir -p "$MODS_PATH"
    # The build targets append the project name to this path, so it must end with a separator.
    MODS_PATH="$(cd "$MODS_PATH" && pwd)/"
    MSBUILD_ARGS+=("-p:ModsPath=$MODS_PATH")
fi

if [[ "$PUBLISH" == true ]]; then
    echo "Publishing $PROJECT ($CONFIGURATION)"
    dotnet publish "$PROJECT" -c "$CONFIGURATION" ${MSBUILD_ARGS[@]+"${MSBUILD_ARGS[@]}"} ${EXTRA_ARGS[@]+"${EXTRA_ARGS[@]}"}
else
    echo "Building $PROJECT ($CONFIGURATION)"
    dotnet build "$PROJECT" -c "$CONFIGURATION" ${MSBUILD_ARGS[@]+"${MSBUILD_ARGS[@]}"} ${EXTRA_ARGS[@]+"${EXTRA_ARGS[@]}"}
fi
