# Act 4: Echos of the Past 1.4.0

**Your past is the final boss.**

This update from **1.3.0** gives each Corrupted Player a character buff,
raises their HP, and reworks their hand and resource display.
Published to Steam Workshop on October 19, 2026 at 10:45 p.m. PDT.

## What's new

- Each Corrupted Player carries a buff for its character: Singeing Ichor
  (Ironclad), Ring of the Viper (Silent), Tarnished Crown (Regent), Fractured
  Urn (Necrobinder) or Overvolted Matrix (Defect). At Ascension 9+, they also
  gain Echo Shard: +1 energy at the start of each turn.
- Corrupted Player HP is now 3.5 times saved maximum HP at Ascension 0-7 and
  4.1 times at Ascension 8+, rounded up, with no extra multiplayer multiplier.
- Corrupted Player hands sit on their health bars, overlap so up to 10 cards
  fit, and animate like the player's hand: draws, discards and exhausts.
- Energy, stars and draw, discard and exhaust piles show as compact icons below
  each Corrupted Player's health bar. Click a pile to view it.
- Corrupted Players have larger target hitboxes. Cards drawn over another
  creature still respond to hover, and only one enlarged card shows at a time.
- Every co-op participant saves the complete Corrupted Party, so anyone in the
  group can host the next run against the same opponents.
- Fixed Corrupted Players stopping mid-fight in three- and four-player runs.
- Fixed Whistle and other stuns targeting a player in Corrupted Player fights.
  The player gains Stunned: no card draw until the end of their next turn.
- Ritual Dagger is renamed Forgotten Ritual. The Architect's intents sit lower.
- Custom power icons show their own art instead of a placeholder.

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

Use `TheArchitect-v1.4.0.zip` for manual installation, not GitHub's source archives.
Extract its `TheArchitect` folder into the game's `mods` folder with the game
closed. See `INSTALL.md` for details and `SHA256SUMS` for file hashes.
