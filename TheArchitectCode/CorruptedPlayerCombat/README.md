# Native in-process Corrupted Player

`CorruptedPlayer` uses `NativeCorruptedPlayer` in ordinary single-player and multiplayer gameplay.
It restores the previous-character snapshot, not a fixed demo deck. Multiplayer
restores every member of the host's last saved matching group, with a separate
native actor and deterministic RNG stream for each enemy. No launch
flag selects an alternative production engine. There is no RPC worker, scalar
damage replay, manual card-effect adapter fallback or singleton-state swapping.

## Ownership and native behavior

The actor has a private native `Player`, `RunState`, `PlayerCombatState`, deck,
energy, stars, RNG and card piles. Its player's Creature backing reference is
bound once to the actual Corrupted Player enemy. Neither the private player nor its
run becomes a real party member, and `LocalContext` is unchanged.

Native save restoration preserves card IDs, upgrades, native enchantments and
saved properties. Combat copies retain their native deck-version relationship.
Cards execute through `SpendResources` and `OnPlayWrapper` inside the enemy move,
including live source/target hooks, native resource spending, powers, pile
movement, generated cards, choices, X costs and automatic plays.

`NativeCombatView` supplies owner-relative opponent/ally collections and private
run RNG while delegating actual combat mutations and absolute-side operations to
the live encounter. This prevents player-oriented native helpers such as random
enemy targeting from attacking their own Corrupted Player. Native pets use the
private owner's RNG for creature creation as well. Private players have distinct
identities that cannot collide with the human party or another Corrupted Player.

Native call sites are rewritten as well as getters: BaseLib pre-JIT and native
inlining can bypass getter-only detours. The stable Creature backing reference,
explicit owner/participant reads, and explicit turn-loop phase boundaries are
essential. Do not replace them with temporary human state swaps.

The real turn loop owns side hooks. Actor energy and hand draw are prepared at
the start of the human turn, including the opening turn, so the human can inspect
the actual upcoming hand. The base draw is five, with native draw modifiers,
Innate, retention and the hand limit preserved. Extra human turns do not draw
another Corrupted Player hand. After its block clears on the enemy turn, the actor
uses that prepared hand without resetting energy or drawing again; native
player-start hooks still run then. Orb start effects, pre-play, manual play and
post-play occur in its move. End-hand effects, Ethereal, retain/discard and orb passives resolve before
side-end completion. Native extra-turn hooks run additional actor turns before
the human side resumes, preparing their own hands because no human turn
intervenes. An extra turn belongs only to that Corrupted Player and its pets,
not the rest of the Corrupted Party. Powers and card-local mutations persist between turns.

Each Corrupted Player retains its monster lifecycle listener and cleans up its
own pets and actor state on death. Only the last confirmed member death adds the
Architect, before the native engine removes that enemy, preserving continuous
combat and human hand/energy/HP. A shared one-shot phase prevents duplicate bosses
from simultaneous or nested deaths; a member whose death is prevented still
blocks the transition. Each member has 2x saved maximum HP, or 2.5x at A8+, rounded
up, without native multiplayer HP scaling. Native terminal outcomes, snapshot deduplication and atomic
replacement remain in the existing lifecycle/persistence implementation.

## NPC policy and display

- Play the first currently playable supported card from left to right, then
  reconsider the current hand after each native effect.
- Use the first live valid opponent for targeted attacks. Native random effects
  retain native randomness using the private actor RNG.
- Resolve native choices in their offered order, selecting the minimum required
  count, or one when a choice is optional; choose the first offered bundle.
- Human attack intent, where a native effect requests it, means **the targeted
  human currently holds any native Attack-type card at that exact query**.
  Energy, affordability, predicted future draws and the previous hand do not
  count. Go for the Eyes retains its native attack-then-intent-query ordering.
- A manual turn stops and reports a 100-play safety limit. More than 20
  consecutive extra turns is an explicit error, not an endless loop.

The telegraph shows only the current native hand, with larger cards and no
background panel or themed scroll-area overlay. A half-size native character
energy orb and three-quarter-size draw, discard and exhaust icons sit below the
hand, using the player's HUD artwork, count badges and typography. Energy shows
current/maximum resources; each pile opens the native read-only pile browser,
which keeps draw order hidden and reflects live pile contents.
The enemy's pile icons do not register or override the human's pile hotkeys.
On death or actor cleanup, the native energy counter leaves the scene tree
synchronously, disconnecting its combat callbacks before its owner's combat state
is removed. Hiding the hand alone is not sufficient. Each party member stops only
its own HUD, and later refreshes cannot recreate a defeated member's counter.
An empty hand stays empty in the preview; neither the draw pile nor the discard
pile is presented as playable cards. Native cards enlarge on hover or keyboard/
controller focus and dismiss when the pointer or focus leaves. Clicking does not
pin a preview, so there is no Clear button.
Supported hand cards and their hover previews use native combat values, including
the Corrupted Player's Strength/Weak and its current target's Vulnerable.
All-enemy and random-enemy attacks use native multi-target preview rules.
Values refresh with live state, without predicting earlier cards' effects,
future draws or power expiry. Unsupported cards retain unpowered previews.
The enemy is named for its saved character (for example, "Corrupted Ironclad").
Party previews are compact, with full-size hover inspection; three or four
Corrupted Players use a two-row enemy layout.
This is a state preview, **not an exact future-damage forecast**.

The actual native `NOrbManager` renders slots, passive/evoke effects and native
orb hover tips for every character, including characters initially having zero
slots. Corrupted Player-owned Osty uses the enemy container and mirrored owner-relative
position, with native health, hitbox, summon/revive and scaling behavior. Ordinary
human pet layout is unchanged.

Directional card effects face the human party from the Corrupted Player's side.
Dagger throws and impacts, scratches, and stabs translate native player-facing
flags at card call sites without changing monster effects. Sweeping Beam mirrors
its emitter; Defect beams use the mirrored eye offset. Hyperbeam and Shiv retain
their native target-derived rotation rather than being flipped a second time.

## Explicit boundaries

Co-op-only native cards and third-party card/enchantment/modifier effects are
outside this release's support. They are visibly labelled **Unsupported**, not
played or spent as fake no-op actions, and do not execute card/modifier combat
hooks. Their exact raw captured JSON remains in the profile snapshot. Unsupported
saved extension data is not handed to arbitrary extension deserializers when
constructing the unplayed native card.

Missing saved card, enchantment or BaseLib modifier models produce a visible
preflight error. Restore the matching content version before entering the boss;
the snapshot is not replaced. In single-player, an unavailable saved character
is treated as a First Visit. Multiplayer instead requires the entire frozen
party to validate on every peer; no member is silently omitted.

The captured schema contains character, max HP and deck, not the previous run's
relic inventory, potions, temporary powers or mid-combat orb state. Those are not
silently synthesized. This is not a certification of every vanilla combination;
new beta APIs or mod interactions can require further native integration.

Normal saves restart this encounter at the room boundary using the same entry
snapshot; there is no mid-turn continuation. The native JSON omission of the
compact Act's empty encounter/event lists is normalized for reload.
The separate snapshot/base-game crash-transaction gap remains tracked in #12.

## Isolated native runtime checks

Target: public-beta v0.111.0, commit `41cef1ea`, Steam build `24724944`,
BaseLib 3.4.5 and .NET 9.

```sh
scripts/build.sh --mods-path artifacts/mods
export ARCHITECT_SNAPSHOT_INPUT=/absolute/path/to/corrupted_player_snapshot.json
bash scripts/native-demo.sh run "/absolute/path/to/Slay the Spire 2" --label cards
# Alternative scenarios (each invocation creates a new isolated instance):
bash scripts/native-demo.sh run "/absolute/path/to/Slay the Spire 2" --loss
bash scripts/native-demo.sh run "/absolute/path/to/Slay the Spire 2" --nondefect
bash scripts/native-demo.sh run "/absolute/path/to/Slay the Spire 2" --poison
# Focused hand/hover modifier coverage, including native damage after preview:
bash scripts/native-demo.sh run "/absolute/path/to/Slay the Spire 2" --previews
# Generated 2-4-member parties; no snapshot input needed:
bash scripts/native-demo.sh run "/absolute/path/to/Slay the Spire 2" --party
# Four-member opening layout only:
bash scripts/native-demo.sh run "/absolute/path/to/Slay the Spire 2" --party-layout
# Directional attack effects in both directions; generates its own disposable snapshot:
bash scripts/native-demo.sh run "/absolute/path/to/Slay the Spire 2" --attack-vfx
```

The script's historical name does not enable a fixture engine. Its explicit
`--architect-native-test` gate controls disposable storage and automation only.
The party scenario uses the native test run service with multiple human players,
not a live network connection. It exercises full-party generation, fixed HP,
independent native/extra turns, preview layout, and sequential/AoE deaths through
the production encounter and handoff. Transport consensus, host ownership, and
frozen save/rejoin behavior have separate deterministic regression coverage.
The default scenario first loads the supplied snapshot through normal encounter
entry, then exercises native reload and targeted mechanics in that disposable
combat. The loss scenario follows saved-deck turns with a lethal multi-hit probe.
The non-Defect scenario writes an explicitly labelled disposable Ironclad orb
deck through the normal snapshot format, exercising zero-to-first-slot capacity.
Winning scenarios transition with two live lightning orbs and verify that the
dead actor's orb UI is cleared even after its creature node leaves the room lookup.
The poison scenario kills the Corrupted Player at enemy turn start instead of using
direct damage, then waits for the next player turn to cover deferred UI refreshes.

### Parallel instances: shared assets, private display

`run` starts a foreground, owned instance. Use one terminal (or one agent-owned
background tool call) per instance; keep the launcher alive. Multiple runs,
including runs from different worktrees, do not need a reserved desktop window.
No game assets are copied: bubblewrap mounts the installed game read-only, with
a **per-run copy of the worktree's mods** mounted over `game/mods`. Only the small
mod bundle, supplied snapshot and optional shader caches are copied. The launcher rejects a mod bundle
that changes during copying; finish a build before starting a run. After startup,
rebuilding the worktree cannot change that instance's loaded files.

Every run gets a random-suffixed ID, private HOME/XDG/save directories, logs,
Xauthority cookie, Xvfb server, and PID/network/IPC/mount namespaces. Its X11
socket directory and abstract socket namespace are private. All servers can
therefore use `:0` **inside their own namespaces** without connecting to the host
`:0`. No host display socket, authorization, input devices, audio devices, GPU,
Steam/desktop session sockets, or inherited Steam environment is passed through.
Other runs' control sockets are hidden inside the sandbox. Root/game/mods are
read-only, host homes/runtime are hidden, networking is denied, native saves use
memory stores, and snapshot files are written only under the disposable runtime.
Steam is disabled before save/mod startup with `--force-steam off`. Neither real
saves nor installed mod files are changed; changing `XDG_DATA_HOME` alone is not
sufficient isolation. This is accidental-interference isolation, not a security
boundary against hostile code running as the same host user.

The launcher prints the exact `run-*` ID and artifact directory. From another
terminal **in that same worktree**, use that ID (not a path, PID, or "latest"):

```sh
bash scripts/native-demo.sh status run-YYYYMMDD-HHMMSS-cards-RANDOM
bash scripts/native-demo.sh capture run-YYYYMMDD-HHMMSS-cards-RANDOM
bash scripts/native-demo.sh pointer run-YYYYMMDD-HHMMSS-cards-RANDOM 1270 710
bash scripts/native-demo.sh key run-YYYYMMDD-HHMMSS-cards-RANDOM Escape
bash scripts/native-demo.sh click run-YYYYMMDD-HHMMSS-cards-RANDOM 1
bash scripts/native-demo.sh stop run-YYYYMMDD-HHMMSS-cards-RANDOM
```

Controls use a mode-0600 Unix socket inside the mode-0700 run directory; there
is no TCP service or arbitrary shell-command endpoint. Input and focus affect
only that private X server. `status` reports the runtime marker, mod SHA-256
hashes, namespace IDs and private pointer position. Reported PIDs are
**namespace-local**, not host PIDs to kill. `capture` writes a unique 1280x720 PNG
under `captures/`; native viewport captures such as `opening.png` and `result.png`
are also produced automatically. Early captures can show a black loading frame.
Use `game.log` to determine which native stage has loaded. Move the private
pointer away from cards before capturing if hover previews obscure the view.

`stop` waits for the supervisor to terminate and reap its
own game and Xvfb children, escalating only those child handles if necessary.
Ctrl-C or terminating the foreground launcher also tears down its bubblewrap
PID namespace, including game-spawned helpers. No global process-name kill is
used. Natural scenario completion cleans up automatically. `status` subsequently
reports `exited` and the exit code; a deliberate stop also records
`stop_requested`. If teardown exceeds 20 seconds, the command reports an error
rather than claiming success. A stale socket or dead run returns an error rather than
signalling a recycled PID. Artifacts are retained for inspection; remove only
the specific finished `artifacts/native-demo/run-*` directory when no longer
needed. Disk usage grows with retained screenshots/logs, not duplicated games.

`ARCHITECT_MODS_INPUT` selects an alternative bundle to copy, including installed
bytes; it must contain complete TheArchitect and BaseLib bundles. Without it,
TheArchitect comes from `artifacts/mods` and BaseLib is copied from the installed
game. Do not invoke the native test gate in a normal saved session.

### Linux dependencies and rendering tradeoffs

Install/provide Python 3, `bwrap`, `Xvfb`, `xauth`, `xdpyinfo`, `xdotool`,
ImageMagick 7 (`magick` with X11 support), and Mesa software GL/EGL (including
`/usr/share/glvnd/egl_vendor.d/50_mesa.json`). Launches never download dependencies.
Set `ARCHITECT_XVFB` to an absolute executable path if Xvfb is not on PATH.
The .NET SDK is needed only for building; a user-local SDK can be added to PATH.

On immutable Fedora/Bazzite, Xvfb can be explicitly downloaded and unpacked
without installing host packages, for example:

```sh
mkdir -p artifacts/tools/rpms artifacts/tools/xvfb
dnf download --resolve --destdir artifacts/tools/rpms xorg-x11-server-Xvfb
# Substitute the exact RPM filename downloaded for your Fedora release:
(cd artifacts/tools/xvfb && rpm2cpio ../rpms/xorg-x11-server-Xvfb-21.1.24-1.fc44.x86_64.rpm | cpio -idm --quiet)
export ARCHITECT_XVFB="$PWD/artifacts/tools/xvfb/usr/bin/Xvfb"
ldd "$ARCHITECT_XVFB" # Resolve any "not found" dependencies before launching.
```

The example was exercised on Fedora 44/Bazzite; other releases must use their
own matching packages. A user-local Xvfb executable still needs compatible host
libraries and XKB data. Keep reusable tools outside disposable run directories.

Virtual runs use Mesa llvmpipe, four renderer threads and a 30 FPS cap.
`--render-threads 2` reduces CPU contention; higher counts (up to 16) may help on
larger CPUs, but all instances still compete for the same CPU/RAM. Mesa EGL
and GLX are selected explicitly: on NVIDIA Bazzite, merely setting
`LIBGL_ALWAYS_SOFTWARE=1` still let Xvfb load NVIDIA EGL and crash. Software
rendering avoids GPU/display ownership and supports the game's compatibility
renderer, but shares CPU/RAM with other instances and is slower than native GPU
rendering. It is suitable for functional/UI automation, not GPU performance,
audio, Steam, controller or desktop window-manager testing.

By default, a new run copies just `cache/mesa_shader_cache` and
`xdg/SlayTheSpire2/shader_cache` from the most recent completed, zero-exit virtual
run for the same game path in this worktree. A deliberate stopped run qualifies.
No profile, settings, snapshots, sockets or other XDG data are reused. Each
instance writes its own cache copy; there is no shared writable cache.
`--cache-from RUN_ID` selects a particular completed source and rejects active
runs, other worktrees/games or absent caches. `--cold` disables reuse.
Mesa/Godot's driver/shader-version cache keys determine which entries can be
reused after updates; use `--cold` to rule out cache problems. The source ID is
recorded as `cache_source` in `run.json`. Cache reuse is an optimization, not a
promise of fast startup: software-rendered native preloading and effects can
still be slow, and existing wall-clock native assertions can time out.

Observed on Bazzite with this target game and concurrent instances:

| Renderer/cache | Launch to map image | Launch to opening combat image |
| --- | --- | --- |
| Two threads, cold | 170-173 seconds | 230-231 seconds |
| Two threads, copied shader cache | 220 seconds | 276 seconds |
| Four threads, copied shader cache | 142 seconds | 173 seconds |

These are individual observations under competing load, not controlled
benchmarks. Shader reuse alone did not improve iteration time. One cold
two-thread poison run hit the native 60-second turn-progression timeout after
its opening capture; launcher success or an image is not a passing native
regression. The timing runs were otherwise explicitly stopped, not certified
as completed gameplay checks. **This is a safe concurrent automation mode, not
yet a fast replacement for visible GPU-accelerated iteration.** An accelerated
private display is a potential follow-up. The explicit shared-visible option
below is another tradeoff for in-process automation; it is not input-isolated.
Neither mode weakens native preloading/assertion timeouts.

### Faster shared-visible GPU automation

For native tests that issue their actions **inside the game**, multiple visible
GPU-rendered windows can run together without desktop mouse automation:

```sh
scripts/build.sh --mods-path artifacts/mods
export ARCHITECT_SNAPSHOT_INPUT=/absolute/path/to/corrupted_player_snapshot.json
# DISPLAY and XAUTHORITY must describe your existing local X11 desktop.
bash scripts/native-demo.sh run "/absolute/path/to/Slay the Spire 2" --shared-visible --label agent-A --nondefect
# Run another invocation in a separate terminal / attached agent command:
bash scripts/native-demo.sh run "/absolute/path/to/Slay the Spire 2" --shared-visible --label agent-B --poison
```

This mode keeps the same frozen mod bundle, copied snapshot, private HOME/XDG,
read-only game, Steam-off/network isolation, PID namespace, run IDs and owned
teardown. It explicitly exposes the selected host X11 socket and authorization,
plus one GPU render node (`--render-device /dev/dri/renderD128` by default).
No host input devices, audio devices or GPU display-control (`card*`) nodes are
mounted. The tested device is the Intel P630 using Mesa; NVIDIA-only support is
not established. Python, bwrap, xdpyinfo and Mesa are required, but Xvfb,
ImageMagick and xdotool are not required for this mode. Software-renderer thread
limits and copied software shader caches do not apply.

**Rebuild the mod before using this mode.** The native test gate disables GUI
input, requests a non-focusable window and disables background FPS limiting in
its memory-only settings. It replaces the test-only pointer warp with the
existing in-process Clear action after native pile browsing, and releases only
Godot's internal GUI focus. No production UI or normal-game settings change.
Window titles contain their unique run IDs. Namespace-local PIDs can repeat,
so desktop windows are never identified or controlled by those PIDs.

Use `status`, `capture` and `stop` with the exact run ID as above. `capture`
requests a PNG from that game's own viewport on its render thread: it does not
capture the desktop, raise a window, move a pointer, or depend on which window
is on top. `pointer`, `key` and `click` deliberately **fail** in shared-visible
mode. A test-only observer writes `telemetry.json` with stage, frame counts,
unfocused-frame counts, focus state, title and observed desktop pointer
coordinates. Native stage captures also log focus and frame counts.

On the tested Bazzite/Intel desktop, two simultaneous instances reached
`corrupted-player-display.png` at **39.8-39.9 seconds**, and `opening.png` at
**41.2-41.3 seconds** (versus 173 seconds with the four-thread software display).
The unfocused instance completed native pile checks and turns, remained
capturable after its peer was stopped, and passed the full poison/native
regression. A separate 60-second hands-off observation covered startup, pile
checks and combat with no desktop pointer movement; its non-Defect scenario
also completed successfully. This confirms concurrent **in-process** automation
on this machine, not arbitrary desktop automation or every window manager.

Desktop focus is still shared: startup, the window manager, closing windows,
or a human clicking them can change focus even with the non-focusable hint.
Other mods may have their own global cursor behavior. Do not treat this mode as
a security boundary or promise of an undisturbed desktop. Coordinate when a
human/another agent needs focus-sensitive desktop work, and use the private
display for mouse/keyboard automation. Minimized-window progression and other
GPU/desktop combinations have not been established.

### Legacy exclusive visible window

The legacy command remains available for a human-reserved X11 window:

```sh
bash scripts/native-demo.sh --exclusive-window "/absolute/path/to/Slay the Spire 2" --nondefect
```

It requires host X11 authorization and retains the conservative global
game-process refusal. Coordinate explicitly with other agents before using it;
it shares desktop focus/input and is **not** the parallel automation mode.
It also uses live build output rather than the virtual mode's frozen mod copy,
so do not rebuild its input while it runs. Sandbox audio warnings are expected.

Launcher-only regressions (no game/display or additional Python packages):
`PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests -p native_demo_test.py`.

The runtime probes cover real saved Defect cards and upgrades; Perfected Strike
and Impervious with native modifiers; generated cards, choices, persistent powers,
Rage/Thorns/Flame Barrier reactions, saved Genetic Algorithm properties and Sharp
enchantment, private RNG, orb rendering, Osty placement/revival, extra turns,
exclusion/error reporting, reload, cleanup, handoff and native terminal outcomes.
The old `CorruptedPlayerPlanner`, exact-adapter classes and pure tests are retained as
historical regression/reference code, but are not the production execution path.
