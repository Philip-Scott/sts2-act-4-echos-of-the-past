# Act 4: Echos of the Past 1.5.3

**Your past is the final boss.**

This update from **1.5.2** addresses a multiplayer hang involving the Corrupted
Defect.
It still requires Slay the Spire 2 public-beta **v0.113.0**.
Prepared for release; not yet published.

## What's new

- Attempt to fix the multiplayer hang regarding the OvervoltedMatrix power, and
  how it interacted with other mods.

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
The new binding-circle animation has not been performance-benchmarked on
lower-end hardware. The multiplayer fix was verified in isolated single-player
and simulated-party playtests; real networked co-op was not tested.

Use `TheArchitect-v1.5.3.zip` for manual installation, not GitHub's source archives.
Extract its `TheArchitect` folder into the game's `mods` folder with the game
closed. See `INSTALL.md` for details and `SHA256SUMS` for file hashes.
