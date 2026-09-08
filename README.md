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
Rest Site → Shop → boss route. The first visit fights the Architect; later
visits first fight a corrupted version of the previous completed character.
The compact map fits all three nodes without scrolling, using neutral-ink
icons and an original Architect boss silhouette. Rest and Shop share the
supplied tower-approach illustration; the boss fight retains the
native Architect workshop interior. Native campfire, merchant, and room
interactions remain intact.

The Challenger now uses the **native in-process card engine in normal play**,
restoring the saved deck, upgrades, native enchantments and saved card properties.
It owns persistent native energy, piles, powers, RNG, orbs and pets without
joining the human party. There is no worker process or adapter fallback.
Cards play left to right with deterministic first-valid choices. The Challenger
draws its upcoming hand during your turn (five cards by default), then plays
that hand when you end your turn without drawing a second opening hand. The preview
shows its actual hand and resources, not a guaranteed damage forecast;
cards enlarge on hover. Native orb slots appear for Defect and for any other
character that acquires orb capacity. Enemy pets stay beside their Challenger,
without moving the human.

Co-op-only cards and third-party card/modifier effects are visibly
**Unsupported** and remain unplayed; the original saved JSON is preserved.
Missing saved models block entry with an actionable error rather than silently
dropping cards. This is not a claim that every vanilla card combination has been
audited. See [native Challenger behavior and boundaries](TheArchitectCode/Challenger/README.md).
Terminal outcomes open the native victory screen directly.

For fast feedback, start a disposable modded single-player run, open the
developer console with the **backtick (`)** key, and enter `architect`. This skips to the Act 4
map using your current deck, HP, and gold; it does not grant a late-game build.
Normal completion of this run can replace your profile's Challenger.
Use `architect`, not `act 4`: the vanilla `act` command can only visit acts
already appended to the current run.

An alternative **unsaved** developer launch uses the game's bootstrap mode:
`--bootstrap --architect-playtest`. Add `--architect-repeat` to use a copy of
the test character's deck for the Challenger phase. This does not replace the
profile's Challenger or record run history.

Developer checks: `dotnet run --project tests/Challenger/Challenger.Tests.csproj`
and `dotnet run --project tests/Architect/Architect.Tests.csproj` retain the
legacy planner and persistence/Architect regressions. They are not substitutes
for native runtime coverage. The Linux `scripts/native-demo.sh` launcher exercises
the production actor from normal startup using a disposable copy of a supplied
snapshot; its name is historical. See its documented invocation and isolation
requirements in the native Challenger README. On Linux, `native-demo.sh run`
uses a private virtual display per instance, so worktrees can run and capture
concurrently without sharing desktop input. Installed game assets are shared
read-only; only each run's small mod bundle, disposable snapshot and optional
shader caches are copied.
**Changing XDG_DATA_HOME alone
does not isolate Steam Cloud or real saves.**

Challengers are stored locally in the active **modded** profile's
`TheArchitect/challenger_snapshot.json`, with an atomic replacement and backup.
They are not synchronized through Steam Cloud. Keep the same character/content
mods enabled for the next visit; an unavailable character produces a First Visit.

This is an early playtest build, not completion of every item in #2. Full
crash-transaction recovery between snapshot capture and base-game progression/history
is still tracked in #12. Avoid force-closing the game during the result transition.
The beta game's normal combat saves restart the encounter rather than resuming mid-turn.

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
