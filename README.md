# slay-the-spire-the-architect

A mod that adds an Act 4 to Slay the Spire 2, in which you fight against a bound version of your past self.

The project was scaffolded from the [Slay the Spire 2 Content mod template](https://github.com/Alchyr/ModTemplate-StS2)
and depends on BaseLib.

## Requirements

- .NET SDK 9.0 or newer (the mod targets `net9.0`)
- Slay the Spire 2 installed through Steam (or a copy of `sts2.dll` and `0Harmony.dll`)
- Docker, if you prefer building in the provided container instead of installing the SDK locally
- MegaDot / Godot 4.5.1 mono — only needed to export the `.pck` when publishing
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

This copies `TheArchitect.dll`, `TheArchitect.pdb` and `TheArchitect.json` into the game's
`mods/TheArchitect/` folder. Add `--publish` (`-Publish` in PowerShell) to also export the Godot
`.pck` containing the assets in `TheArchitect/`; that requires a Godot/MegaDot 4.5.1 mono executable,
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

Plain `dotnet build` / `dotnet publish` still work if you prefer.

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
