# Animated Player Hands

A Slay the Spire 2 mod for co-op. It shows every other player's hand above
their character, using the same compact card strip and animations that
[Act 4: Echos of the Past](../README.md) uses for the Corrupted Player.

- Each other player's health bar and powers stay visible, instead of only while
  you hover them.
- Their energy (and stars, once they have any) and their draw, discard and
  exhaust piles appear as icons ahead of their powers, with the game's tooltips.
  Clicking a pile icon opens that pile.
- Drawn cards fly in from the draw pile icon, discarded cards fly to the discard
  pile icon, and exhausted cards burn in place. The remaining cards slide into
  their new slots.
- Hovering a card shows it at full size with its keyword tooltips. Card values
  include the owner's powers, the same as in their own hand.
- Players standing side by side get a wider gap (170 instead of 70), so their
  hands have room. The game still narrows the gap when a formation would not
  fit its half of the screen. The formation also moves toward the middle of the
  screen and stops just left of it. Solo runs, and encounters where the game
  centers the players, keep their native positions.

Each hand rests on its owner's health bar and gets the room between the screen's
edges and its neighbours in the same row. A hand shrinks, down to a readable
minimum, rather than cover another hand or another player's health bar or icons.

Your own hand and singleplayer runs are unchanged. While you drag or target a
card, the remote hands ignore the mouse, so you can still target allies.
Fast mode speeds up the animations, and Instant mode turns them off.

## Modded cards

Cards from content mods use the game's own card display, so they look the same
as in their owner's hand. Cards that a mod moves into its own piles, such as the
Automaton's stash, leave the hand without flying to a pile icon. Cards that come from
those piles grow in place, like generated cards.

A modded card's code may fail when it is shown for a player other than you. If
its live text fails, the card shows its deck text instead. If a card can't be
shown at all, its slot shows the card's ID. The mod catches any failure in its
own display, so it never interrupts the game's card commands. The game log gets
one `[AnimatedPlayerHands]` warning per problem, naming the class that threw.

## Client-side only

The mod sends no network messages. In co-op, every client runs the full combat
for all players and checks its piles against the other clients, so each
client already knows every player's hand. The mod reads those piles.

The manifest sets `affects_gameplay: false`. The game leaves the mod out of the
co-op mod check, so other players do not need it. The mod has no dependencies,
including BaseLib, and does not depend on Act 4: Echos of the Past.

Targets Slay the Spire 2 public-beta v0.111.0.

## Build and install

Install the .NET 9 SDK, then run this from this folder:

```sh
dotnet build -c Release
```

The build finds the default Steam install on Linux, Windows and macOS. For
another location, pass `-p:Sts2Path="/path/to/Slay the Spire 2"`, or
`-p:Sts2DataDir=...` pointing at the folder with `sts2.dll` and `0Harmony.dll`.

Copy `artifacts/AnimatedPlayerHands/` into the game's `mods/` folder and enable
Animated Player Hands in the in-game mod list. The build does not touch your
game install.

## Tests

Unit tests cover the layout and motion math and the manifest:

```sh
dotnet run --project tests/Unit/AnimatedPlayerHands.Tests.csproj -c Release
```

The native tests start the real game in a `bwrap` sandbox with a private home
and mods folder. They run a fake three-player combat and check each remote hand
against the player's piles after draws, discards, exhausts and plays. They also
check hover previews, layout, cleanup when combat ends, and that cards go back
to the game's card pool unchanged. Every card in each remote player's card pool
passes through their hand, and a card whose text throws must fall back to its
deck text.

```sh
tests/run-native.sh                  # headless
XVFB=/path/to/Xvfb tests/run-native.sh --screenshots
```

To test modded characters, load their mods with `EXTRA_MODS` and name two or three
remote characters by ID fragment (three makes a four-player fight):

```sh
EXTRA_MODS=/path/to/BaseLib:/path/to/Downfall \
ANIMATED_PLAYER_HANDS_CHARACTERS=HERMIT,AUTOMATON tests/run-native.sh
```

Seven Downfall characters pass this way. The Snecko can't be a remote player in
these tests because it asks its player for a choice on entering an act, and the
test's fake multiplayer can't answer a remote choice.

`--screenshots` renders under a private Xvfb (1280x720 by default; set `RESOLUTION` to change it) and writes PNGs to
`artifacts/native-tests/run-*/screenshots/`. Set `DOTNET` if `dotnet` is not on
your `PATH`, or `SKIP_BUILD=1` to reuse the last build.

## Layout

| Path | Contents |
| --- | --- |
| `src/RemoteHands.cs` | Attaches a hand to each remote player's creature, keeps their health bar visible, runs the hands every frame and lays them out. |
| `src/RemoteHandPanel.cs` | The card strip and hover preview for one player. |
| `src/RemoteStatusIcons.cs` | Energy, stars and pile icons placed ahead of a remote player's powers. |
| `src/PlayerFormation.cs` | Widens the gap between players standing side by side and moves the formation toward the middle. |
| `src/RemoteHandAnimator.cs` | Draw, discard, exhaust and slide animations, driven by the player's pile events. |
| `src/CardNodes.cs` | Borrows and returns card nodes from the game's card pool. |
| `src/Faults.cs` | Keeps display failures, such as a modded card that throws, out of the game's commands. |
| `src/HandLayoutGeometry.cs`, `src/HandMotion.cs` | Layout and motion math, tested without the game. |
