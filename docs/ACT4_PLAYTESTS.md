# Act 4 deck playtests

`scripts/setup-act4.py` composes a matchup from reusable `decks/*.yml` files.
The script uses Python and PyYAML. The current launcher targets Linux/Steam and
uses the existing isolated native-demo dependencies for template export.
See [native playtest dependencies](../tests/CorruptedPlayer/README.md).

```sh
python3 -m pip install -r scripts/requirements-act4.txt
scripts/build.sh --mods-path artifacts/mods
python3 scripts/setup-act4.py load \
  --player decks/deck-1.yml \
  --corrupted decks/deck-2.yml decks/deck-3.yml --replace
```

This backs up modded Slot 2, replaces its solo run, and starts the game at
The Watcher. Slot 1 is read-only. Use `--slot 3` for a different target.
The script refuses multiplayer saves and refuses an existing solo run unless
`--replace` is supplied. Close the game first, or authorize `--stop-game`.
Only use direct replacement with Steam Cloud disabled.

Backups, generated saves, native receipts, and logs live in `artifacts/act4/`.
Each load gets a separate directory. The backup contains the complete previous
target profile and account-level modded `profile.save*` files. The script also
backs up the installed Architect bundle before installing the staged build.
It leaves target history, preferences, progress, unlocks, and other profiles intact.

## Steam dispatch and recovery

For a saved live run, the launcher resolves and prints Steam before exporting
the native template. Resolution order is `--steam`, `ARCHITECT_STEAM`, the native
client at `~/.local/share/Steam/ubuntu12_32/steam`, the native client at
`~/.steam/steam/ubuntu12_32/steam`, then `steam` on `PATH`. An invalid explicit
choice fails instead of falling back.
An operation lock prevents simultaneous Act 4 launcher commands in one worktree.
Do not run another launcher against the same installation from a different
checkout while preparation or recovery is active.

```sh
export ARCHITECT_STEAM="$HOME/.local/share/Steam/ubuntu12_32/steam"
```

The local `/usr/bin/steam` dispatch once returned zero without a game process,
new log, or receipt. The native client dispatched the same request successfully.
The wrapper's internal failure is unverified. Exit zero is not a ready signal.

Every dispatch writes a new `attempt-*/launch.json`, `steam.log`, and
`dispatch.json` inside the preparation directory. The status records the command,
exit status or dispatch timeout, elapsed time, and startup evidence. Receipts from
earlier attempts cannot satisfy a new attempt. At 20 seconds the launcher reports
whether startup has begun, but continues waiting. `--ready-timeout` defaults to
240 seconds and never triggers another dispatch or kills the live game.
A process or game log without a receipt is a receipt timeout, not success.
Native assertions and invalid receipts are separate failures. Asset warnings,
missing encounter images, and resource-leak messages remain in logs; the
launcher neither suppresses them nor treats every warning as a failed assertion.

If dispatch fails, do not run `load --replace` again. Inspect the attempt's logs
and Steam client, rule out a delayed launch, and close any game before retrying:

```sh
python3 scripts/setup-act4.py launch-only artifacts/act4/load-REPLACE_ME \
  --steam "$HOME/.local/share/Steam/ubuntu12_32/steam" \
  --confirm-no-pending-launch
```

For a new `load --no-launch` preparation, omit `--confirm-no-pending-launch`.
That flag is required only to acknowledge an unresolved earlier dispatch.
It does not authorize stopping a running game.

Launch-only never exports a template, chooses a seed, rewrites a profile,
reinstalls mods, or reads the original YAML again. It validates the versioned,
checksummed preparation and checkpoint, frozen inputs, exact primary and backup
save bytes, selected account/profile, saved run identity and opponents, game
files, and installed runtime mods. Native Continue still performs its normal
save when the game starts. The new receipt must match the starting builds and
natural offers, and the persisted save must retain its identity and opponents.
If any compatibility check fails, recovery refuses rather than restoring old
save bytes.

Preparation records are version 1. Older load directories, including playtests
created before these records existed, are deliberately unsupported by automated
recovery and restart. They lack frozen build identities and a verified live-save
checkpoint. Do not manufacture records from the current installation.
Use normal Continue for an already working legacy run, or authorize a fresh full
preparation separately. There is no silent migration.

## Restart an unchanged playtest for UI work

This is a narrow alternative to full preparation, not a general save migration.
It accepts only a versioned solo playtest with a successful live receipt, still
at its recorded Watcher state with no offer selected. Both save files and profile
selection must be byte-for-byte unchanged. A changed reload counter, gameplay,
new offer selection, another profile, missing record, or changed game/dependency
runtime makes the command refuse.

After building presentation-only changes into a separate staged bundle:

```sh
ARCHITECT_MODS_INPUT="$PWD/artifacts/ui-build" \
python3 scripts/setup-act4.py restart-existing-playtest artifacts/act4/load-REPLACE_ME \
  --stop-game --ui-only --unchanged-playtest
```

All three flags are required. `--stop-game` authorizes termination of the
identified game process. `--unchanged-playtest` confirms there has been no
gameplay since the receipt, including unsaved progress that Python cannot inspect.
`--ui-only` confirms the build changes only presentation, not gameplay,
serialization, save schema, native playtest hooks, or dependencies.
The launcher cannot prove semantic compatibility from a compiled DLL.
Do not use this assertion for an arbitrary code change. Use full preparation
and native verification for gameplay or persistence changes.

The command freezes the staged bundle and requires an unchanged mod manifest.
It stops the game only after these checks, rechecks the save after shutdown,
backs up the entire target profile, profile selection files, and installed
Architect mod, then installs the frozen build while the game is closed.
It never writes either live save or profile selection. Native Continue resumes
the existing save, with the same seed, resources, opponents, and unselected offers.
Receipt and persisted-opponent checks still apply. No native template or isolated
encounter test runs on this consented UI-only path.

Each restart creates `artifacts/act4/restart-*` with its own backup, provenance,
checkpoint, and fresh receipt attempt. Use that new directory for the next
restart or launch-only recovery. Never restore a backup automatically after a
failed launch; it could discard progress. The original preparation remains intact.

## Environment and isolated builds

On the local rootless-Podman machine, reuse the working image rather than
rebuilding it or trying another SDK image:

```sh
export STS2_PATH="$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2"
DOCKER=podman REUSE_IMAGE=true \
IMAGE=localhost/sts2-the-architect-build:latest \
OUTPUT_DIR="$PWD/artifacts/ui-build" \
scripts/docker-build.sh -- --verbosity quiet
```

`REUSE_IMAGE=true` checks for an existing local image and never builds or pulls
one. Podman uses `--userns=keep-id --security-opt label=disable`; Docker keeps
the numeric `--user` mapping. The game mount is read-only. The output bundle
is under `OUTPUT_DIR`; the container home defaults to the sibling
`OUTPUT_DIR.container-home`. `CONTAINER_HOME` can select another cache directory
outside the output bundle. Native launchers copy only mod directories with a
manifest and matching DLL or PCK, including all assets inside those directories.
Choose a new output directory for independent work. Compilation still uses this
checkout's intermediate files, so do not build concurrently in the same checkout.
Without `REUSE_IMAGE=true`, the helper retains its image-build step.

Run both C# test projects with the same container and explicit game assembly paths:

```sh
DOCKER=podman REUSE_IMAGE=true \
IMAGE=localhost/sts2-the-architect-build:latest \
scripts/docker-build.sh --test --verbosity quiet
```

The test command checks for `sts2.dll`, `0Harmony.dll` and `GodotSharp.dll`
before running either project. `STS2_DATA_DIR`, when set, is a path inside the
container; the default is `/sts2/data_sts2_linuxbsd_x86_64`.
It neither launches the game nor installs mods.

An alternate `ARCHITECT_MODS_INPUT` must contain every required runtime mod,
including these BaseLib files:

```sh
mkdir -p artifacts/ui-build/BaseLib
cp "$STS2_PATH/mods/BaseLib/"BaseLib.{dll,json,pck} artifacts/ui-build/BaseLib/
```

Copy any other installed runtime dependencies into the staged bundle too.
Saved live playtests require those dependencies to match the installation.
Only TheArchitect is installed by this launcher. Without an input override, the
default `artifacts/mods` bundle gets BaseLib from the installed runtime.
Both launchers now fail early if required DLL, JSON, or PCK files are missing.

Both `setup-act4.py` and direct `native-demo.py` use the same Xvfb discovery.
`ARCHITECT_XVFB` takes priority, followed by `Xvfb` on `PATH`, then
`artifacts/tools/xvfb/usr/bin/Xvfb`. An invalid override fails explicitly.
Do not install another Xvfb or SDK when these existing tools work.

If decompilation is needed, the existing `artifacts/tools/dotnet/ilspycmd`
targets .NET 8. Run it in the existing build image with
`DOTNET_ROLL_FORWARD=Major` for its .NET 9 runtime. It is not needed for routine
preparation. Do not run blind installations or workload updates.
The successful build's workload-verification notice is not a compiler error.

## Preparation progress and remaining startup cost

Each operation prints stage starts, outcomes, and elapsed seconds. `stages.json`
records them, including failure category and error text. Recovery checks have
their own `recovery-*/stages.json` rather than overwriting the preparation's
history. Preflight checks account ambiguity, slot eligibility, executable and
dependency availability before any game shutdown, native export, or profile
write. It may write diagnostic artifacts, but does not mutate a profile or
installation. The launcher checks safety again before live writes and detects a
target profile changed during preparation.

Each full load freezes the complete staged bundle before both native processes.
Changing the original YAML or rebuilding the source bundle cannot change that
selection halfway through. Native serialization, full reload, and encounter
checks remain mandatory before a saved run is installed.

The measured full preparation took about 217 seconds: 79 seconds for template
export, 108 seconds for isolated native verification, and 29 seconds for live
dispatch through receipt. Both isolated processes spent roughly 58 to 65 seconds
on initial asset preload. Shader-cache reuse already worked; enabling it is not
a fix for that cost.

Single-process preparation is deferred. `ExportTemplate` currently initializes
and launches a run, enters the Ancient, exports the save, and quits.
`LoadFixture` starts from a separate process, then serializes, returns to the
main menu, reloads, and checks the encounter. Combining them would require a
Python/native handoff with immutable inputs, cancellation and failure cleanup,
plus proof that tearing down the template run does not retain RNG, event, or
manager state in verification. Mocked Python tests cannot establish that native
lifecycle guarantee. This change keeps independent verification rather than
adding an untested protocol or reusing cached save templates.

## Reusable builds

The checked-in examples cover all five characters with single-player victories:

| File | Character | Ascension | Cards | Relics |
| --- | --- | --- | --- | --- |
| `decks/deck-1.yml` | Necrobinder | 5 | 27 | 15 |
| `decks/deck-2.yml` | Silent | 5 | 32 | 13 |
| `decks/deck-3.yml` | Ironclad | 6 | 77 | 25 |
| `decks/deck-4.yml` | Defect | 5 | 24 | 16 |
| `decks/deck-5.yml` | Regent | 6 | 25 | 15 |

A file can fill either role, including multiple roles in the same matchup.
`cards`, `relics`, and `potions` contain native serialized entries. Preserve their
properties to retain upgrades, enchantments, potion slots, and relic counters.
The other fields are explicit:

```yaml
schema: 1
name: Small Ironclad example
character: CHARACTER.IRONCLAD
ascension: 0
cards:
  - id: CARD.BASH
    current_upgrade_level: 1
relics:
  - id: RELIC.BURNING_BLOOD
potions: []
current_hp: 60
max_hp: 80
gold: 150
max_energy: 3
base_orb_slot_count: 0
max_potion_slot_count: 3
```

The player receives all these fields. A Corrupted Player receives only
`character`, `cards`, and `max_hp`, followed by the mod's normal enemy HP scaling.
Opponent relics, potions, Gold, and current HP are never copied into the enemy.
The first player's Ascension applies to the run unless `--ascension` overrides it.
`--seed` fixes the run seed. Otherwise the script generates one seed without
searching Ancient offers.

Scripted runs preserve the YAML's exact arrival HP instead of applying The
Watcher's automatic entry heal. This also applies when Continue reloads that
Ancient. Normal runs keep their usual healing.

Unknown fields, malformed entries, unresolved player resources, or missing
installed models stop preparation instead of silently dropping content.
The game validates loadability; installed modded content still needs compatible
native serialization and Corrupted Player support.

## Fake multiplayer

```sh
python3 scripts/setup-act4.py load --fake-multiplayer \
  --player decks/deck-1.yml decks/deck-2.yml \
  --corrupted decks/deck-3.yml
```

Both lists accept one to four files. Counts do not have to match. A solo player
can face multiple opponents without inventing dead players or co-op identities.
The local fixture uses the existing fake-multiplayer runtime, not a network
lobby. Like the earlier inspection, it starts at the Corrupted Player fight
without selecting Ancient gifts. It stays in the foreground until the game
closes. It is unsaved and never replaces a real profile.
The solo mode persists its selected opponents in the
Architect run extension, so Continue and the corrupted-deck preview retain them.
The normal deck-reset failsafe clears them too.

## Capture victories

```sh
python3 scripts/setup-act4.py capture --count 3 --source-slot 1 --output decks/new
```

To capture a missing character without sampling unrelated builds:

```sh
python3 scripts/setup-act4.py capture --count 1 --source-slot 1 \
  --character CHARACTER.DEFECT --output decks/new-defect
```

Capture reads both vanilla and modded history, excludes losses, abandoned runs,
and multiplayer records, and deduplicates by seed/start time before sampling.
It never overwrites existing YAML files. Only build data goes into the YAML;
account identities and original histories remain in the ignored capture directory.

History does not record base energy or base orb slots. Captured YAML marks those
fields `null` until reviewed. Check character defaults and permanent changes;
combat-only energy relics must not increase `max_energy` a second time. All five
checked-in builds use 3 base energy. Defect uses 3 base orb slots; the others use 0.

## Native template and verification

Python does not reconstruct RNG streams, map serialization, or native run
initialization. A small mod hook exports a fresh native Act 4 save in the existing
isolated launcher, using the selected player characters, seed, and Ascension.
Character-specific relic pools and native player fields therefore match the build.
Python edits its loadouts and opponent snapshots, verifies the edited save in an
isolated game, then writes the native-serialized result to the target profile.
The live game resumes it through native Continue and saves at The Watcher
without choosing an offer.

To exercise a matchup without touching profiles or the shared desktop:

```sh
python3 scripts/setup-act4.py load --verify \
  --player decks/deck-1.yml \
  --corrupted decks/deck-2.yml decks/deck-3.yml
```

This checks native loading, a full native reload, and encounter
startup. It does not play a human turn. `ready.json` records starting loadouts and
offers; `verified.json` exists only after the private encounter check succeeds.
The live-write command performs this check before replacing the target save.

`--no-launch` prepares the persistent solo save but does not start the live game.
It still runs the isolated game to generate the native template. No mode requires
changing platform/account settings or enabling the native-test flag in a live
profile.

Run Python regressions with
`python3 -m unittest discover -s tests -p '*_test.py'`.
The focused launcher checks need no Steam client, game, display, or container:

```sh
mkdir -p artifacts/test-scratch
TMPDIR="$(pwd -P)/artifacts/test-scratch" PYTHONPATH=tests \
  python3 -m unittest setup_act4_test native_demo_test docker_build_test
```

The persistence checks are in `dotnet run --project tests/CorruptedPlayer`.
