# Act 4: Echos of the Past 1.2.0

**Your past is the final boss.**

This update from **1.1.0** introduces The Watcher Ancient, expands Ancient gifts,
adds Wrath and Calm stances, and brings new music to the final encounter.

## What's new

- The Watcher replaces The Unwritten as the Act 4 Ancient!
- Expanded Ancient gifts to 12 relics across 3 different categories: Preparation,
  Discipline, and Transcendence.
- Added Scry and Wrath/Calm enchantments and stances. Wrath increases Attack damage
  dealt and enemy damage received by 50%; leaving Calm grants 1 Energy.
- Added "The Hollow Pulse" boss music.

The internal ID, runtime filenames and installation folder remain `TheArchitect`.
Update the existing installation; do not install this as a second mod.
Existing saves retain retired relics and the Ancient's original saved identity.
The planned Workshop update must preserve the Steam-edited description,
visibility, thumbnail, and screenshots.

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

Use `TheArchitect-v1.2.0.zip` for manual installation, not GitHub's source archives.
Extract its `TheArchitect` folder into the game's `mods` folder with the game
closed. See `INSTALL.md` for details and `SHA256SUMS` for file hashes.
