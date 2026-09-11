# Act 4: Echos of the Past 1.0.1

Previously named The Architect. This is the same mod and Workshop item:
the internal ID, `TheArchitect` installation folder and saved lineages are unchanged.

## Requirements

Slay the Spire 2 **public-beta v0.111.0** and **BaseLib 3.4.5**.
Later game or BaseLib releases are not automatically guaranteed compatible.
For co-op, every participant must use matching game, mod and content versions.
Disable other fourth-act mods, including Act4Heart.

## Steam Workshop

Subscribe to [Act 4: Echos of the Past](https://steamcommunity.com/sharedfiles/filedetails/?id=3799307965) and its required
[BaseLib dependency](https://steamcommunity.com/sharedfiles/filedetails/?id=3737335127).
Use the game's modded launch and enable both mods. Restart after updates.
Do not also keep a manually installed copy of either mod.

## Manual installation (GitHub release)

1. Close the game. Back up your modded saves and the profile's `TheArchitect` folder.
2. Open the game installation using Steam's **Manage > Browse local files**.
3. Extract the release ZIP's `TheArchitect` folder into the game's `mods` directory.
4. Install matching [BaseLib release files](https://github.com/Alchyr/BaseLib-StS2/releases)
   separately in `mods/BaseLib`. BaseLib is not bundled.
5. Start the game in modded mode, enable both mods, and start a new run.

The resulting layout must be:

```text
Slay the Spire 2/
  mods/
    TheArchitect/
      TheArchitect.dll
      TheArchitect.json
      TheArchitect.pck
    BaseLib/
      BaseLib.dll
      BaseLib.json
      BaseLib.pck
```

You do not need a .NET SDK, Godot editor, source archive, or `.pdb` to play.
When upgrading a manual installation, replace all three Architect runtime files
together with the game closed. Do not remove your saves to update the mod.

## What to expect

Act 4 follows Act 3 automatically: Ancient, Rest Site, Shop, then boss.
Your first visit faces the Architect. Future visits first face a Corrupted Player
restored from your previous terminal Architect encounter. In co-op, the same host
and group face their entire last saved party. Either Architect outcome records
the next opponents.

Saved decks can be viewed from character select. Corrupted Player lineages are
local to the modded profile and are **not synchronized by Steam Cloud**.
Keep the same character and content mods enabled for later visits.

## Known limitations and troubleshooting

Co-op-only cards and third-party card/modifier effects on Corrupted Players remain
visibly Unsupported and unplayed. Not every vanilla combination is audited.
Missing saved content can block entry rather than silently discard cards.
Host migration is not supported. Avoid force-closing during result transitions;
crash-transaction recovery remains incomplete. Combat reloads restart encounters,
as in the supported beta game.

If a Workshop auto-update changes compatibility, use matching manual releases for
the whole party, removing duplicate Workshop installations first.
To uninstall, finish or abandon any Architect run, close the game, then unsubscribe
or remove only `mods/TheArchitect`. Preserve the profile's lineage folder.

Report issues at https://github.com/Philip-Scott/slay-the-spire-the-architect/issues
with game/mod/BaseLib versions, solo or co-op, reproduction steps, and relevant logs.
Review logs and saves for personal information before attaching them.
