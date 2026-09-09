#!/usr/bin/env bash
set -euo pipefail

# Run only in an explicitly reserved game window. The installed mod directory is never modified.
if [[ "${1:-}" != "--exclusive-window" || $# -lt 2 || $# -gt 3 ]]; then
    echo "Usage: bash scripts/native-demo.sh --exclusive-window /absolute/path/to/game [--loss|--nondefect|--poison|--corruption]" >&2
    exit 2
fi
scenario=()
if [[ "${3:-}" == "--loss" ]]; then
    scenario=(--architect-native-loss)
elif [[ "${3:-}" == "--nondefect" ]]; then
    scenario=(--architect-native-nondefect)
elif [[ "${3:-}" == "--poison" ]]; then
    scenario=(--architect-native-poison)
elif [[ "${3:-}" == "--corruption" ]]; then
    scenario=(--architect-corruption-visuals)
elif [[ -n "${3:-}" ]]; then
    echo "Unknown demo scenario: $3" >&2
    exit 2
fi
root="$(cd "$(dirname "$0")/.." && pwd -P)"
game="$(realpath "$2")"
if pgrep -x SlayTheSpire2 >/dev/null; then
    echo "A game is already running; refusing to launch another." >&2
    exit 1
fi
command -v bwrap >/dev/null
mods="$(realpath "${ARCHITECT_MODS_INPUT:-$root/artifacts/mods}")"
test -f "$mods/TheArchitect/TheArchitect.dll"
test -f "$game/mods/BaseLib/BaseLib.dll"
test -f "${XAUTHORITY:?X11 authorization required}"
runtime="$root/artifacts/native-demo/run-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$runtime/home" "$runtime/xdg" "$runtime/tmp" "$runtime/config" "$runtime/cache"
if [[ -z "${ARCHITECT_MODS_INPUT:-}" ]]; then
    mkdir -p "$mods/BaseLib"
    cp "$game/mods/BaseLib/BaseLib.dll" "$game/mods/BaseLib/BaseLib.json" \
       "$game/mods/BaseLib/BaseLib.pck" "$mods/BaseLib/"
fi
test -f "$mods/BaseLib/BaseLib.dll"
# Standalone Steam-off account; no settings are read from the real profile.
mkdir -p "$runtime/xdg/SlayTheSpire2/default/1"
cp "$root/scripts/native-demo-settings.json" "$runtime/xdg/SlayTheSpire2/default/1/settings.save"
if [[ "${3:-}" != "--corruption" ]]; then
    cp "${ARCHITECT_SNAPSHOT_INPUT:?Set ARCHITECT_SNAPSHOT_INPUT to the read-only captured snapshot}" "$runtime/snapshot-input.json"
fi
echo "Isolated automatic native demo: $runtime"
exec bwrap --die-with-parent --unshare-net --unshare-ipc \
    --ro-bind / / --tmpfs /var/home --bind "$runtime/tmp" /tmp --tmpfs /run \
    --ro-bind /tmp/.X11-unix /tmp/.X11-unix \
    --ro-bind "$XAUTHORITY" "$XAUTHORITY" \
    --ro-bind "$game" "$game" --ro-bind "$root" "$root" \
    --ro-bind "$mods" "$game/mods" \
    --bind "$runtime" "$runtime" --dev-bind /dev /dev --proc /proc \
    --setenv HOME "$runtime/home" --setenv XDG_DATA_HOME "$runtime/xdg" \
    --setenv XDG_CONFIG_HOME "$runtime/config" --setenv XDG_CACHE_HOME "$runtime/cache" \
    --setenv XDG_RUNTIME_DIR "$runtime/tmp" --setenv TMPDIR "$runtime/tmp" \
    --setenv ALSA_CONFIG_PATH "$root/scripts/native-demo-alsa.conf" \
    --setenv ARCHITECT_NATIVE_RUNTIME "$runtime" --unsetenv DBUS_SESSION_BUS_ADDRESS \
    --unsetenv WAYLAND_DISPLAY --unsetenv SteamAppId --unsetenv SteamGameId \
    --chdir "$game" "$game/SlayTheSpire2" \
    --display-driver x11 --rendering-method gl_compatibility --windowed --resolution 1280x720 \
    --log-file "$runtime/game.log" --force-steam off --architect-native-test "${scenario[@]}"
