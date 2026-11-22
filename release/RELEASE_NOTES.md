# Act 4: Echos of the Past 1.5.3

**Your past is the final boss.**

This update from **1.5.3** adds Blitz support: Blitz runs now continue to
the Architect Act.
It still requires Slay the Spire 2 public-beta **v0.113.0**.
Released November 21, 2026 at 7:51 p.m. PST.

## What's new

- Blitz runs now continue to Act 4: The Watcher, a Rest Site, the Blitz Shop,
  the Corrupted Player Elite when you have saved opponents, and the Architect.
  Standard runs are unchanged.
- Defeating the Act 3 boss in Blitz grants a Rare card choice and a potion.
- Blitz keeps its own Corrupted Players, separate from other runs. The Blitz
  lobby can view and reset them.
- In Blitz, Corrupted Players gain more HP at Ascension 3 (Tough Enemies) and
  Echo Shard at Ascension 4 (Deadly Enemies).
- In Blitz, The Last Wish grants a random relic instead of 99 Gold.

The internal ID, runtime filenames and installation folder remain `TheArchitect`.
Update the existing installation; do not install this as a second mod.
Keep your saved opponent lineages. Back up ongoing runs before updating, and use
matching versions across the entire party.

## Compatibility and limitations

**Compatibility:** public-beta **v0.113.0**, BaseLib **3.4.8**.
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
The binding-circle animation has not been performance-benchmarked on
lower-end hardware. Blitz support was verified by automated tests and one
in-game Blitz run; networked co-op Blitz was not tested.

Use `TheArchitect-v1.6.0.zip` for manual installation, not GitHub's source archives.
Extract its `TheArchitect` folder into the game's `mods` folder with the game
closed. See `INSTALL.md` for details and `SHA256SUMS` for file hashes.
