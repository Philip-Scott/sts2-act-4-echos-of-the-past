#!/usr/bin/env bash
# Runs the native in-game tests in an isolated sandbox. The installed game, its mods folder and
# your saves are never touched: the test mods folder is mounted over the game's mods directory and
# home/XDG directories point into a throwaway run directory.
#
#   tests/run-native.sh                 headless checks
#   tests/run-native.sh --screenshots   render under a private Xvfb and save PNGs into the run directory
#
# Environment: STS2_PATH (game install), DOTNET (dotnet executable), SKIP_BUILD=1 (reuse the last
# build in artifacts/test-mods), XVFB (Xvfb executable for --screenshots), EXTRA_MODS (colon-separated
# mod folders to load as well, such as BaseLib and a content mod), ANIMATED_PLAYER_HANDS_CHARACTERS
# (two or three comma-separated character ID fragments for the remote players), RESOLUTION (window size
# for --screenshots; the UI lays out the same at any size, and smaller renders faster).
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd -P)"
game="$(realpath "${STS2_PATH:-$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2}")"
screenshots=false
[[ "${1:-}" == "--screenshots" ]] && screenshots=true
command -v bwrap >/dev/null
test -x "$game/SlayTheSpire2"
if [[ "${SKIP_BUILD:-}" != 1 ]]; then
    dotnet="${DOTNET:-dotnet}"
    "$dotnet" build "$root/AnimatedPlayerHands.csproj" -c Release --nologo "-p:Sts2Path=$game"
    "$dotnet" build "$root/tests/Native/AnimatedPlayerHandsNativeTests.csproj" -c Release --nologo "-p:Sts2Path=$game"
fi
test -f "$root/artifacts/test-mods/AnimatedPlayerHands/AnimatedPlayerHands.dll"
test -f "$root/artifacts/test-mods/AnimatedPlayerHandsNativeTests/AnimatedPlayerHandsNativeTests.dll"
mkdir -p "$root/artifacts/native-tests"
run="$(mktemp -d "$root/artifacts/native-tests/run-XXXXXX")"
mkdir -p "$run/home" "$run/xdg/SlayTheSpire2/default/1" "$run/config" "$run/cache" "$run/tmp" "$run/screenshots"
cp "$root/tests/Native/settings.save" "$run/xdg/SlayTheSpire2/default/1/settings.save"
mkdir "$run/mods"
cp -r "$root/artifacts/test-mods/." "$run/mods/"
IFS=: read -r -a extra_mods <<<"${EXTRA_MODS:-}"
for mod in "${extra_mods[@]}"; do
    if [[ -n "$mod" ]]; then cp -r "$mod" "$run/mods/"; fi
done
echo "Isolated native tests: $run"

launch=("$game/SlayTheSpire2" --audio-driver Dummy --log-file "$run/game.log" --force-steam off)
display=()
if [[ "$screenshots" == true ]]; then
    xvfb="$(realpath "$(command -v "${XVFB:-Xvfb}")")"
    resolution="${RESOLUTION:-1280x720}"
    display=(--ro-bind "$xvfb" "$run/Xvfb" --setenv LIBGL_ALWAYS_SOFTWARE 1
        --setenv ANIMATED_PLAYER_HANDS_SCREENSHOTS "$run/screenshots" --setenv ANIMATED_PLAYER_HANDS_SWEEP 0
        --setenv RESOLUTION "$resolution")
    launch=(/bin/bash -c '
        "$0/Xvfb" :0 -screen 0 "$RESOLUTION"x24 -nolisten tcp >"$0/display.log" 2>&1 &
        for _ in $(seq 100); do [[ -S /tmp/.X11-unix/X0 ]] && break; sleep 0.1; done
        export DISPLAY=:0
        exec "$@" --display-driver x11 --rendering-method gl_compatibility --windowed --resolution "$RESOLUTION"
    ' "$run" "${launch[@]}")
else
    launch+=(--headless)
fi

status=0
timeout "$([[ "$screenshots" == true ]] && echo 2400 || echo 180)" bwrap --die-with-parent --unshare-net --unshare-ipc --unshare-pid \
    --ro-bind / / --bind "$run" "$run" --bind "$run/tmp" /tmp \
    --ro-bind "$run/mods" "$game/mods" \
    --dev-bind /dev /dev --proc /proc "${display[@]}" \
    --setenv HOME "$run/home" --setenv XDG_DATA_HOME "$run/xdg" \
    --setenv XDG_CONFIG_HOME "$run/config" --setenv XDG_CACHE_HOME "$run/cache" \
    --setenv XDG_RUNTIME_DIR "$run/tmp" --setenv TMPDIR "$run/tmp" \
    --setenv ALSA_CONFIG_PATH "$root/tests/Native/alsa.conf" \
    --setenv ANIMATED_PLAYER_HANDS_TESTS 1 \
    --setenv ANIMATED_PLAYER_HANDS_CHARACTERS "${ANIMATED_PLAYER_HANDS_CHARACTERS:-}" --unsetenv SteamAppId --unsetenv SteamGameId \
    --unsetenv DISPLAY --unsetenv WAYLAND_DISPLAY --unsetenv DBUS_SESSION_BUS_ADDRESS \
    --chdir "$game" "${launch[@]}" >"$run/output.log" 2>&1 || status=$?
grep -E '^(PASS|LAYOUT|SCREENSHOT|UNFORMATTED|STATUS|HAND) ' "$run/output.log" || true
if grep -E 'NodePool|Tried to free|ObjectDisposedException' "$run/output.log"; then
    echo "Card pool misuse; see $run/output.log"
    status=1
fi
# Any engine error or exception whose backtrace passes through the mod.
if grep -E '(\] void|^ +at) AnimatedPlayerHands\.' "$run/output.log" | head -5 | grep .; then
    echo "Errors from the mod's code; see $run/output.log"
    status=1
fi
if [[ $status -ne 0 ]] || ! grep -q '^ANIMATED_PLAYER_HANDS_TESTS_OK' "$run/output.log"; then
    echo "Native tests failed (exit $status); see $run/output.log and $run/game.log"
    grep -E 'ANIMATED_PLAYER_HANDS_TESTS_FAILED|AnimatedPlayerHands|Exception' "$run/output.log" | head -40 || true
    exit 1
fi
grep '^ANIMATED_PLAYER_HANDS_TESTS_OK' "$run/output.log"
