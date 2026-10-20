# Changelog

## Unreleased

- Corrupted Players now carry a character buff: Singeing Ichor (Ironclad),
  Ring of the Viper (Silent), Tarnished Crown (Regent), Fractured Urn
  (Necrobinder) and Overvolted Matrix (Defect).
- At Ascension 9 and above, Corrupted Players also gain Echo Shard: +1 energy
  at the start of each turn.
- Corrupted Player hand previews overlap cards so up to 10 fit without
  scrolling; the scroll arrows are removed.
- Corrupted Player hands now animate like the player's hand: cards are drawn
  from their draw pile one at a time, kept cards slide into place, discards fly
  to their discard pile and exhausted cards burn away.
- Corrupted Player hands now rest directly on each Corrupted Player's health
  bar, centered over it, instead of above its head.
- Corrupted Player energy, stars and draw, discard and exhaust piles now show
  as compact icons ahead of its buffs below the health bar. Hover them for
  details; click a pile to view it. The energy icon no longer shows the
  Corrupted Player Plan tooltip.
- Fixed Corrupted Players stopping mid-fight in three- and four-player runs:
  native enemy block scaling had no Act 4 tier and threw every time they
  gained block.
- Corrupted Player hand cards drawn over another creature now still respond to
  hover, and only one enlarged card is shown at a time.
- The corrupted Regent renders slightly smaller so his hand preview clears the
  relic bar.
- Custom power icons now load their own art instead of the missing-icon
  placeholder.

## 1.3.0 Changes

Released October 12, 2026 at 2:19 p.m. PDT.

- Corrupted Players now appear in a separate Elite fight between the Shop and
  the Architect, only when saved opponents exist. Winning grants normal Elite
  rewards and returns to the map before a fresh Architect combat.
- Increased Corrupted Player HP to 2.5 times saved maximum HP at Ascension 0-7
  and 3.1 times at Ascension 8+, rounded up with no additional multiplayer scaling.
- Losing the Corrupted Player Elite now shows the normal game-over screen and
  preserves the previous saved opponents. Only wins or losses against the
  Architect boss capture player decks and play the corruption ending.
- Fixed Act 4 map icons losing their interior details. Shop and campfire icons
  now retain light and dark contrast instead of appearing as solid silhouettes.
- Updated the Watcher's map portrait and hover outline.
- Clarified empty and unavailable Corrupted Deck preview messages.
- Replaced the Architect soundtrack with RYS's "The Heart OST but it's Metal".
  Each boss fight starts at the intro and loops during combat.
- Corrupted Players use their own section of the soundtrack. After victory,
  the track plays through its ending once, continuing across rewards and the map.
- Restored the game's run-ending music after the Architect's final strike,
  without changing the recorded win or loss.

## 1.2.1 Changes

Released October 7, 2026 at 10:35 p.m. PDT.

- Fixed a multiplayer resume black screen caused by deck-preview cleanup accessing
  an already-freed lobby screen. The same cleanup error when starting a new
  multiplayer run is also fixed.
- Multiplayer Continue previews now show the full saved party's Corrupted Decks
  before the other players rejoin, including runs that have not reached Act 4.
  Once opponents are selected, the preview keeps using the decks saved with the run.
- Added a recovery shortcut for broken Corrupted Decks. Right-click "View Corrupted
  Deck" 10 times in a row to clear the saved opponent and its backups. Exhaust
  effects start on the fourth click; the tenth burns away the button.
- Solo resets affect only the active profile's opponent. Co-op resets are
  host-only and affect only that exact party.
- Using the recovery shortcut on a Continue screen also clears that run's frozen
  opponent without deleting the run or changing the players' decks. Co-op
  Continue resets close the lobby so everyone can rejoin with the corrected save.

## 1.2.0 Changes

Released September 16, 2026 at 2:23 p.m. PDT.

Changes since 1.1.0, released September 14, 2026 at 10:00 a.m. PDT.

- The Watcher replaces The Unwritten as the Act 4 Ancient!
- Expanded Ancient gifts to 12 relics across 3 different categories: Preparation, Discipline, and
  Transcendence.
- Added Scry and Wrath/Calm enchantments and stances. Wrath increases Attack damage
  dealt and enemy damage received by 50%; leaving Calm grants 1 Energy.
- Added "The Hollow Pulse" boss music.

## 1.1.0 Changes

Released September 14, 2026 at 10:00 a.m. PDT.

- Performance improvements, specially on Multiplayer
- Corrupted Players can use native co-op cards.
- Corrupted Players can use installed modded cards, enchantments, and BaseLib card
  modifiers through the native combat engine.
  - Downfall 0.1.16 support.
- You can inspect saved Corrupted Decks from the solo Continue menu and co-op
  load-game screen.
- Improved placement of corrupted players for multiplayers.
- Corrupted players now tracks stars
- The Architect's Invincible counter now displays the remaining damage allowance
  for the turn.
