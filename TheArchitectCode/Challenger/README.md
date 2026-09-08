# Native in-process Challenger

`CorruptedChallenger` uses `NativeChallenger` in ordinary single-player gameplay.
It restores the previous-character snapshot, not a fixed demo deck. No launch
flag selects an alternative production engine. There is no RPC worker, scalar
damage replay, manual card-effect adapter fallback or singleton-state swapping.

## Ownership and native behavior

The actor has a private native `Player`, `RunState`, `PlayerCombatState`, deck,
energy, stars, RNG and card piles. Its player's Creature backing reference is
bound once to the actual Challenger enemy. Neither the private player nor its
run becomes a real party member, and `LocalContext` is unchanged.

Native save restoration preserves card IDs, upgrades, native enchantments and
saved properties. Combat copies retain their native deck-version relationship.
Cards execute through `SpendResources` and `OnPlayWrapper` inside the enemy move,
including live source/target hooks, native resource spending, powers, pile
movement, generated cards, choices, X costs and automatic plays.

`NativeCombatView` supplies owner-relative opponent/ally collections and private
run RNG while delegating actual combat mutations and absolute-side operations to
the live encounter. This prevents player-oriented native helpers such as random
enemy targeting from attacking their own Challenger. Native pets use the
private owner's RNG for creature creation as well.

Native call sites are rewritten as well as getters: BaseLib pre-JIT and native
inlining can bypass getter-only detours. The stable Creature backing reference,
explicit owner/participant reads, and explicit turn-loop phase boundaries are
essential. Do not replace them with temporary human state swaps.

The real turn loop owns side hooks. Actor energy and hand draw are prepared at
the start of the human turn, including the opening turn, so the human can inspect
the actual upcoming hand. The base draw is five, with native draw modifiers,
Innate, retention and the hand limit preserved. Extra human turns do not draw
another Challenger hand. After its block clears on the enemy turn, the actor
uses that prepared hand without resetting energy or drawing again; native
player-start hooks still run then. Orb start effects, pre-play, manual play and
post-play occur in its move. End-hand effects, Ethereal, retain/discard and orb passives resolve before
side-end completion. Native extra-turn hooks run additional actor turns before
the human side resumes, preparing their own hands because no human turn
intervenes. Powers and card-local mutations persist between turns.

The Challenger retains its monster lifecycle listener. Death adds the Architect
before removing owned pets and actor state, preserving continuous combat and
human hand/energy/HP. Native terminal outcomes, snapshot deduplication and atomic
replacement remain in the existing lifecycle/persistence implementation.

## NPC policy and display

- Play the first currently playable supported card from left to right, then
  reconsider the current hand after each native effect.
- Use the first live valid opponent for targeted attacks. Native random effects
  retain native randomness using the private actor RNG.
- Resolve native choices in their offered order, selecting the minimum required
  count, or one when a choice is optional; choose the first offered bundle.
- Human attack intent, where a native effect requests it, means **the targeted
  human currently holds any native Attack-type card at that exact query**.
  Energy, affordability, predicted future draws and the previous hand do not
  count. Go for the Eyes retains its native attack-then-intent-query ordering.
- A manual turn stops and reports a 100-play safety limit. More than 20
  consecutive extra turns is an explicit error, not an endless loop.

The telegraph shows only the current native hand, plus energy and pile counts.
An empty hand stays empty in the preview; neither the draw pile nor the discard
pile is presented as playable cards. Native cards enlarge on hover.
This is a state preview, **not an exact future-damage forecast**.

The actual native `NOrbManager` renders slots, passive/evoke effects and native
orb hover tips for every character, including characters initially having zero
slots. Challenger-owned Osty uses the enemy container and mirrored owner-relative
position, with native health, hitbox, summon/revive and scaling behavior. Ordinary
human pet layout is unchanged.

## Explicit boundaries

Co-op-only native cards and third-party card/enchantment/modifier effects are
outside this release's support. They are visibly labelled **Unsupported**, not
played or spent as fake no-op actions, and do not execute card/modifier combat
hooks. Their exact raw captured JSON remains in the profile snapshot. Unsupported
saved extension data is not handed to arbitrary extension deserializers when
constructing the unplayed native card.

Missing saved card, enchantment or BaseLib modifier models produce a visible
preflight error. Restore the matching content version before entering the boss;
the snapshot is not replaced. As in the earlier alpha, an unavailable saved
character is treated as a First Visit.

The captured schema contains character, max HP and deck, not the previous run's
relic inventory, potions, temporary powers or mid-combat orb state. Those are not
silently synthesized. This is not a certification of every vanilla combination;
new beta APIs or mod interactions can require further native integration.

Normal saves restart this encounter at the room boundary using the same entry
snapshot; there is no mid-turn continuation. The native JSON omission of the
compact Act's empty encounter/event lists is normalized for reload.
The separate snapshot/base-game crash-transaction gap remains tracked in #12.

## Isolated native runtime checks

Target: public-beta v0.111.0, commit `41cef1ea`, Steam build `24724944`,
BaseLib 3.4.5 and .NET 9.

```sh
scripts/build.sh --mods-path artifacts/mods
export ARCHITECT_SNAPSHOT_INPUT=/absolute/path/to/challenger_snapshot.json
bash scripts/native-demo.sh --exclusive-window "/absolute/path/to/Slay the Spire 2"
bash scripts/native-demo.sh --exclusive-window "/absolute/path/to/Slay the Spire 2" --loss
bash scripts/native-demo.sh --exclusive-window "/absolute/path/to/Slay the Spire 2" --nondefect
```

The script's historical name does not enable a fixture engine. Its explicit
`--architect-native-test` gate controls disposable storage and automation only.
The default scenario first loads the supplied snapshot through normal encounter
entry, then exercises native reload and targeted mechanics in that disposable
combat. The loss scenario follows saved-deck turns with a lethal multi-hit probe.
The non-Defect scenario writes an explicitly labelled disposable Ironclad orb
deck through the normal snapshot format, exercising zero-to-first-slot capacity.

The launcher requires Linux `bwrap`, X11 and authorization, installed game and
BaseLib, and an exclusive window with no existing game process. It refuses
concurrent launches. Root/game/mods are read-only, host home/runtime are hidden,
network access is denied, native saves use memory stores, and snapshot files are
written only under the disposable runtime. Steam is disabled before save/mod
startup with `--force-steam off`, before Godot's `--` argument separator.
Neither real saves nor installed mod files are changed. Merely changing
`XDG_DATA_HOME` is not sufficient isolation.

Receipts and viewport images are written to `artifacts/native-demo/run-*`.
`ARCHITECT_MODS_INPUT` can select an existing read-only mods directory, including
the final installed bytes, instead of `artifacts/mods`. Do not invoke this test
gate in a normal saved session. Sandbox audio-server warnings are expected.

The runtime probes cover real saved Defect cards and upgrades; Perfected Strike
and Impervious with native modifiers; generated cards, choices, persistent powers,
Rage/Thorns/Flame Barrier reactions, saved Genetic Algorithm properties and Sharp
enchantment, private RNG, orb rendering, Osty placement/revival, extra turns,
exclusion/error reporting, reload, cleanup, handoff and native terminal outcomes.
The old `ChallengerPlanner`, exact-adapter classes and pure tests are retained as
historical regression/reference code, but are not the production execution path.
