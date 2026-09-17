# Act 4: Echoes of the Past

![Act 4: Echoes of the Past - Your past is the final boss](docs/media/release-banner.jpg)

Meet The Watcher and choose an Ancient gift before fighting the Architect.
On later visits, you first fight a Corrupted Player that uses your previous deck.
Play solo or with a party of 2-4 players.

Version 1.2.0 is available on
[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3799307965).

## Gameplay and compatibility

After Act 3, visit The Watcher, a Rest Site, and a Shop before the boss.

On your first visit, you fight only the Architect. Later visits begin with your
saved Corrupted Player, whose deck keeps its upgrades and enchantments. Its upcoming
hand is visible during your turn. In co-op, the entire last saved party fights
together. After you defeat them all, the Architect joins the same combat.
Use "View Corrupted Deck" or "View Corrupted Decks" on the character-select
screen to preview your saved opponents.

Whether you win or lose against the Architect, the mod saves your build as your
next opponent. The mod stores saves on your computer and does not sync them through Steam Cloud.
Co-op saves belong to the host. Changing the host or a participant starts a
separate history. Keep the same character and content mods enabled.
If the game cannot load a saved card model or synchronize the party, the mod
blocks entry and reports an error. The mod does not support host migration.

Installed modded cards use the game's card engine. Cards that need human input
or character data missing from the saved build may need compatibility work.

## The Watcher's gifts

**The Watcher** is the Act 4 ancient, and she offers a relic from one of these categories: **Preparation**, **Discipline**, and **Transcendence**.

| Category | Relic | Effect |
| --- | --- | --- |
| Preparation | The Last Wish | Gain 99 Gold. Start combat with 4 Plating and 1 Strength. |
| Preparation | Golden Eye | Scry 8 at the start of combat. |
| Preparation | Orange Pearl | Start combat with 1 Artifact. |
| Preparation | Medieval Meal | Gain 20 maximum HP and receive two potion rewards and one rare-card reward. |
| Discipline | Loose Thread | Draw +1 on your first three turns. |
| Discipline | Diamond Hand | After the opening draw, apply combat-only Glam to a random eligible unenchanted card in hand. |
| Discipline | Deus Ex Machina | Upon pickup, enchant 3 random eligible deck cards with Sown. |
| Discipline | Violet Lotus | Upon pickup, enchant 2 random deck cards with Wrath and 2 other cards with Calm. |
| Transcendence | Nuremberg Egg | The 12th card played each combat is replayed twice, then the original Exhausts. Its counter hides after the replay sequence and returns next combat. |
| Transcendence | Ritual Dagger | Start combat with 1 Ritual and 3 Vulnerable. |
| Transcendence | Deva Form | After the first mid-combat reshuffle, gain 1 additional Energy at the start of each subsequent turn. Does not stack. |
| Transcendence | Handheld Mirror | Acquire a copy of 3 random relics. |

## Developer requirements

- .NET SDK 9.0 or newer. The mod targets `net9.0`.
- Slay the Spire 2 installed through Steam, or a copy of `sts2.dll` and `0Harmony.dll`.
- Docker, if you build in the provided container instead of installing the SDK locally.
- MegaDot / Godot 4.5.1 mono, required only for a full Godot export when publishing.
  The game will not load a `.pck` exported with a newer Godot version.

## Setup

1. Clone the repository.
2. Copy `Directory.Build.props.example` to `Directory.Build.props`. Set `GodotPath`
   to your MegaDot/Godot mono executable. If automatic discovery cannot find the
   game, uncomment and set `Sts2Path`. Git ignores this machine-specific file.
3. Open `TheArchitect.sln` in your IDE, or build from the command line.

`Sts2PathDiscovery.props` finds the game in the default Steam locations on
Windows, Linux, and macOS. The build references `sts2.dll` and `0Harmony.dll`
from that installation. Install the game before building.

## Building

On Linux or macOS:

```
scripts/build.sh
```

On Windows:

```powershell
./scripts/build.ps1
```

The script packs the assets into `TheArchitect.pck`. It then copies that file,
`TheArchitect.dll`, `TheArchitect.pdb`, and `TheArchitect.json` into the game's
`mods/TheArchitect/` folder. Install the matching BaseLib runtime files in `mods/BaseLib/`.
You need `BaseLib.dll`, `BaseLib.json`, and `BaseLib.pck`. Restart the game after rebuilding.

Add `--publish`, or `-Publish` in PowerShell, to run a full Godot export of the
assets in `TheArchitect/`. This requires Godot/MegaDot 4.5.1 mono.
Set its executable path through `GodotPath` in `Directory.Build.props` or `--godot`.

Both scripts accept the same options. Use `--help` for the shell script or
`Get-Help` in PowerShell for the full list.

| Option | Environment variable | Purpose |
| --- | --- | --- |
| `--configuration` | `CONFIGURATION` | Build configuration, defaults to `Debug` |
| `--sts2-path` | `STS2_PATH` | Game install directory, when auto-detection fails |
| `--sts2-data-dir` | `STS2_DATA_DIR` | Directory holding `sts2.dll` and `0Harmony.dll` |
| `--mods-path` | `MODS_PATH` | Where the built mod is copied, defaults to the game's `mods` folder |
| `--godot` | `GODOT_BIN` | Godot/MegaDot 4.5.1 mono executable |
| `--publish` | None | Also export the `.pck` |

You can also use `dotnet build` or `dotnet publish`. Both install the mod in the game
by default. For development, use `scripts/build.sh --mods-path artifacts/mods`.
Install only with the game closed.

### Building in Docker

`Dockerfile` describes a build environment with the .NET SDK, Godot 4.5.1 mono and the native
libraries Godot needs. It does not contain the game assemblies.
The build mounts the game installation read-only:

```
scripts/docker-build.sh
```

The helper builds the image, mounts the repository and the game, and writes the
mod to `artifacts/mods`. Set `STS2_PATH` to override the game location or
`OUTPUT_DIR` to change the output directory. The helper forwards extra arguments
to `scripts/build.sh`, for example `scripts/docker-build.sh --configuration Release`.

## Continuous integration

`.github/workflows/build.yml` runs on every push and pull request. It validates the mod manifest
with `scripts/validate_manifest.py`, builds the mod, uploads the result as a
workflow artifact, and builds the Docker image.

The repository cannot redistribute `sts2.dll` and `0Harmony.dll`.
Set the repository secret `STS2_ASSEMBLIES_URL` to the URL of a ZIP archive
containing those assemblies or API stubs with the same public types and members.
Without the secret, the workflow skips compilation and logs a warning.
It still validates the manifest and builds the Docker image.

## Layout

| Path | Purpose |
| --- | --- |
| `TheArchitect.json` | Mod manifest with ID, version, dependencies, and minimum game version |
| `TheArchitect/` | Images, localization, and other assets packed into the `.pck` |
| `TheArchitectCode/` | C# source. `MainFile.cs` is the mod entry point and applies Harmony patches |
| `project.godot`, `export_presets.cfg` | Godot project used for exporting the asset `.pck` |
| `Sts2PathDiscovery.props` | Locates the Slay the Spire 2 install |
| `scripts/` | Build helpers and manifest validation |
| `Dockerfile` | Reproducible build environment |

See the [template wiki](https://github.com/Alchyr/ModTemplate-StS2/wiki) for more about modding.
