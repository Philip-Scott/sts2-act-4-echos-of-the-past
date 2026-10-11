# Act 4: Echos of the Past 1.2.1

**Your past is the final boss.**

This update from **1.2.0** fixes multiplayer Continue previews and adds a way to
remove broken saved opponents. Published on Steam Workshop on October 7, 2026.

## What's new

- Fixed a black screen when resuming multiplayer and a related cleanup error
  when starting a new multiplayer run.
- Multiplayer Continue now previews every saved party member's opponent before
  anyone else rejoins, including runs before Act 4. Selected opponents still
  come from the run's frozen decks, not a newer party history.
- Added a ten-right-click recovery shortcut to the Corrupted Deck buttons.
  It clears only the relevant solo or host-owned party opponent and its backups.

## Recovering a broken opponent

Right-click "View Corrupted Deck" or "View Corrupted Decks" 10 times in a row.
Exhaust effects start on the fourth click; the tenth burns away the button.
Other input or leaving the screen cancels an incomplete sequence. In co-op,
only the host can reset; unready first.

The solo button clears the active profile's saved opponent. The co-op button
clears only that host's exact party. Continue-menu resets also clear the
opponent saved inside the ongoing run, without deleting the run or changing
the players' decks. Continue to fight the Architect directly.
A co-op Continue reset closes the lobby; reopen it and have everyone rejoin
to receive the corrected save. Errors are shown instead of reporting a
successful reset.

The internal ID, runtime filenames and installation folder remain `TheArchitect`.
Update the existing installation; do not install this as a second mod.
Existing saves remain compatible. The content-only Workshop update
preserves the Steam-edited description, title, visibility, thumbnail, and
screenshot gallery.

## Compatibility and limitations

**Compatibility:** public-beta **v0.111.0**, BaseLib **3.4.5**.
All co-op participants need matching versions. Disable other fourth-act mods.
BaseLib must be installed separately.
Downfall **0.1.16** remains optional; it is neither required nor bundled.

**Known limitations:** mod mechanics requiring human-only UI, relics or uncaptured
custom state may still need compatibility work. Unrecognized saved extension
formats remain visibly **Unsupported** and unplayed; missing saved models block
entry rather than silently dropping cards. Unverified Downfall versions/API
layouts are explicitly Unsupported. Not all vanilla or modded combinations are
audited. Lineages are local, not Steam Cloud data. Host migration is unsupported.
Avoid force-closing during the result transition; full crash-transaction recovery
is still tracked in issue #12.

Use `TheArchitect-v1.2.1.zip` for manual installation, not GitHub's source archives.
Extract its `TheArchitect` folder into the game's `mods` folder with the game
closed. See `INSTALL.md` for details and `SHA256SUMS` for file hashes.
