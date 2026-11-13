# Act 4: Echos of the Past 1.5.0

**Your past is the final boss.**

This update from **1.4.1** brings the mod to Slay the Spire 2 public-beta
**v0.113.0** and adds the Fool as a Corrupted Player. 1.4.1 does not load on
v0.113.0, and 1.5.0 requires it.
Published to Steam Workshop on November 12, 2026 at 11:42 p.m. PST.

## What's new

- Updated for public-beta v0.113.0 and BaseLib 3.4.8. Corrupted Players keep
  their card choices, extra turns and targetable Osty, the Act 4 map stays
  compact, and defeating the Architect still leads into the bound-party ending.
- The Fool can now be a Corrupted Player. Its character buff is Discordant Bow:
  12 Aim at the start of combat and 3 Aim at the end of each of its turns.
- A Corrupted Player with arrows shows how many it has ready below its health
  bar, next to its energy and stars.
- Blitz runs end after Act 3; the Architect Act is not added to them.

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

Use `TheArchitect-v1.5.0.zip` for manual installation, not GitHub's source archives.
Extract its `TheArchitect` folder into the game's `mods` folder with the game
closed. See `INSTALL.md` for details and `SHA256SUMS` for file hashes.
