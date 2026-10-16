---
name: setup-act4
description: Compose an Act 4 playtest from reusable YAML decks. Supports a saved local player or unsaved fake multiplayer, with independently selected Corrupted Players.
---

# Set up Act 4

Use `scripts/setup-act4.py`. Keep selection, backup, and save-editing logic in
Python, not ad hoc shell commands or a second C# loadout importer.

## Load decks

Each `decks/*.yml` file describes one build, not a matchup. Assign files to roles:

```sh
python3 scripts/setup-act4.py load \
  --player decks/deck-1.yml \
  --corrupted decks/deck-2.yml decks/deck-3.yml --replace
```

Default target is modded Slot 2. `--slot 3` is supported; Slot 1 is protected.
One to four corrupted decks can face a single player. Opponents receive only
character, cards, and maximum HP. Their relics and other resources are ignored.

For the existing unsaved local fake-multiplayer mode:

```sh
python3 scripts/setup-act4.py load --fake-multiplayer \
  --player decks/deck-1.yml decks/deck-2.yml \
  --corrupted decks/deck-3.yml
```

This is not networked co-op. Like the earlier fake-multiplayer inspection, it
starts directly at the Corrupted Player fight with no Ancient gift selected.
It leaves a visible local fixture open and does not
overwrite a profile. Player and opponent counts are independent, each from 1 to 4.
Run this foreground command in a persistent terminal or an approved detached
session. Check its ready receipt before reporting success.

## Capture more builds

```sh
python3 scripts/setup-act4.py capture --count 3 --source-slot 1 --output decks/new
```

The script samples distinct single-player victories from both source-slot trees.
It exports loadout data only. Private histories, paths, seeds, and hashes stay in
ignored `artifacts/act4/capture-*/`, never in tracked deck files.

History omits `max_energy` and `base_orb_slot_count`; capture writes these as
`null`. Establish them from the character and permanent effects before using
the build as a player. Do not double-count combat-time relic bonuses. Ask if the
values cannot be recovered confidently. Never reroll a weak or unusual build.

## Before running

- Read `--help` and [the launcher documentation](../../../docs/ACT4_PLAYTESTS.md).
- Install `scripts/requirements-act4.txt` if PyYAML is missing.
- Build with `scripts/build.sh --mods-path artifacts/mods`, or the existing
  container build. The script installs that bundle only with the game closed.
- On the rootless-Podman machine, use the existing image without rebuilding:
  `DOCKER=podman REUSE_IMAGE=true IMAGE=localhost/sts2-the-architect-build:latest OUTPUT_DIR="$PWD/artifacts/ui-build" scripts/docker-build.sh -- --verbosity quiet`.
  The helper uses `--userns=keep-id`, not a host UID mapping. Use separate output
  for independent work and avoid concurrent builds in the same checkout.
  An `ARCHITECT_MODS_INPUT` override must include BaseLib's DLL, JSON and PCK
  under `BaseLib/`, plus any other installed runtime dependencies. See the
  launcher documentation for the copy commands. Do not install a new SDK or
  update workloads just because a successful build prints a workload notice.
- Steam resolution is `--steam`, then `ARCHITECT_STEAM`, then known native-client
  locations, then `PATH`. Prefer the native client on this machine:
  `ARCHITECT_STEAM="$HOME/.local/share/Steam/ubuntu12_32/steam"`.
  The launcher resolves it before costly preparation. An invalid explicit
  executable fails instead of falling back.
- Both launchers discover the existing
  `artifacts/tools/xvfb/usr/bin/Xvfb` when Xvfb is absent from `PATH`.
  `ARCHITECT_XVFB` overrides discovery. The existing `ilspycmd`, when needed,
  runs inside the build image with `DOTNET_ROLL_FORWARD=Major`; do not install
  another copy.
- Ask before stopping a game or replacing a solo run unless the request already
  authorizes it. Then use `--stop-game` or `--replace`. Never delete a multiplayer
  save; the script refuses it.
- Steam Cloud is disabled on this local playtest machine. Confirm its state on
  another machine before direct save replacement.
- `--account` resolves ambiguous Steam accounts; never guess.

Python edits a freshly exported native template and writes both the primary
solo save and its backup. It preserves the complete previous target profile and
installed mod under `artifacts/act4/load-*/`. Small in-game hooks export the
template, resume the save, and run fake multiplayer. They do not select decks.

`--verify` runs an isolated load/encounter check without touching profiles.
`--no-launch` writes a solo save without launching the **live** game, but still
needs an isolated game process to export the native template. If the user asks
for no game processes at all, explain this dependency and ask before proceeding.

## Handoff

Wait for `ready.json`, not just process startup. Check the requested characters,
counts, resources, seed, and opponents against the receipt. Leave The Watcher's
natural offers unselected. Do not search seeds for a Mirror or play turns.

Report the target slot or unsaved mode, assigned decks, natural offers, and backup
path. On failure preserve artifacts, report the error, and do not repeatedly
overwrite the new save. Normal Continue resumes a successfully prepared solo run.

For failed Steam dispatch, inspect the new `attempt-*/steam.log`,
`dispatch.json`, and game log. Exit zero alone is not success. The launcher
distinguishes no startup evidence from loading without a receipt, waits up to
`--ready-timeout` seconds, and never retries automatically. After ruling out a
delayed launch and closing any game, recover a versioned preparation with:

```sh
python3 scripts/setup-act4.py launch-only artifacts/act4/load-REPLACE_ME \
  --confirm-no-pending-launch \
  --steam "$HOME/.local/share/Steam/ubuntu12_32/steam"
```

This validates the existing artifacts, exact save, selected profile, and runtime
compatibility. It never regenerates, reinstalls, or rewrites the save. A new
`--no-launch` preparation needs no pending-dispatch confirmation.
Legacy preparation directories lack the required records and are refused;
do not invent a migration or replace a working legacy fixture.

For presentation-only iteration on a versioned, unchanged Watcher playtest:

```sh
ARCHITECT_MODS_INPUT="$PWD/artifacts/ui-build" \
python3 scripts/setup-act4.py restart-existing-playtest artifacts/act4/load-REPLACE_ME \
  --stop-game --ui-only --unchanged-playtest
```

Obtain all three consents. There must be no gameplay, including unsaved progress,
since the successful receipt. The build must not change gameplay, persistence,
native playtest hooks, dependencies, or the manifest. Restart backs up the
current profile and installed mod, installs only while closed, and resumes the
existing save without rewriting it. It rejects changed saves rather than
silently resetting progress. Use the printed `restart-*` directory next time.
Gameplay/schema changes require full preparation and native verification.

Read `stages.json` for stage timings and failures. Keep asset warnings separate
from failed native assertions or receipts. Full preparation still uses two
isolated native processes; shader-cache reuse already works. Single-process
preparation is deferred pending native lifecycle verification, not replaced
with cached templates or weaker checks.
