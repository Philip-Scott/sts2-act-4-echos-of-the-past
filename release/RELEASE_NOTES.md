# Act 4: Echos of the Past 1.3.0

**Your past is the final boss.**

This update from **1.2.1** separates the Corrupted Players from the Architect boss,
increases their HP, updates Act 4 map artwork, and adds new fight music.
Published to Steam Workshop on October 12, 2026 at 2:19 p.m. PDT.

## What's new

- Corrupted Players now have their own Elite fight between the Shop and the
  Architect. The node appears only when saved opponents exist. Winning grants
  normal Elite rewards and returns to the map before a fresh Architect combat.
- Losing the Elite ends the run normally without capturing player decks or
  replacing saved opponents. Only wins or losses against the Architect boss
  capture the next opponents and play the corruption ending.
- Corrupted Player HP is now 2.5 times saved maximum HP at Ascension 0-7 and
  3.1 times at Ascension 8+, rounded up, with no extra multiplayer multiplier.
  The Architect's HP scaling is unchanged.
- Shop and campfire map icons retain their interior light and dark details.
- The Watcher has a new map portrait and hover outline.
- Empty and unavailable Corrupted Deck previews use clearer messages.
- The Architect now fights to RYS's "The Heart OST but it's Metal". Each fight
  starts at the intro and loops during combat.
- Corrupted Players use a separate section of the soundtrack. After victory,
  it plays through its ending once across rewards and the map. Entering the
  Architect fight starts the boss intro instead.
- The game's run-ending music plays after the Architect's final strike.
  The recorded win or loss remains unchanged.

The recording is arranged and performed by RYS, with original music by
Clark Aboud. The mod author confirmed permission from RYS to use the recording
in this mod on October 12, 2026, and will add credits to the Steam description.
See `TheArchitect/audio/CREDITS.txt` in the source repository.

The internal ID, runtime filenames and installation folder remain `TheArchitect`.
Update the existing installation; do not install this as a second mod.
Keep your saved opponent lineages. Back up ongoing runs before updating, and use
matching versions across the entire party. The content-only Workshop update
left the Steam-edited description, title, visibility, thumbnail, and screenshot
gallery unchanged.

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

Use `TheArchitect-v1.3.0.zip` for manual installation, not GitHub's source archives.
Extract its `TheArchitect` folder into the game's `mods` folder with the game
closed. See `INSTALL.md` for details and `SHA256SUMS` for file hashes.
