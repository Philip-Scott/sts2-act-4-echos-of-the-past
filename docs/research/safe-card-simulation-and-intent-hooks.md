# Safe card simulation and intent hooks

Research for [Wayfinder #8](https://github.com/Philip-Scott/slay-the-spire-the-architect/issues/8), performed against the repository at
`2df06571bdc7316c3524570c82e95e6180a3975e`, BaseLib at
`22757933ba10adc4322a628519a233a567507d87`, and a pinned decompilation of the
base-game `sts2.dll` at `1b7e7ce35e608722650763938c153ea8bc370333`.

## Scope and confidence

This is an **StS2/.NET/Godot** mod: the project targets `net9.0`, directly
references `sts2.dll`, and depends on `Alchyr.Sts2.BaseLib`
([project file](https://github.com/Philip-Scott/slay-the-spire-the-architect/blob/2df06571bdc7316c3524570c82e95e6180a3975e/TheArchitect.csproj#L1-L35)).
StS1's Java BaseMod/ModTheSpire/StSLib APIs therefore are not applicable to
this ticket. The first-party modding source used here is BaseLib-StS2 3.4.5,
whose manifest targets game version 0.107.1
([manifest](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/BaseLib.json#L1-L11)).

No local `sts2.dll` was available in this worktree and Mega Crit does not
publish the game source. Base-game links below therefore point to a pinned
source-level decompilation. That repository says the files were produced from
`sts2.dll`
([provenance](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/DECOMPILED_ARCHITECTURE.md#L1-L4)),
but it is not an authoritative Mega Crit repository. Treat exact names and
signatures as **version-sensitive observations**, and verify them against the
installed game assembly before implementation. BaseLib links are authoritative
for BaseLib itself.

## Conclusions

1. Represent the past character as a **custom `MonsterModel`**, not as a
   `Player` placed on the enemy side. A `Creature(Player, ...)` always sets
   `CombatSide.Player`, while `Creature(MonsterModel, side, slot)` accepts the
   enemy side and wires the monster lifecycle
   ([Creature](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Entities.Creatures/Creature.cs#L278-L301)).
2. Reconstruct saved cards through the game's `SerializableCard` and
   `CardModel.FromSerializable` path. Do not instantiate card types by name or
   replay their `OnPlay` methods.
3. Translate only explicitly registered, declarative effects into monster
   commands. Card `OnPlay` is arbitrary imperative code, and even ordinary
   cards can require a `Player`, piles, choices, RNG, VFX, relic hooks, or
   player-only resources.
4. If any card or component is unsupported, preserve the preview but replace
   its action with an explicit no-op/`UnknownIntent`; never partially execute
   an effect whose omitted portion changes targeting, cost, or value.
5. Render preview cards as UI only. Use either scaled `NCard` instances with
   detached preview models, or a smaller custom `Control` that reads card
   portrait/frame/title resources. Publish aggregate damage/block/buff/debuff
   through the monster's `MoveState.Intents`; the existing creature UI already
   creates and refreshes one `NIntent` per entry.

## 1. Safely representing a player character as an enemy

### Recommended representation

Create a `ChallengerMonsterModel : BaseLib.Abstracts.CustomMonsterModel`.
Store a read-only `ChallengerSnapshot` (character ID, max/current HP if desired,
serialized deck, and a deterministic simulation seed) on a separate service or
the monster model. The challenger is visually a former player character, but
mechanically remains a monster.

This respects the engine's split:

- `Creature` has mutually distinct `Monster` and `Player` references;
  `IsMonster`/`IsPlayer` derive from which reference exists
  ([Creature](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Entities.Creatures/Creature.cs#L99-L137)).
- `CombatState.CreateCreature` initializes monster RNG, HP scaling, combat ID,
  and attachment when given a mutable `MonsterModel`
  ([CombatState](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Combat/CombatState.cs#L118-L137)).
- Monster setup, move rolling, execution, history, removal, and intent refresh
  all live on `MonsterModel`
  ([MonsterModel](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Models/MonsterModel.cs#L303-L354)).
- BaseLib's `CustomMonsterModel` registers the custom type and supplies supported
  overrides for custom visuals, animation mappings, and SFX
  ([CustomMonsterModel](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Abstracts/CustomMonsterModel.cs#L11-L55),
  [animation helper](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Abstracts/CustomMonsterModel.cs#L58-L107)).

Do **not** mutate a real `Player`/`PlayerCombatState`, change a player's side, or
temporarily install the saved deck into the current combat. The player's
constructor fixes the side to `Player`; combat hook enumeration also treats
players and monsters differently, adding player relics, potions, orbs, cards,
enchantments, and afflictions only for actual players
([CombatState hook listeners](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Combat/CombatState.cs#L264-L323)).

For visuals, build a dedicated `NCreatureVisuals` scene using permitted
character-like art/animation, or return it from `CreateCustomVisuals`. BaseLib
explicitly supports both a custom scene path and fully generated visuals
([CustomMonsterModel](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Abstracts/CustomMonsterModel.cs#L18-L39)).
Do not reuse a live player `NCreature` node: player nodes add player-only orb and
multiplayer-intent UI based on `Entity.IsPlayer`
([NCreature](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Nodes.Combat/NCreature.cs#L220-L267)).

## 2. Reconstructing and inspecting saved cards

`SerializablePlayer.Deck` is a list of `SerializableCard`
([SerializablePlayer](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Saves.Runs/SerializablePlayer.cs#L12-L42)).
Each card preserves its `ModelId`, upgrade level, enchantment, saved properties,
and floor-added metadata
([SerializableCard](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Saves.Runs/SerializableCard.cs#L8-L27)).
`ModelId` serializes as `Category.Entry`, rather than a CLR type name
([ModelId](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Models/ModelId.cs#L18-L40)).

Use:

```csharp
CardModel preview = CardModel.FromSerializable(savedCard);
```

The base-game path resolves the ID, makes a mutable clone, restores saved
properties, runs deserialization, applies enchantment modification, and replays
upgrades in order
([CardModel](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Models/CardModel.cs#L1682-L1718)).
This is also the path used by `Player.FromSerializable`
([Player](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Entities.Players/Player.cs#L213-L260)).

This supports modded cards only when the defining mod is loaded and its model
type has been registered in `ModelDb`. `ModelDb` includes reflected mod
subtypes, constructs their canonical instances, and provides ID lookup
([ModelDb registration](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Models/ModelDb.cs#L52-L60),
[lookup](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Models/ModelDb.cs#L234-L265),
[by ID](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Models/ModelDb.cs#L325-L338)).
If the model is absent, the built-in load path returns `DeprecatedCard`
([SaveUtil](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Saves/SaveUtil.cs#L24-L36));
that must be classified unsupported, not simulated.

BaseLib can attach additional saved values to cards and register extra
serialization types
([ExtendedSaveTypes](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Patches/Saves/ExtendedSaveTypes.cs#L30-L68)).
Consequently, reconstruction must happen after all source mods initialize, and
compatibility must be keyed by `ModelId` plus an adapter/version contract—not
by assuming every `CustomCardModel` with a `DamageVar` has attack semantics.

### Safe inspection boundary

Safe structural fields include `Id`, `Type`, `Rarity`, `TargetType`, keywords,
tags, upgrade level, and (for a known adapter) named dynamic variables. Those
properties are exposed by `CardModel`
([metadata](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Models/CardModel.cs#L266-L294),
[target/variables](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Models/CardModel.cs#L411-L447)).
Dynamic calculations are not generally safe to evaluate: for example,
`BodySlam` reads `card.Owner.Creature.Block`
([BodySlam](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Models.Cards/BodySlam.cs#L14-L35)).

Never call `TryManualPlay`, `OnPlayWrapper`, or reflected `OnPlay` for the
challenger. The wrapper spends player resources, moves cards among combat
piles, invokes hooks/history, and executes card/enchantment/affliction code
([CardModel](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Models/CardModel.cs#L1395-L1435),
[execution](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Models/CardModel.cs#L1437-L1507)).

## 3. Pure simulation and supported action translation

Use a mod-owned, pure `ChallengerPlanner`; do not instantiate a hidden
`PlayerCombatState`. Its state should be plain data:

```text
drawOrder, hand, discard, exhausted, energy, turn, deterministic RNG,
and immutable CardDescriptor values derived from reconstructed cards
```

The base-game pile model establishes five combat piles and a ten-card hand
([PlayerCombatState](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Entities.Players/PlayerCombatState.cs#L19-L49),
[CardPile](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Entities.Cards/CardPile.cs#L13-L25)).
Preserve list order explicitly and use a mod-owned seeded RNG. Calling the
game's draw/shuffle commands is unsafe because they mutate piles, emit history,
invoke draw/shuffle hooks, and use the live run RNG
([CardPileCmd.Draw](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Commands/CardPileCmd.cs#L720-L779),
[shuffle](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Commands/CardPileCmd.cs#L782-L829)).

Likewise, use an explicit challenger energy budget and adapter-declared costs.
The real cost path can invoke global combat hooks and X-cost reads the owning
player's energy
([CardEnergyCost](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Entities.Cards/CardEnergyCost.cs#L56-L100)).

### Initial support matrix

| Card effect | Support | Translation | Required adapter evidence |
|---|---:|---|---|
| Fixed single-target damage | Yes | `MonsterActions.Attack(monster, damage)` + `SingleAttackIntent` | Exact `ModelId`, fixed damage var, enemy target |
| Fixed repeated damage | Yes | attack with `hitCount` + `MultiAttackIntent` | Exact repeat and per-hit values |
| Fixed self block | Yes | `CreatureCmd.GainBlock` + `DefendIntent` | Exact block var; no player-state dependency |
| Fixed self power/buff | Allowlist | `MonsterActions.ApplySelf<T>` + `BuffIntent` | Power explicitly audited for monster owner |
| Fixed debuff to all players | Allowlist | `MonsterActions.Apply<T>` + `DebuffIntent` | Power explicitly audited for player targets |
| Fixed heal self | Yes, if desired | `CreatureCmd.Heal` + `HealIntent` | Fixed amount; encounter balance cap |
| Damage + audited debuff | Yes | ordered attack then allowlisted power; both intents | Entire compound card adapter required |
| Draw/discard/exhaust/retain | Planner only | mutate pure simulated piles; no combat command | Explicit planner rule |
| Energy gain/cost change | Planner only | mutate pure energy/cost snapshot | Explicit planner rule |
| X-cost, random target/value | No initially | explicit fallback | Requires deterministic policy and UI semantics |
| Player choice / card selection | No | explicit fallback | Never invoke choice UI |
| Orb, potion, relic, gold, stars, summon, deck mutation | No initially | explicit fallback | Player/run-only state or lasting side effects |
| Calculated/contextual damage | No by default | explicit fallback | Only add a bespoke adapter with bounded inputs |
| Unknown modded card or `DeprecatedCard` | No | explicit fallback | Adapter absent or model missing |

BaseLib already packages the safe command/intent pairs: attack, block, power to
players, power to self, and heal
([MoveBuilder](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Monsters/MoveBuilder.cs#L67-L153),
[heal](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Monsters/MoveBuilder.cs#L183-L201)).
Its `MonsterActions` ensures damage is sourced from a monster and uses a
throwing choice context by default for power application
([MonsterActions](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Utils/MonsterActions.cs#L8-L35)).
That is the preferred execution substrate.

The allowlist requirement is substantive, not cosmetic. `Bash` happens to map
cleanly to damage plus Vulnerable
([Bash](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Models.Cards/Bash.cs#L18-L41)),
whereas `Acrobatics` opens a hand-selection flow
([Acrobatics](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Models.Cards/Acrobatics.cs#L12-L29))
and `Alchemize` creates a potion using player RNG
([Alchemize](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Models.Cards/Alchemize.cs#L10-L25)).
All are ordinary `CardModel` subclasses; metadata alone cannot distinguish
their full behavior.

## 4. Explicit fallback behavior

Every planned card should produce one of:

```text
Supported(adapter id, translated actions)
PlannerOnly(reason)
Unsupported(reason code, card ModelId)
MissingModel(original ModelId)
AdapterError(adapter id, exception summary)
```

Recommended policy:

- Unsupported cards remain visible in the miniature queue with a warning badge.
- They consume **no** real combat resources and execute **no** partial effect.
- In the pure planner, choose one documented rule: discard without effect
  (recommended), retain, or replace with a fixed weak move. Do not silently
  reinterpret it.
- If at least one selected card is unsupported, add `UnknownIntent` and expose
  the card IDs/reasons in hover text or the miniature badge. `UnknownIntent` is
  a built-in intent with the unknown sprite
  ([UnknownIntent](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.MonsterMoves.Intents/UnknownIntent.cs#L1-L10)).
- Catch failures at descriptor construction and adapter planning boundaries,
  before installing the `MoveState`. Never catch and continue after the first
  live command has executed; that would leave a partially applied move.
- Persist adapter schema/version and source mod ID alongside any cached plan.
  Rebuild the plan when the game, BaseLib, or source mod version changes.

## 5. Card miniatures and aggregate intent

### Miniatures

The native `NCard` is reusable UI: `NCard.Create(card, visibility)` obtains a
pooled card node and assigns its model; its natural size is 300×422 multiplied
by node scale
([NCard](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Nodes.Cards/NCard.cs#L475-L490),
[sizing](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Nodes.Cards/NCard.cs#L522-L525)).
It renders title, cost, description, portrait, frame, rarity, and enchantment
from the model
([binding](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Nodes.Cards/NCard.cs#L355-L379),
[visual reload](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Nodes.Cards/NCard.cs#L820-L903)).

Place a mod-owned `Control` above the challenger in its custom visuals scene,
create one scaled, non-interactive `NCard` per planned card, and return pooled
nodes correctly when the plan changes. Keep preview models out of all live
piles and never call `SetPreviewTarget`/`UpdateVisuals` for unsupported cards:
visual updates evaluate dynamic previews
([NCard](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Nodes.Cards/NCard.cs#L527-L562)).
If detached `NCard` instances prove to assume owner/combat state for a modded
card, fall back to a custom miniature using only its safe portrait/frame/title
resources and an unsupported badge.

### Aggregate intent

A `MoveState` accepts an arbitrary intent array
([MoveState](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine/MoveState.cs#L11-L60)).
`NCreature.UpdateIntent` creates/updates one `NIntent` for every entry in
`NextMove.Intents`
([NCreature](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Nodes.Combat/NCreature.cs#L373-L400)).
Therefore aggregate the selected cards into a small ordered intent list:

1. one `SingleAttackIntent(total)` or `MultiAttackIntent(perHit, count)` when
   the damage shape is honestly representable;
2. at most one each of `DefendIntent`, `BuffIntent`, and `DebuffIntent`;
3. one `UnknownIntent` if any component is unsupported.

Do not collapse unequal hits into `MultiAttackIntent`: its label and total are
defined as one damage value times one repeat count
([MultiAttackIntent](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.MonsterMoves.Intents/MultiAttackIntent.cs#L8-L41)).
For mixed unequal attacks, either show separate attack intents in card order or
author a custom `AbstractIntent`. The base class owns texture, animation, label,
and hover-tip hooks
([AbstractIntent](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.MonsterMoves.Intents/AbstractIntent.cs#L14-L75));
`NIntent` updates those values on combat-state changes and displays numeric
labels only for attack/status intents
([NIntent](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Nodes.Combat/NIntent.cs#L142-L172)).

## Recommended architecture

```text
SavedChallengerSource
  -> List<SerializableCard>
  -> CardReconstructor (base-game FromSerializable; no owner/piles)
  -> CardDescriptorFactory
       -> exact ModelId adapter registry
       -> structural metadata for display only
       -> Supported | PlannerOnly | Unsupported | MissingModel
  -> ChallengerPlanner (pure state, own RNG, energy, piles, policy)
  -> PlannedTurn
       cards[] + translated action DTOs + fallback diagnostics
  -> ChallengerMonsterModel.GenerateMoveStateMachine()
       action DTOs -> BaseLib MoveBuilder/MonsterActions
       summaries -> built-in/custom AbstractIntent[]
  -> ChallengerPreviewControl
       detached NCard/custom miniatures + badges
```

Keep these boundaries one-way. Reconstruction may inspect game models; the
planner operates on immutable descriptors; only the executor touches live
combat. Install a complete `MoveState` atomically after planning. `MoveBuilder`
collects action delegates and intents before constructing the state
([MoveBuilder](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Monsters/MoveBuilder.cs#L26-L32),
[build](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Monsters/MoveBuilder.cs#L242-L279)).

## Safety constraints

- Never execute a saved card's `OnPlay` or card-play wrapper.
- Never make the challenger a `Player`, add it to `CombatState.Players`, or
  borrow the live player's `PlayerCombatState`.
- Never attach preview card models to live run/combat piles.
- Allowlist exact `ModelId`s and power types; reject unknown mod versions.
- Treat `DeprecatedCard`, missing mods, malformed save properties, unavailable
  assets, and adapter exceptions as explicit unsupported results.
- Keep planner RNG independent from `RunState.Rng`; consuming game RNG while
  previewing would alter combat outcomes.
- Compute intent and action DTOs from the same immutable plan so displayed
  damage cannot diverge from execution.
- Cap total hits, damage, block, healing, generated nodes, and preview-card
  count to protect combat pacing and UI.
- Validate multiplayer separately: native attack-intent calculation runs
  damage hooks against the local player context
  ([AttackIntent](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.MonsterMoves.Intents/AttackIntent.cs#L74-L93)).

## Unresolved risks and required spikes

1. **Version skew:** verify all pinned decompiled signatures against the
   installed `sts2.dll` for the project's minimum and current supported game
   versions.
2. **Save provenance:** define which historical save/run snapshot is selected,
   how missing source mods are surfaced, and whether loading foreign saved
   properties can run mod code during `AfterDeserialized`.
3. **Adapter ownership:** no generic API exposes a declarative card-effect AST.
   Supporting modded cards requires opt-in adapters or a small public provider
   interface owned by this mod.
4. **Preview model assumptions:** spike detached `NCard` with upgraded,
   enchanted, X-cost, calculated, and modded cards. Use the custom-thumbnail
   fallback if any accesses `Owner`, `CombatState`, or live hooks.
5. **Exact simulation semantics:** a pure planner intentionally does not run
   relic/power/card hooks. Label it a challenger policy, not an exact replay of
   the original player's combat engine.
6. **Mixed aggregate damage:** decide between several native attack intents and
   a custom summary intent; never show mathematically false `damage × hits`.
7. **Asset lifecycle:** ensure custom monster visuals preload every custom
   intent/card-preview resource and return pooled `NCard` nodes without stale
   subscriptions.
8. **Multiplayer:** test target sets, scaling, local-context damage previews,
   action synchronization, and deterministic planner state on every peer
   before enabling this encounter in multiplayer.
9. **Model ID collision:** the base game already defines a monster class named
   `Architect`
   ([base-game Architect](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Models.Monsters/Architect.cs#L9-L24)),
   while model entries are slugified from CLR type names
   ([ModelDb](https://github.com/zhiyue/sts2-rl-agent/blob/1b7e7ce35e608722650763938c153ea8bc370333/decompiled/MegaCrit.Sts2.Core.Models/ModelDb.cs#L295-L323)).
   Use a distinct class/entry such as `PastSelfChallenger`, not `Architect`.
