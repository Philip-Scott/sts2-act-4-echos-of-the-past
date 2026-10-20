# Act 4: Echos of the Past 1.3.0

Previously named The Architect. This is the same mod and Workshop item:
the internal ID, `TheArchitect` installation folder and saved lineages are unchanged.

## Requirements

Slay the Spire 2 **public-beta v0.111.0** and **BaseLib 3.4.5**.
Later game or BaseLib releases are not automatically guaranteed compatible.
For co-op, every participant must use matching game, mod and content versions.
Disable other fourth-act mods, including Act4Heart.

Downfall **0.1.16** is optional and is not bundled or required. When it is loaded,
Corrupted Players use a version-checked adapter for its character mechanics.
Other Downfall versions are not automatically supported; this is not a guarantee
that every Downfall card or multiplayer combination works.

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

Act 4 follows Act 3 automatically: The Watcher, Rest Site, Shop, then the Architect.
When saved opponents exist, a separate Corrupted Player Elite appears between the
Shop and the Architect. In co-op, the same host and group face their entire last
saved party. Winning the Elite grants normal Elite rewards and returns to the map.
Losing ends the run normally without capturing decks or replacing saved opponents.
Only wins or losses against the Architect boss record the next opponents.

Corrupted Players have 3.5 times their saved maximum HP at Ascension 0-7 and
4.1 times at Ascension 8+, rounded up. There is no additional multiplayer HP
multiplier. The Architect starts a fresh combat after the Elite.

Saved decks can be viewed from character select. Corrupted Player lineages are
local to the modded profile and are **not synchronized by Steam Cloud**.
Keep the same character and content mods enabled for later visits.
Multiplayer Continue also previews the full saved party before other players
rejoin. Before Act 4 it reads the original host's current party history; once
opponents are selected it shows the frozen decks saved with the run.

## Known limitations and troubleshooting

Corrupted Players execute installed modded card/modifier effects and native co-op
cards. Human-only mod interfaces and mechanics requiring uncaptured relics or
custom state may need specific compatibility work. Unrecognized saved extension
formats remain visibly Unsupported and unplayed. Not every combination is audited.
Missing saved content can block entry rather than silently discard cards.
To remove a broken saved opponent, right-click its "View Corrupted Deck" button
10 times in a row. Exhaust effects start on click four, and click ten burns the
button and clears that opponent and its backups. This affects only the active
solo profile or the exact co-op party. Co-op resets are host-only; unready first.
On a Continue screen, it also clears the opponent frozen inside the ongoing
run without deleting the run or the players' decks. The fight then goes directly
to the Architect. A co-op Continue reset closes the lobby; reopen it and have
everyone rejoin with the corrected save.

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
