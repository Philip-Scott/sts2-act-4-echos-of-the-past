# Changelog

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
