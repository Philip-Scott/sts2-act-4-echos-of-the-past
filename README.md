# slay-the-spire-the-architect

A mod that adds an Act 4 to Slay the Spire 2, in which you fight against a bound version of your past self.

The project was scaffolded from the [Slay the Spire 2 Content mod template](https://github.com/Alchyr/ModTemplate-StS2)
and depends on BaseLib.

## Requirements

- .NET SDK 9.0 or newer (the mod targets `net9.0`)
- Slay the Spire 2 installed through Steam
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

```
dotnet build
```

Building copies `TheArchitect.dll`, `TheArchitect.pdb` and `TheArchitect.json` into the game's
`mods/TheArchitect/` folder.

```
dotnet publish
```

Publishing additionally exports the Godot `.pck` (assets in the `TheArchitect/` folder) into the same
mods folder, which requires `GodotPath` to be set.

## Layout

| Path | Purpose |
| --- | --- |
| `TheArchitect.json` | Mod manifest (id, version, dependencies, min game version) |
| `TheArchitect/` | Assets: images, localization, `mod_image.png` — packed into the `.pck` |
| `TheArchitectCode/` | C# source; `MainFile.cs` is the mod entry point and applies Harmony patches |
| `project.godot`, `export_presets.cfg` | Godot project used for exporting the asset `.pck` |
| `Sts2PathDiscovery.props` | Locates the Slay the Spire 2 install |

See the [template wiki](https://github.com/Alchyr/ModTemplate-StS2/wiki) for further modding details.
