# slay-the-spire-the-architect

A mod that adds an Act 4 to Slay the Spire 2, in which you fight against a bound version of your past self.

The project was scaffolded from the [Slay the Spire 2 Content mod template](https://github.com/Alchyr/ModTemplate-StS2)
and depends on BaseLib.

## Playable beta slice

This development build targets Steam's **public-beta**, game **v0.111.0**
(Steam build `24724944`, game commit `41cef1ea`), with **BaseLib 3.4.5**.
It is single-player only. Disable other fourth-act mods, including Act4Heart,
before playing; multiplayer runs retain the vanilla route.

After Act 3, the run continues to **Act 4 — The Architect** with a fixed
Ancient → Rest Site → Shop → boss route. The first visit fights the Architect; later
visits first fight a corrupted version of the previous completed character.
The Corrupted Player starts at twice that character's saved maximum HP, or
2.5 times at Ascension 8 (Tough Enemies) and above in the current run (rounded up).
The compact map fits all four nodes without scrolling, using neutral-ink
icons and an original Architect boss silhouette. Rest and Shop share the
supplied tower-approach illustration; the boss fight retains the
native Architect workshop interior. Native campfire, merchant, and room
interactions remain intact.

**The Unwritten**, the Architect's rival and patron of imperfection, offers one
mandatory choice from three personal relic offers: a build gift, a recovery gift,
and a bargain-slot gift. Native Ancient entry healing, choices, reward screens,
and save behavior are retained; there is no decline, reroll, or special
multiplayer protocol. Candidates have equal weights within each category.

| Category | Relic | Effect |
| --- | --- | --- |
| Build | Loose Thread | Draw +1 on your first three turns. |
| Build | Crooked Needle | Start combat with 1 Strength and 1 Dexterity. |
| Build | Orange Pearl | Start combat with 1 Artifact. |
| Build | Diamond Hand | After drawing on odd-numbered turns, apply combat-only Glam to a random eligible unenchanted card in hand. |
| Recovery | Unspent Possibility | Gain 150 Gold. |
| Recovery | Last Meal | Gain 20 maximum HP, heal 20, and receive two potion rewards and one rare-card reward. |
| Bargain | Borrowed Tomorrow | Gain +1 Energy on your first three turns; start combat with 2 ordinary Vulnerable. |
| Bargain | Handheld Mirror | Acquire fresh copies of three different eligible owned relic types, with no additional cost. |

First and Repeat Visits use the same pool. Turn counts include extra turns, and
the Corrupted Player-to-Architect transition does not restart bonuses or copied
relic counters. Only permanent deck and maximum-HP changes enter the existing
terminal snapshot; relics, Gold, potions, and combat-only Glam do not.
The Unwritten uses an initial sigil presentation and eight original, individually
illustrated relic icons, with matching inventory, selection-outline, and large
inspection textures. Editable SVG sources accompany the PNG assets.
Mirror can copy owned relic types, including modded relics, by default.
Its blocklist excludes Mirror itself, Touch of Orobas, Pael's Eye, Golden Compass,
Fur Coat, Lord's Parasol, Archaic Tooth, Paper Krane, Paper Phrog, Lava Rock,
Winged Boots, and Byrdpip; melted copies are also excluded. At least three
distinct eligible types are required for Mirror to be offered. The shared
blocklist in `TheArchitectCode/Relics/MirrorDuplication.cs` applies to both offers
and duplication. Copied relics keep their normal acquisition behavior,
requirements, and drawbacks. Dusty Tome retains its prepared card, Sea Glass
retains its selected character, and Girya retains its current training (including
all three lifts when fully trained). Other counters still start fresh. When
several copies of a type are owned, the first non-melted copy in inventory supplies
this state. All three copies are prepared before any pickup effect runs.
Older saves already in the Architect Act retain their original three-stop route.
Interrupted nested rewards inherit native Ancient limitations, not a new
crash-safe transaction system.

The Corrupted Player now uses the **native in-process card engine in normal play**,
restoring the saved deck, upgrades, native enchantments and saved card properties.
It owns persistent native energy, piles, powers, RNG, orbs and pets without
joining the human party. There is no worker process or adapter fallback.
Cards play left to right with deterministic first-valid choices. The Corrupted Player
draws its upcoming hand during your turn (five cards by default), then plays
that hand when you end your turn without drawing a second opening hand. The preview
shows its actual hand and resources, not a guaranteed damage forecast;
cards enlarge on hover. Native orb slots appear for Defect and for any other
character that acquires orb capacity. Enemy pets stay beside their Corrupted Player,
without moving the human.

Corrupted Players use **Bound Echo** rather than a purple tint: cyan-violet
afterimages and a slowly shifting body slice sit inside orbiting gold seals and
a ground sigil. The effect composites the animated body, not individual Spine
attachments, and leaves native character materials, health, intents and orbs
alone. Enemy-owned Osty gets the same effect on its own body, including when it
grows or revives; the human's Osty is unchanged. The bindings stay outside the
distortion and fade when their creature dies.

Co-op-only cards and third-party card/modifier effects are visibly
**Unsupported** and remain unplayed; the original saved JSON is preserved.
Missing saved models block entry with an actionable error rather than silently
dropping cards. This is not a claim that every vanilla card combination has been
audited. See [native Corrupted Player behavior and boundaries](TheArchitectCode/CorruptedPlayerCombat/README.md).
Terminal outcomes open the native victory screen directly.

For fast feedback, start a disposable modded single-player run, open the
developer console with the **backtick (`)** key, and enter `architect`. This skips to the Act 4
map using your current build; The Unwritten's native entry healing still applies,
but the command does not grant a late-game deck or extra Gold.
Normal completion of this run can replace your profile's Corrupted Player.
Use `architect`, not `act 4`: the vanilla `act` command can only visit acts
already appended to the current run.

The isolated native arrival scenario needs no snapshot input:
`bash scripts/native-demo.sh run "/path/to/Slay the Spire 2" --ancient`.
It uses disposable saves and the worktree's `artifacts/mods`, never the live mod
installation. The `architect` command includes The Unwritten; the direct
boss bootstrap below intentionally remains a combat-only entry.

The repository skill [`setup-act4`](.github/skills/setup-act4/SKILL.md) coordinates
this setup from random victory histories: source Slot 1 to modded Slot 2 by
default, with backups and naturally rolled Ancient offers. Explicit slot
overrides are supported.

`--architect-history-setup /absolute/path/to/config.json` prepares a live selected-slot
run from two single-player victory histories and opens The Unwritten with Mirror
offered by the selected native seed unless disabled below. Select the target and
back it up first; the launcher refuses a mismatched slot or an active run.
The configuration supplies `ProfileId` (1, 2 or 3),
`PlayerHistoryPath`, `OpponentHistoryPath`, `OutputDirectory`, a `Seed` prefix,
and explicit historical `MaxEnergy` / `BaseOrbSlotCount` values, which run history
does not store. Deck upgrades, enchantments, relic state, potions, HP and Gold are
restored without replaying acquisition rewards. Ordinary launches do not run
this setup.
Set `ForceMirror` to `false` to use `Seed` unchanged and let all Ancient offers
roll normally; it defaults to `true` for existing Mirror-specific setups.
`--architect-resume-slot2` instead opens the existing selected Slot 2 single-player
save without rerunning history setup or replacing its rewards and Shop choices.

An alternative **unsaved** developer launch uses the game's bootstrap mode:
`--bootstrap --architect-playtest`. Add `--architect-repeat` to use a copy of
the test character's deck for the Corrupted Player phase. This does not replace the
profile's Corrupted Player or record run history.

Developer checks: `dotnet run --project tests/CorruptedPlayer/CorruptedPlayer.Tests.csproj`
and `dotnet run --project tests/Architect/Architect.Tests.csproj` retain the
legacy planner and persistence/Architect regressions. They are not substitutes
for native runtime coverage. The Linux `scripts/native-demo.sh` launcher exercises
the production actor from normal startup using a disposable copy of a supplied
snapshot; its name is historical. See its documented invocation and isolation
requirements in the native Corrupted Player README. On Linux, `native-demo.sh run`
uses a private virtual display per instance, so worktrees can run and capture
concurrently without sharing desktop input. Installed game assets are shared
read-only; only each run's small mod bundle, disposable snapshot and optional
shader caches are copied.
For faster in-process automation, opt into `run --shared-visible`: separate
GPU-rendered desktop windows retain file/process isolation, use native viewport
captures, and reject external mouse/keyboard commands. Desktop focus is still
shared; see the native Corrupted Player README for the tradeoff and measured timings.
**Changing XDG_DATA_HOME alone
does not isolate Steam Cloud or real saves.**

To reproduce an existing Corrupted Player fight without touching its profile,
set `ARCHITECT_RUN_INPUT` to the saved `current_run.save` and run
`scripts/native-demo.sh run "/path/to/Slay the Spire 2" --saved-run`.
This copies the run into the isolated instance and exercises preview creation,
human power-card play, and save/reload, including optimized creature-identity reads.

For the Bound Echo visual pass, build to `artifacts/mods`, close the game, then
run `bash scripts/native-demo.sh --exclusive-window "/path/to/Slay the Spire 2" --corruption`.
This opt-in scenario uses the five base characters rather than a saved snapshot,
with disposable data and Steam disabled. Its captures and log go under
`artifacts/native-demo/`; it does not install the build into the live game.

Corrupted Players are stored locally in the active **modded** profile's
`TheArchitect/corrupted_player_snapshot.json`, with an atomic replacement and backup.
They are not synchronized through Steam Cloud. Keep the same character/content
mods enabled for the next visit; an unavailable character produces a First Visit.

This is an early playtest build, not completion of every item in #2. Full
crash-transaction recovery between snapshot capture and base-game progression/history
is still tracked in #12. Avoid force-closing the game during the result transition.
The beta game's normal combat saves restart the encounter rather than resuming mid-turn.

**Naming:** The enemy fought before the Architect is the **Corrupted Player**,
not the human player. Code identifiers, paths, and save keys use this name too.
Pre-release saves using the old name are not migrated.

## Requirements

- .NET SDK 9.0 or newer (the mod targets `net9.0`)
- Slay the Spire 2 installed through Steam (or a copy of `sts2.dll` and `0Harmony.dll`)
- Docker, if you prefer building in the provided container instead of installing the SDK locally
- MegaDot / Godot 4.5.1 mono — only needed for a full Godot export when publishing
  (the game will not load a `.pck` exported with a newer Godot version)

## Setup

1. Clone the repository.
2. Copy `Directory.Build.props.example` to `Directory.Build.props` and edit it:
   - set `GodotPath` to your MegaDot/Godot mono executable;
   - uncomment and set `Sts2Path` if the game is not found automatically.

   `Directory.Build.props` is machine specific and is intentionally git-ignored.
3. Open `TheArchitect.sln` in your IDE, or build from the command line.

The game install is discovered automatically by `Sts2PathDiscovery.props` for the default Steam
locations on Windows, Linux and macOS. `sts2.dll` and `0Harmony.dll` are referenced directly from
that install, so the game must be installed before building.

## Building

Single command, on Linux/macOS and Windows respectively:

```
scripts/build.sh
```

```powershell
./scripts/build.ps1
```

This copies `TheArchitect.dll`, `TheArchitect.pdb`, `TheArchitect.json`, and an automatically
packed `TheArchitect.pck` into the game's `mods/TheArchitect/` folder.
Install the matching BaseLib runtime (`BaseLib.dll`, `BaseLib.json`, `BaseLib.pck`)
in `mods/BaseLib/` as well. Restart the game after rebuilding.
Add `--publish` (`-Publish` in PowerShell) to run a full Godot
export of the assets in `TheArchitect/`; that requires a Godot/MegaDot 4.5.1 mono executable,
taken from `GodotPath` in `Directory.Build.props` or from `--godot`.

Both scripts accept the same options — run them with `--help` / `Get-Help` for the full list:

| Option | Environment variable | Purpose |
| --- | --- | --- |
| `--configuration` | `CONFIGURATION` | Build configuration, defaults to `Debug` |
| `--sts2-path` | `STS2_PATH` | Game install directory, when auto-detection fails |
| `--sts2-data-dir` | `STS2_DATA_DIR` | Directory holding `sts2.dll` and `0Harmony.dll` |
| `--mods-path` | `MODS_PATH` | Where the built mod is copied, defaults to the game's `mods` folder |
| `--godot` | `GODOT_BIN` | Godot/MegaDot 4.5.1 mono executable |
| `--publish` | — | Also export the `.pck` |

Plain `dotnet build` / `dotnet publish` still work if you prefer, and also default
to copying into the live game. For development, use
`scripts/build.sh --mods-path artifacts/mods`; install only with the game closed.

### Building in Docker

`Dockerfile` describes a build environment with the .NET SDK, Godot 4.5.1 mono and the native
libraries Godot needs. It deliberately does not contain the game assemblies; the game install is
mounted read-only at build time:

```
scripts/docker-build.sh
```

The helper builds the image, mounts the repository and the game (override the location with
`STS2_PATH`), and writes the built mod to `artifacts/mods` (override with `OUTPUT_DIR`). Any extra
arguments are forwarded to `scripts/build.sh`, e.g. `scripts/docker-build.sh --configuration Release`.

## Continuous integration

`.github/workflows/build.yml` runs on every push and pull request. It validates the mod manifest
(`scripts/validate_manifest.py`), builds the mod, uploads the result as a workflow artifact, and
builds the Docker image so the build environment stays working.

Because `sts2.dll` and `0Harmony.dll` cannot be redistributed, the compilation step needs them to be
supplied: set the repository secret `STS2_ASSEMBLIES_URL` to a URL of a zip archive containing those
two assemblies (or API stubs exposing the same public surface). When the secret is absent the
workflow still validates the manifest and the Docker image, and logs a warning that compilation was
skipped.

## Layout

| Path | Purpose |
| --- | --- |
| `TheArchitect.json` | Mod manifest (id, version, dependencies, min game version) |
| `TheArchitect/` | Assets: images, localization, `mod_image.png` — packed into the `.pck` |
| `TheArchitectCode/` | C# source; `MainFile.cs` is the mod entry point and applies Harmony patches |
| `project.godot`, `export_presets.cfg` | Godot project used for exporting the asset `.pck` |
| `Sts2PathDiscovery.props` | Locates the Slay the Spire 2 install |
| `scripts/` | Build helpers and manifest validation |
| `Dockerfile` | Reproducible build environment |

See the [template wiki](https://github.com/Alchyr/ModTemplate-StS2/wiki) for further modding details.
