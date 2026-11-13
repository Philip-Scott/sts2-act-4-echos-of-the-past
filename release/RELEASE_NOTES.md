# Act 4: Echos of the Past 1.4.1

**Your past is the final boss.**

This update from **1.4.0** fixes modded card text, Corrupted Player card
cleanup and exhaust animations, and Ring of the Viper's multiplayer scaling.
Published to Steam Workshop on October 21, 2026 at 11:07 p.m. PDT.

## What's new

- Fixed card text showing raw code strings, including calculated damage values
  and energy icons, when another mod adds cards to a card pool.
- Corrupted Player cards exhausted from hand now burn in place instead of
  disappearing before their flames play.
- Corrupted Player displays return cards to the game's pool without leaving
  stale entries or changing mouse behavior in your own hand.
- Ring of the Viper no longer grants extra Artifact stacks based on party size.
  Each corrupted Silent receives the intended amount in multiplayer.

The internal ID, runtime filenames and installation folder remain `TheArchitect`.
Update the existing installation; do not install this as a second mod.
Keep your saved opponent lineages. Back up ongoing runs before updating, and use
matching versions across the entire party.

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
audited. Lineages are local, not Steam Cloud data. Changing hosts during an ongoing run is unsupported.
Avoid force-closing during the result transition; full crash-transaction recovery
is still tracked in issue #12.

Use `TheArchitect-v1.4.1.zip` for manual installation, not GitHub's source archives.
Extract its `TheArchitect` folder into the game's `mods` folder with the game
closed. See `INSTALL.md` for details and `SHA256SUMS` for file hashes.
