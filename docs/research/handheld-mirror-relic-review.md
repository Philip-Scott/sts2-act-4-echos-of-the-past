# Handheld Mirror: native relic duplication review

Research: **2026-09-09**. Scope: the installed native game and Handheld
Mirror's original **fresh canonical-copy acquisition**, not copying an owned
relic's saved state. The catalog and findings below preserve that initial review
snapshot; the approved implementation follow-up is recorded separately here.

## Approved follow-up

The user approved these changes on **2026-09-09**:

- **Block Archaic Tooth, Paper Krane, Paper Phrog, Lava Rock, Winged Boots, and
  Byrdpip**, in addition to the original five native exclusions.
- **Keep Dusty Tome eligible**, preserving the original's `AncientCard` before
  normal acquisition rather than rerolling or leaving it unprepared.
- **Keep Sea Glass eligible**, preserving the original's `CharacterId` rather
  than falling back to Ironclad.
- **Keep Girya eligible**, preserving `TimesLifted`, including three lifts on a
  fully trained original. Native Lift behavior is otherwise unchanged.

These changes are implemented in [MirrorDuplication.cs](../../TheArchitectCode/Relics/MirrorDuplication.cs)
and [HandheldMirror.cs](../../TheArchitectCode/Relics/HandheldMirror.cs).
Prepared choices and training come from the first non-melted owned copy of the
selected type; all three copies are prepared before any pickup effect runs.
Other counters still start fresh. Missing Dusty Tome / Sea Glass preparation
raises an explicit error instead of inventing a reward.

There are now **11 blocked native types / 288 remaining native types**, plus the
existing exclusion of Handheld Mirror itself and melted instances. The **294-row
catalog below is the original review snapshot**, including the six newly
blocked entries for reference. **No other review candidate is approved for
exclusion.** Earlier failure descriptions below refer to the pre-fix behavior.

## Review first: prioritized candidates

These are discussion priorities, **not a proposed automatic blocklist**.
“Method-probed” means an isolated headless invocation against the installed
assembly; it does **not** mean a Mirror selection was playtested in-game.

| Priority | Relic(s) | Why review this duplicate? | Evidence / confidence |
| --- | --- | --- | --- |
| **1: acquisition failure** | **DustyTome** | Its reward card ID is assigned by Ancient setup, not the canonical model. Mirror's fresh copy has no ID; acquisition looks it up anyway. | `DustyTome.SetupForPlayer`, `AncientCard`, `AfterObtained`; `ModelDb.GetById`. **High: method-probed `ArgumentNullException`** for an unprepared fresh copy. [G-setup][P] |
| **1: acquisition failure** | **ArchaicTooth** | The first acquisition normally consumes the special starter card. Reacquisition searches for that starter again and dereferences the result without a null guard. | `GetTranscendenceStarterCard`, `GetTranscendenceTransformedCard`, `AfterObtained`. **High: method-probed `NullReferenceException`** with no eligible starter. Not unconditional if another eligible starter exists. [G-setup][P] |
| **2: wrong state updated** | **Girya** | The fresh copy has zero training, but every Lift action trains the *first* owned Girya. A trained original plus a fresh copy can reopen training and push the original past the intended three lifts while leaving the copy at zero. | `Girya.TryModifyRestSiteOptions`; `LiftRestSiteOption.OnSelect`; `Player.GetRelic<T>`. **High: method-probed `(3,0) → (4,0)`**. [G-rest][P] |
| **2: wrong reward identity** | **SeaGlass** | The selected character is saved on the original. Fresh copies lose it; acquisition explicitly falls back to Ironclad rather than reproducing the original character's offer. Its different character-specific display names are still **one model ID**, not separate eligible types. | `CharacterId`, `Title`, `AfterObtained`; `Orobas.SeaGlassOptions`. **High static**, null default also method-probed; reward screen not exercised. [G-setup][P] |
| **2: extra copy ignored** | **PaperKrane**, **PaperPhrog** | Their multiplier changes are reached through a single `GetRelic<T>()`, not a per-relic hook. A second ordinary copy adds no second paper multiplier. | `WeakPower.ModifyDamageMultiplicative`; `VulnerablePower.ModifyDamageMultiplicative`; paper helper methods. **High static**; first-copy lookup separately probed. [G-singleton][P] |
| **3: shared resource/state** | **ToyBox**, **PumpkinCandle**, **WingedBoots** | Toy Boxes melt from one shared leftmost wax-relic list; candles share a first-copy Kindle target; Boots spend charges on the same off-path move rather than maintaining an additive shared charge bank. These can still grant real benefits. | `ToyBox.AfterCombatEnd`; `KindleRestSiteOption.OnSelect`; `WingedBoots.AfterRoomEntered`. **High static mechanism; gameplay impact untested**. [G-shared] |
| **3: expired opportunity** | **LavaRock** | Its reward requires `CurrentActIndex == 0` and a boss room. A fresh copy in the Architect's appended act cannot make the Act 1 boss happen again. | `LavaRock.TryModifyRewards`. **High static**, route-dependent uselessness rather than a crash. [G-opportunity] |
| **3: potentially redundant pets** | **Byrdpip**, **PaelsLegion** | Each copy summons a pet, while related card/animation/skin paths use the first matching pet or relic. Byrdpip's pickup transforms existing eggs; it does not unconditionally grant another attack card. Pael's Legion also has real per-copy block multipliers. | `BeforeCombatStart`, `SummonPet`; `PlayerCmd.AddPet`; `PlayerCombatState.GetPet`; `ByrdSwoop.OnPlay`. **Medium concern**, high confidence in the mixed addressing; no pet/UI failure reproduced. [G-pets] |

For the next approval discussion: resolve the two acquisition failures first,
then Girya and Sea Glass, then decide whether **pointless duplication** should
be treated differently from **broken duplication**. The broader redundant-effect
list below is deliberately separate from those failures.

## Scope, counts, and version

**299 concrete native `RelicModel` types discovered; 294 remaining after the
five already-approved native exclusions.** This includes the fallback Circlet
and the legacy Deprecated Relic. Excluding those two nonstandard entries leaves
**297 non-placeholder models / 292 remaining non-placeholder models**. “Model”
does not mean an independently spawnable reward in every run. [G-inventory]

| Native `Rarity` | Discovered | Already excluded | Remaining |
| --- | ---: | ---: | ---: |
| Starter | 10 | 0 | 10 |
| Common | 30 | 0 | 30 |
| Uncommon | 40 | 0 | 40 |
| Rare | 50 | 0 | 50 |
| Shop | 30 | 0 | 30 |
| Event | 35 | 0 | 35 |
| Ancient | 102 | 5 | 97 |
| None: fallback / legacy | 2 | 0 | 2 |
| **Total** | **299** | **5** | **294** |

- Installed `release_info.json`: **v0.111.0**, commit **41cef1ea**,
  release timestamp `2026-08-13T17:39:18-07:00`. These match the repository's
  documented public-beta target / Steam build **24724944**; the Steam manifest
  was not independently re-read for this note. [G][R]
- Actual assembly: `sts2, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null`;
  module MVID **`8a76776c-0ce1-4d4f-90bd-8cce653dad8e`**.
  SHA-256 **`2b40d2df538db1ceb5fa48d958c80ab730ada1e07db88a870aff01a661768b9f`**.
  The assembly version is **not** the public-beta version. [G][P]
- Native means assignable to `RelicModel` **in this `sts2.dll`**, including
  non-sealed concrete classes. It does not mean all DLLs in the installed
  `mods` directory. No third-party mod inventory or live saves were examined.
- Ten `Fake*` relics are genuine native event models, not test stubs.
  `DeprecatedRelic` is explicitly legacy; `Circlet` is a fallback.
  `VakuuCardSelector` lives in the relic namespace but implements
  `ICardSelector`, **not `RelicModel`**, and is not counted. [G-inventory]
- The five upgraded starter relics are counted with Starter, not Ancient:
  `BlackBlood`, `DivineDestiny`, `InfusedCore`, `PhylacteryUnbound`,
  `RingOfTheDrake`. Native pool membership and rarity are different concepts.
  [G-inventory]

### Existing exclusions — unchanged

| Native model | Existing approved exclusion |
| --- | --- |
| `TouchOfOrobas` | Already excluded; starter-relic replacement. |
| `PaelsEye` | Already excluded; extra-turn behavior. |
| `GoldenCompass` | Already excluded; map replacement. |
| `FurCoat` | Already excluded; marked-map encounter behavior. |
| `LordsParasol` | Already excluded; automatic merchant purchases. |

The mod's `HandheldMirror` itself and melted relic instances are also excluded,
but are **not five additional native models**. Eligibility otherwise includes
native and modded types. CanOffer requires a living owner and at least three
distinct eligible IDs. The population is captured before acquisition; three
distinct IDs are selected uniformly without replacement; normal
`RelicCmd.Obtain` is used on fresh canonical mutable copies. These are the
review's current implementation assumptions, not changes made here. [M]

## Duplicate mechanisms worth distinguishing

### Fresh acquisition is not an ownership-state clone

`RelicCmd.Obtain` adds the object to the player's inventory and then awaits
`AfterObtained`. `IsStackable == false` removes a model from reward grab bags;
it does **not** reject a second owned object. `Player.AddRelicInternal` appends
or inserts without an ID-uniqueness check. Acquisition is not shown as a
transaction that rolls back earlier obtained copies when a later callback
fails. That makes the Dusty Tome / Archaic Tooth failures more consequential
than simply losing one reward. This is source analysis, not a reproduced
partial-Mirror outcome. [G-acquire]

Normal hook enumeration includes each non-melted owned relic; some
`Should...` hooks stop at a first preventer, while others combine booleans.
Consequently, “the game supports duplicate objects” does not establish that
every effect stacks. Neither `HasUponPickupEffect` nor `IsUsedUp` is a
universal safety test. [G-acquire][G-hooks]

### Redundant-effect candidates, not acquisition failures

All names here remain eligible unless separately approved for exclusion:

- **Boolean / set-like benefits:** `IceCream`, `RunicPyramid`,
  `RingingTriangle`, `MiniatureTent`, `DingyRug`, `Driftwood`,
  `JuzuBracelet`, `WhiteBeastStatue`. They respectively preserve energy,
  suppress flushing, preserve the first-turn hand, keep rest options open,
  union a card pool, set a reroll flag, remove a possible room type, or force
  a potion reward. A second copy does not mean twice the same permission.
  **High static confidence**, not gameplay-tested. [G-idempotent]
- **Fixed cap / threshold:** `SturdyClamp` uses the first block-clear preventer
  and keeps at most ten block; `TheBoot` raises low damage to the same floor;
  `BeatingRemnant` copies observe the same HP-loss history and apply the same
  twenty-HP cap; `BrilliantScarf` copies reach the same fifth-card discount and
  both set its cost to zero. **High static confidence** for the normal
  between-combat duplication case, not a universal interaction proof.
  [G-threshold]
- **Already-upgraded / already-enchanted targets:** `FrozenEgg`, `MoltenEgg`,
  `ToxicEgg`, `Bellows`, `RazorTooth`, `LavaLamp`, `FresnelLens`, `Glitter`,
  `GhostSeed` generally revisit targets the first copy has already modified.
  Upgrade limits, enchantment eligibility and keyword guards matter.
  These are **redundancy candidates, not blanket “always useless” claims**:
  hook ordering, eligible new targets, or other content can change marginal
  benefit. `BoneTea` deserves the same check when both copies still have uses.
  [G-upgrades]
- **Acquisition with exhausted targets:** `PandorasBox` often finds no basic
  Strikes/Defends after the original; `NutritiousSoup` and `PaelsClaw` often
  find their eligible starters already enchanted; `NeowsTalisman` chooses the
  last matching basic Strike and Defend, without selecting a different
  unupgraded copy. `LeafyPoultice` is worse when no targets remain: it still
  pays its max-HP cost *before* conditionally transforming the available
  starters. All are **conditional** on the current deck. [G-targets]
- **Intentionally inert / drawback-only:** `FakeMerchantsRug`,
  `WongoCustomerAppreciationBadge`, `Circlet`, and `DeprecatedRelic` have no
  duplication benefit established by their relic implementation.
  `FakeSneckoEye` only reapplies a single-stack Confused power, `RoyalPoison`
  causes recurring HP loss, and a fresh `TeaOfDiscourtesy` renews its
  next-combat Dazed penalty. These are usefulness/balance questions, not
  evidence of a broken collection. [G-inert]

### Shared menus, counters, and remaining opportunities

- **Girya / Pumpkin Candle:** each relic can add an option, but the Lift and
  Kindle action finds the first relic of its type. Candle's fresh copy still
  starts with five combats of energy; the concern is subsequent replenishment,
  not an entirely dead duplicate. [G-rest][G-shared]
- **Shovel / Meat Cleaver / Pael's Growth / Pael's Wing:** source appends
  duplicate Dig, Cook, Clone, or Sacrifice options. Rest option generation
  uses a list and does not de-duplicate it. A Sacrifice option closes its
  reward and increments its bound relic's counter, not all copies' counters.
  These warrant a menu/controller/multiplayer check; **no duplicate-key crash
  is claimed**. Pael's Growth also repeats its pickup enchantment, so it is not
  purely redundant. [G-rest]
- **Toy Box:** fresh acquisition offers five new wax relics, but each active
  Box melts the owner's first non-melted wax relic on its own three-combat
  schedule, with no per-box provenance. Copying an eligible wax relic itself
  from its canonical model also does not preserve the wax flag. The former
  is shared-resource behavior; the latter follows Mirror's specified
  fresh-copy semantics, not a hidden implementation defect. [G-shared][M]
- **Winged Boots:** each unused copy charges itself for the same off-path
  transition. Duplicating three remaining charges does not necessarily create
  six jumps. A fresh copy can still help if the original is depleted or partly
  spent. The Architect's fixed route has no ordinary branch to exploit.
  [G-shared][R]
- **Delicate Frond:** each callback fills the same potion belt. If the first
  copy has already filled it and no intervening effect opens a slot, the
  second supplies nothing. The loop breaks on failed procurement, so pairing
  it with a potion-acquisition veto is not evidence of an endless loop.
  **High static confidence** in that limited behavior. [G-reference]
- **Whispering Earring:** each instance runs its own first-turn autoplay loop,
  with its own thirteen-card limit. A second loop could continue after the
  first reaches that limit if playable cards/resources remain. This is an
  **uncertain gameplay/agency concern**, not a reproduced crash or proof that
  the extra energy is useless. [G-reference]
- **Pet caution:** native `PlayerCombatState.GetPet<T>` explicitly describes
  returning the first pet when several have the same type; `AddPetInternal`
  prevents reinserting the same object, not a second object of its type.
  Thus the pet shortlist is a request to verify useful duplicate behavior
  and presentation, **not a claim that multiple same-type pets are forbidden**.
  [G-pets]
- **Delayed rewards:** fresh `WongosMysteryTicket` restarts at five combats;
  `SwordOfStone` restarts at five elite victories before replacing *itself*
  with Sword of Jade. Neither threshold fits a remaining Rest → Shop → Boss
  route. `BlackStar`, `WhiteStar`, `WarHammer`, `BoomingConch` and
  `SlingOfCourage` likewise need elites absent from that route, whereas
  `Pantograph` still has a boss trigger. `FishingRod` needs ordinary combats.
  **Route-specific low value is not a duplicate correctness bug.**
  [G-opportunity][R]
- **Refreshed consumable state can be useful:** `LizardTail`, `EmberTea`,
  `MawBank`, `SilverCrucible`, `SilkenTress`, and the ticket may restart uses
  or progress from canonical defaults. Tail's death prevention chooses one
  preventer at a time, so an additional unused Tail is not automatically
  wasted. Crucible can also renew chest suppression; Tress loses current gold
  again. Do not copy the “used” flag into a conclusion about the fresh object.
  [G-counters]

### Reference cases: strong stacking is not enough to exclude

- **Membership Card** multiplies the incoming merchant price by 0.5;
  **The Courier** multiplies it by 0.8. `Hook.ModifyMerchantPrice` feeds the
  running result to each listener, so ordinary repeated discounts have
  cumulative effect, before merchant rounding. Courier's refill permission is
  boolean, but its discount is not. Membership Card explicitly gates its
  adjustment to the local owner; network/UI correctness was not playtested.
  [G-shop]
- **Bing Bong** adds copies with a non-null `clonedBy` source and only reacts
  when `clonedBy == null`. That rejects the naive claim that two copies must
  recursively duplicate each other's outputs forever. Multiple extra cards
  from the original acquisition remain ordinary strong stacking. [G-reference]
- **Orichalcum / fake Orichalcum** record whether to trigger in a very-early
  phase and grant block later. Do not assume the first block gain necessarily
  suppresses the second copy. **Unceasing Top** reacts to the hand-empty event
  without rechecking the hand's count inside that callback; do not classify
  its second copy as certainly useless either. [G-reference]
- **Snecko Eye** has a single-stack confusion component but separate additive
  draw modifiers. **Ectoplasm**, **Sozu**, **Fiddle**, **VelvetChoker** and
  **PrismaticGem** combine a repeatable resource modifier with a restriction
  or shared permission; a redundant restriction does not erase the benefit.
  `PhilosophersStone`, `Brimstone`, `SealOfGold`, `SpikedGauntlets`, and
  `ToastyMittens` can repeat both benefits and costs. [G-reference]
- **Ordinary reacquisition:** removal/transform/enchant selectors such as
  `EmptyCage`, `Astrolabe`, `DollysMirror`, and relic/card rewards such as
  `CallingBell`, `SmallCapsule`, `LargeCapsule`, `Orrery`, and `NeowsBones`
  repeat pickup actions. Reduced targets, curses, inventory limits and
  consuming reward pools are normal acquisition concerns; their names or
  “one-time” descriptions alone do not prove a duplication failure.
  [G-acquire][G-reference]
- **Potential death is not a bespoke game-ending relic:** HP/max-HP costs
  (`PrecariousShears`, `FragrantMushroom`, `LeafyPoultice`, `SereTalon`) and
  `Storybook`'s Brightest Flame card need ordinary lethal-cost review.
  `CreatureCmd.LoseMaxHp` applies current-HP damage before clamping final max
  HP to at least one, so that clamp alone is not proof of survival.
  No direct “win/end the run on duplicate pickup” call was found in the native
  relic-body screen. That is a search result, **not proof of every transitive
  interaction**. [G-costs]

### Multiplayer boundary

`MassiveScroll` is explicitly **multiplayer-only** in `IsAllowed`, and its
pickup selects from multiplayer-only cards. `SilverCrucible` and
`WingedBoots` are explicitly **single-player-only** in `IsAllowed`. Those are
spawn restrictions; Mirror's owned-ID eligibility does not rerun every
native spawn predicate. No reason was established to block an ordinary
owned Massive Scroll solely for being multiplayer content. A forced or
imported invalid-mode inventory is a separate unsupported-state question.
No multiplayer session or synchronization/UI behavior was exercised. [G-multi]

## Complete remaining native catalog

Navigation: [Starter](#starter--10) · [Common](#common--30) ·
[Uncommon](#uncommon--40) · [Rare](#rare--50) · [Shop](#shop--30) ·
[Event](#event--35) · [Ancient](#ancient--97) ·
[Fallback / legacy](#none--2-fallback--legacy).

Every row's display name and **paraphrased effect** was checked against the
installed English relic localization. The class identifier is authoritative;
it is the suffix of `MegaCrit.Sts2.Core.Models.Relics.<Class>`. The source-member
column names actual members in that class for follow-up; it is **not** a claim
that every helper, hook interaction and UI path was traced. Category comes from
the native `Rarity` getter, not memory of Slay the Spire 1. [L][G-inventory]

Status:

- **C** — candidate for discussion; linked to the relevant evidence above.
  **Not approved for exclusion.**
- **R** — inspected reference case / expected tradeoff; not an exclusion
  recommendation and not a universal safety certification.
- **N** — no specific concern found in the initial localization +
  implementation-structure screen; **individual behavior not fully inspected**.
  This is explicitly not “safe.”

Coverage in the 294 remaining rows: **60 C / 35 R / 199 N**. These are review
labels, not approvals; the broad C set includes route-specific low value,
redundancy and uncertain interactions, not sixty proven bugs.

### Starter — 10

| # | Native model / display name | Effect paraphrase | Review status | Source member(s) |
| ---: | --- | --- | --- | --- |
| 1 | `BlackBlood` — Black Blood | Improved post-victory healing. | N | `AfterCombatVictory` |
| 2 | `BoundPhylactery` — Bound Phylactery | Summons Osty initially and on subsequent turns. | N | `BeforeCombatStart; AfterEnergyResetLate` |
| 3 | `BurningBlood` — Burning Blood | Post-victory healing. | N | `AfterCombatVictory` |
| 4 | `CrackedCore` — Cracked Core | Opening Lightning channel. | N | `BeforeSideTurnStart` |
| 5 | `DivineDestiny` — Divine Destiny | Improved opening Stars. | N | `AfterSideTurnStart` |
| 6 | `DivineRight` — Divine Right | Opening Stars. | N | `AfterRoomEntered` |
| 7 | `InfusedCore` — Infused Core | Improved opening Lightning and extra Lightning damage. | N | `AfterSideTurnStart; ModifyOrbValue` |
| 8 | `PhylacteryUnbound` — Phylactery Unbound | Improved initial and recurring Osty summons. | N | `BeforeCombatStart; AfterSideTurnStart` |
| 9 | `RingOfTheDrake` — Ring of the Drake | Improved early-turn draw. | N | `ModifyHandDraw` |
| 10 | `RingOfTheSnake` — Ring of the Snake | Additional opening draw. | N | `ModifyHandDraw` |

### Common — 30

| # | Native model / display name | Effect paraphrase | Review status | Source member(s) |
| ---: | --- | --- | --- | --- |
| 11 | `AmethystAubergine` — Amethyst Aubergine | Extra gold from enemy rewards. | N | `TryModifyRewards; AfterModifyingRewards` |
| 12 | `Anchor` — Anchor | Opening block. | N | `BeforeCombatStart` |
| 13 | `BagOfMarbles` — Bag of Marbles | Opening Vulnerable on enemies. | N | `BeforeSideTurnStart` |
| 14 | `BagOfPreparation` — Bag of Preparation | Additional opening draw. | N | `ModifyHandDraw` |
| 15 | `BloodVial` — Blood Vial | Opening healing. | N | `AfterPlayerTurnStartLate` |
| 16 | `BoneFlute` — Bone Flute | Block when Osty attacks. | N | `AfterAttack` |
| 17 | `BookOfFiveRings` — Book of Five Rings | Healing after a number of deck additions. | N | `AfterCardChangedPiles` |
| 18 | `BronzeScales` — Bronze Scales | Opening Thorns. | N | `AfterRoomEntered` |
| 19 | `CentennialPuzzle` — Centennial Puzzle | Draw after the first HP loss of a combat. | N | `AfterDamageReceived; AfterCombatEnd` |
| 20 | `DataDisk` — Data Disk | Opening Focus. | N | `AfterRoomEntered` |
| 21 | `FencingManual` — Fencing Manual | Opening Forge. | N | `AfterSideTurnStart` |
| 22 | `FestivePopper` — Festive Popper | Opening area damage. | N | `AfterPlayerTurnStart` |
| 23 | `Gorget` — Gorget | Opening Plating. | N | `AfterRoomEntered` |
| 24 | `HappyFlower` — Happy Flower | Periodic energy across turns. | N | `AfterSideTurnStart; AfterCombatEnd` |
| 25 | `JuzuBracelet` — Juzu Bracelet | Prevents ordinary enemy outcomes in unknown rooms. | C: redundant permission [G-idempotent] | `ModifyUnknownMapPointRoomTypes` |
| 26 | `Lantern` — Lantern | Opening energy. | N | `AfterSideTurnStart` |
| 27 | `MealTicket` — Meal Ticket | Healing on shop entry. | N | `AfterRoomEntered` |
| 28 | `OddlySmoothStone` — Oddly Smooth Stone | Opening Dexterity. | N | `AfterRoomEntered` |
| 29 | `Pendulum` — Pendulum | Periodic extra draw. | N | `BeforeHandDraw; ModifyHandDraw` |
| 30 | `PotionBelt` — Potion Belt | Adds potion slots. | N | `AfterObtained` |
| 31 | `RedMask` — Red Mask | Opening Weak on enemies. | N | `BeforeSideTurnStart` |
| 32 | `RedSkull` — Red Skull | Additional Strength while sufficiently low on HP. | N | `AfterRoomEntered; AfterCombatEnd` |
| 33 | `RegalPillow` — Regal Pillow | Extra healing from resting. | N | `ModifyRestSiteHealAmount; AfterRestSiteHeal` |
| 34 | `SneckoSkull` — Snecko Skull | Additional Poison when Poison is applied. | N | `ModifyPowerAmountGivenAdditive; AfterModifyingPowerAmountGiven` |
| 35 | `Strawberry` — Strawberry | Immediate max-HP increase. | N | `AfterObtained` |
| 36 | `StrikeDummy` — Strike Dummy | Extra damage for Strike-named cards. | N | `ModifyDamageAdditive` |
| 37 | `Vajra` — Vajra | Opening Strength. | N | `AfterRoomEntered` |
| 38 | `VenerableTeaSet` — Venerable Tea Set | Next-combat opening energy after rest-site entry. | N | `AfterRoomEntered; AfterEnergyReset` |
| 39 | `WarPaint` — War Paint | Random permanent Skill upgrades. | N | `AfterObtained` |
| 40 | `Whetstone` — Whetstone | Random permanent Attack upgrades. | N | `AfterObtained` |

### Uncommon — 40

| # | Native model / display name | Effect paraphrase | Review status | Source member(s) |
| ---: | --- | --- | --- | --- |
| 41 | `Akabeko` — Akabeko | Opening Vigor. | N | `AfterSideTurnStart` |
| 42 | `BookRepairKnife` — Book Repair Knife | Healing from eligible Doom kills. | N | `AfterDiedToDoom` |
| 43 | `BowlerHat` — Bowler Hat | Increases gold income. | N | `ModifyGoldGained; AfterModifyingGoldGained` |
| 44 | `Candelabra` — Candelabra | Second-turn energy. | N | `AfterSideTurnStart` |
| 45 | `EternalFeather` — Eternal Feather | Rest-site entry healing based on deck size. | N | `AfterRoomEntered` |
| 46 | `FuneraryMask` — Funerary Mask | Adds Souls to the combat draw pile. | N | `BeforeHandDraw` |
| 47 | `GalacticDust` — Galactic Dust | Block after spending a threshold of Stars. | N | `AfterStarsSpent` |
| 48 | `GoldPlatedCables` — Gold-Plated Cables | Additional passive trigger for the rightmost Orb. | N | `ModifyOrbPassiveTriggerCounts; AfterModifyingOrbPassiveTriggerCount` |
| 49 | `GremlinHorn` — Gremlin Horn | Energy and draw when an enemy dies. | N | `AfterDeath` |
| 50 | `HornCleat` — Horn Cleat | Second-turn block. | N | `AfterBlockCleared` |
| 51 | `JossPaper` — Joss Paper | Draw after enough cards are exhausted. | N | `AfterCardExhausted; AfterSideTurnEnd` |
| 52 | `Kusarigama` — Kusarigama | Random-target damage after enough attacks in a turn. | N | `BeforeCombatStart; AfterSideTurnEnd` |
| 53 | `LastingCandy` — Lasting Candy | Adds a Power option to alternating combat card rewards. | N | `TryModifyCardRewardOptions; BeforeCombatRewardOffered` |
| 54 | `LetterOpener` — Letter Opener | Area damage after enough Skills in a turn. | N | `BeforeCombatStart; AfterSideTurnStart` |
| 55 | `LuckyFysh` — Lucky Fysh | Gold for adding cards to the deck. | N | `AfterCardChangedPiles` |
| 56 | `MercuryHourglass` — Mercury Hourglass | Turn-start area damage. | N | `AfterPlayerTurnStart` |
| 57 | `MiniatureCannon` — Miniature Cannon | Extra damage from upgraded Attacks. | N | `ModifyDamageAdditive` |
| 58 | `Nunchaku` — Nunchaku | Energy after an attack-count milestone. | N | `AfterCardPlayed` |
| 59 | `Orichalcum` — Orichalcum | Block when the end-turn eligibility check found no block. | R: stacking/tradeoff [G-reference] | `BeforeSideTurnEndVeryEarly; BeforeSideTurnEnd` |
| 60 | `OrnamentalFan` — Ornamental Fan | Block after enough attacks in a turn. | N | `BeforeSideTurnStart; AfterCardPlayed` |
| 61 | `Pantograph` — Pantograph | Healing at the start of boss combat. | N | `AfterRoomEntered; BeforeCombatStart` |
| 62 | `PaperPhrog` — Paper Phrog | Improves Vulnerable's damage multiplier for the owner. | C: first copy only [G-singleton] | `ModifyVulnerableMultiplier` |
| 63 | `ParryingShield` — Parrying Shield | Random-target damage after ending with enough block. | N | `AfterSideTurnEnd` |
| 64 | `Pear` — Pear | Immediate max-HP increase. | N | `AfterObtained` |
| 65 | `PenNib` — Pen Nib | Double damage on an attack-count milestone. | N | `ModifyDamageMultiplicative; BeforeCardPlayed` |
| 66 | `Permafrost` — Permafrost | Block on the first Power played in combat. | N | `AfterRoomEntered; AfterCardPlayed` |
| 67 | `PetrifiedToad` — Petrified Toad | Produces a Potion-Shaped Rock at combat start. | N | `BeforeCombatStartLate` |
| 68 | `Planisphere` — Planisphere | Healing on unknown-room entry. | N | `AfterRoomEntered` |
| 69 | `Regalite` — Regalite | Block on the first card generation each turn. | N | `AfterCardGeneratedForCombat; BeforeSideTurnStart` |
| 70 | `ReptileTrinket` — Reptile Trinket | Temporary Strength after using a potion. | N | `AfterPotionUsed` |
| 71 | `RippleBasin` — Ripple Basin | Block after a turn with no attacks. | N | `BeforeSideTurnEnd; AfterCardPlayed` |
| 72 | `SelfFormingClay` — Self-Forming Clay | Next-turn block when HP is lost in combat. | N | `AfterDamageReceived` |
| 73 | `SparklingRouge` — Sparkling Rouge | Third-turn Strength and Dexterity. | N | `AfterBlockCleared` |
| 74 | `StoneCracker` — Stone Cracker | Random temporary opening upgrades in the draw pile. | N | `AfterRoomEntered` |
| 75 | `SymbioticVirus` — Symbiotic Virus | Opening Dark channel. | N | `AfterSideTurnStart` |
| 76 | `Tingsha` — Tingsha | Random-target damage for discarded cards. | N | `AfterCardDiscarded` |
| 77 | `TinyMailbox` — Tiny Mailbox | Potion rewards from resting. | N | `TryModifyRestSiteHealRewards; ModifyExtraRestSiteHealText` |
| 78 | `TuningFork` — Tuning Fork | Block after a Skill-count milestone. | N | `AfterCardPlayed` |
| 79 | `TwistedFunnel` — Twisted Funnel | Opening Poison on enemies. | N | `BeforeSideTurnStart` |
| 80 | `Vambrace` — Vambrace | Doubles the first qualifying card block gain in combat. | N | `BeforeCombatStart; ModifyBlockMultiplicative` |

### Rare — 50

| # | Native model / display name | Effect paraphrase | Review status | Source member(s) |
| ---: | --- | --- | --- | --- |
| 81 | `ArtOfWar` — Art of War | Extra next-turn energy after a turn without attacks. | N | `AfterCardPlayed; AfterSideTurnEnd` |
| 82 | `BeatingRemnant` — Beating Remnant | Limits HP lost during a turn. | C: same cap/threshold [G-threshold] | `ModifyHpLostAfterOsty; AfterModifyingHpLostAfterOsty` |
| 83 | `Bellows` — Bellows | Upgrades the initial combat hand. | C: repeated modification [G-upgrades] | `AfterPlayerTurnStart` |
| 84 | `BigHat` — Big Hat | Generates random Ethereal cards at combat start. | N | `AfterSideTurnStart` |
| 85 | `Bookmark` — Bookmark | Discounts a retained card at turn end. | N | `AfterFlush` |
| 86 | `CaptainsWheel` — Captain's Wheel | Third-turn block. | N | `AfterBlockCleared` |
| 87 | `Chandelier` — Chandelier | Third-turn energy. | N | `AfterSideTurnStart` |
| 88 | `CharonsAshes` — Charon's Ashes | Area damage when cards are exhausted. | N | `AfterCardExhausted` |
| 89 | `CloakClasp` — Cloak Clasp | End-turn block based on cards remaining in hand. | N | `BeforeSideTurnEnd` |
| 90 | `DemonTongue` — Demon Tongue | Refunds the first HP loss on the owner's turn as healing. | N | `AfterDamageReceived; BeforeSideTurnStart` |
| 91 | `EmotionChip` — Emotion Chip | Orb passive activation after HP was lost in the preceding turn. | N | `AfterDamageReceived; AfterPlayerTurnStart` |
| 92 | `FrozenEgg` — Frozen Egg | Upgrades acquired Powers. | C: repeated modification [G-upgrades] | `TryModifyCardRewardOptionsLate; ModifyMerchantCardCreationResults` |
| 93 | `GamblingChip` — Gambling Chip | Opening discard-and-redraw choice. | N | `AfterPlayerTurnStart` |
| 94 | `GamePiece` — Game Piece | Draw when a Power is played. | N | `AfterCardPlayed` |
| 95 | `Girya` — Girya | Rest-site lifting builds persistent starting Strength. | C: wrong Lift target [G-rest] | `TryModifyRestSiteOptions; TimesLifted` |
| 96 | `HelicalDart` — Helical Dart | Temporary Dexterity after playing a Shiv. | N | `AfterCardPlayed` |
| 97 | `IceCream` — Ice Cream | Preserves energy between turns. | C: redundant permission [G-idempotent] | `ShouldPlayerResetEnergy` |
| 98 | `IntimidatingHelmet` — Intimidating Helmet | Block for playing expensive cards. | N | `BeforeCardPlayed` |
| 99 | `IvoryTile` — Ivory Tile | Energy for playing sufficiently expensive cards. | N | `AfterCardPlayed` |
| 100 | `Kunai` — Kunai | Dexterity after enough attacks in a turn. | N | `BeforeSideTurnStart; AfterCardPlayed` |
| 101 | `LizardTail` — Lizard Tail | One-use death prevention and healing. | R: fresh uses/tradeoff [G-counters] | `ShouldDieLate; AfterPreventingDeath` |
| 102 | `LunarPastry` — Lunar Pastry | End-turn Stars. | N | `AfterSideTurnEnd` |
| 103 | `Mango` — Mango | Immediate max-HP increase. | N | `AfterObtained` |
| 104 | `MeatOnTheBone` — Meat on the Bone | Post-victory healing when HP is sufficiently low. | N | `BeforeCombatStart; AfterCurrentHpChanged` |
| 105 | `Metronome` — Metronome | Area damage at the first Orb-channel milestone in combat. | N | `AfterRoomEntered; AfterOrbChanneled` |
| 106 | `MiniRegent` — Mini Regent | Strength on the first Star expenditure each turn. | N | `AfterStarsSpent; BeforeSideTurnStart` |
| 107 | `MoltenEgg` — Molten Egg | Upgrades acquired Attacks. | C: repeated modification [G-upgrades] | `TryModifyCardRewardOptionsLate; ModifyMerchantCardCreationResults` |
| 108 | `MummifiedHand` — Mummified Hand | A random hand card becomes free for the turn after playing a Power. | N | `AfterCardPlayed` |
| 109 | `OldCoin` — Old Coin | Immediate gold. | N | `AfterObtained` |
| 110 | `OrangeDough` — Orange Dough | Generates opening colorless cards. | N | `AfterSideTurnStart` |
| 111 | `PaperKrane` — Paper Krane | Improves Weak's damage reduction against the owner. | C: first copy only [G-singleton] | `ModifyWeakMultiplier` |
| 112 | `Pocketwatch` — Pocketwatch | Extra next-turn draw after a low-card-count turn. | N | `AfterCardPlayed; ModifyHandDraw` |
| 113 | `PowerCell` — Power Cell | Moves zero-cost draw-pile cards into the opening hand. | N | `BeforeSideTurnStart` |
| 114 | `PrayerWheel` — Prayer Wheel | Extra card reward from ordinary enemies. | N | `TryModifyRewards` |
| 115 | `RainbowRing` — Rainbow Ring | Strength and Dexterity for playing each principal card type in a turn. | N | `BeforeSideTurnStart; AfterCardPlayed` |
| 116 | `RazorTooth` — Razor Tooth | Upgrades played Attacks and Skills for combat. | C: repeated modification [G-upgrades] | `AfterCardPlayed` |
| 117 | `RuinedHelmet` — Ruined Helmet | Doubles the first Strength gain of a combat. | N | `TryModifyPowerAmountReceived; AfterModifyingPowerAmountReceived` |
| 118 | `Shovel` — Shovel | Unlocks Dig at rest sites. | C: repeated menu option [G-rest] | `TryModifyRestSiteOptions` |
| 119 | `Shuriken` — Shuriken | Strength after enough attacks in a turn. | N | `BeforeSideTurnStart; AfterCardPlayed` |
| 120 | `StoneCalendar` — Stone Calendar | Area damage after reaching a specified combat turn. | N | `AfterSideTurnStart; BeforeSideTurnEnd` |
| 121 | `SturdyClamp` — Sturdy Clamp | Keeps a fixed maximum amount of block between turns. | C: same cap/threshold [G-threshold] | `ShouldClearBlock; AfterPreventingBlockClear` |
| 122 | `TheCourier` — The Courier | Merchant restocking and a price discount. | R: cumulative discount [G-shop] | `ModifyMerchantPrice; ShouldRefillMerchantEntry` |
| 123 | `ToughBandages` — Tough Bandages | Block for discarded cards. | N | `AfterCardDiscarded` |
| 124 | `ToxicEgg` — Toxic Egg | Upgrades acquired Skills. | C: repeated modification [G-upgrades] | `TryModifyCardRewardOptionsLate; ModifyMerchantCardCreationResults` |
| 125 | `TungstenRod` — Tungsten Rod | Reduces individual HP-loss amounts. | N | `ModifyHpLostAfterOsty; AfterModifyingHpLostAfterOsty` |
| 126 | `UnceasingTop` — Unceasing Top | Draw after the hand-empty event in eligible turn phases. | R: stacking/tradeoff [G-reference] | `AfterHandEmptied; IsValidPhase` |
| 127 | `UnsettlingLamp` — Unsettling Lamp | Doubles an eligible card's first debuff application sequence each combat. | N | `BeforeCombatStart; BeforePowerAmountChanged` |
| 128 | `VexingPuzzlebox` — Vexing Puzzlebox | Opening random card discounted for that turn. | N | `AfterPlayerTurnStart` |
| 129 | `WhiteBeastStatue` — White Beast Statue | Guarantees potion rewards from combats. | C: redundant permission [G-idempotent] | `ShouldForcePotionReward` |
| 130 | `WhiteStar` — White Star | Extra rare-card reward from elites. | C: remaining-route value [G-opportunity] | `TryModifyRewards` |

### Shop — 30

| # | Native model / display name | Effect paraphrase | Review status | Source member(s) |
| ---: | --- | --- | --- | --- |
| 131 | `BeltBuckle` — Belt Buckle | Bonus Dexterity while the potion belt is empty. | N | `AfterObtained; BeforeCombatStart` |
| 132 | `Bread` — Bread | Reduces opening energy in exchange for later-turn energy. | N | `ModifyMaxEnergy; AfterSideTurnStart` |
| 133 | `Brimstone` — Brimstone | Gives Strength to the owner and enemies each turn. | R: stacking/tradeoff [G-reference] | `AfterSideTurnStart` |
| 134 | `BurningSticks` — Burning Sticks | Copies the first exhausted Skill each combat into hand. | N | `AfterRoomEntered; AfterCardExhausted` |
| 135 | `Cauldron` — Cauldron | Offers random potion rewards on pickup. | N | `AfterObtained; GenerateRewards` |
| 136 | `ChemicalX` — Chemical X | Increases X-card effects. | N | `BeforeCardPlayed; ModifyXValue` |
| 137 | `DingyRug` — Dingy Rug | Adds colorless cards to the eligible reward pools. | C: redundant permission [G-idempotent] | `ModifyCardRewardCreationOptions` |
| 138 | `DollysMirror` — Dolly's Mirror | Copies a selected deck card. | R: repeat acquisition [G-reference] | `AfterObtained; Filter` |
| 139 | `DragonFruit` — Dragon Fruit | Max-HP growth when gold is gained. | N | `AfterGoldGained` |
| 140 | `GhostSeed` — Ghost Seed | Gives basic Strikes and Defends Ethereal. | C: repeated modification [G-upgrades] | `AfterCardEnteredCombat; AfterRoomEntered` |
| 141 | `GnarledHammer` — Gnarled Hammer | Enchants selected Attacks with Sharp. | N | `AfterObtained` |
| 142 | `Kifuda` — Kifuda | Enchants selected cards with Adroit. | N | `AfterObtained` |
| 143 | `LavaLamp` — Lava Lamp | Upgrades combat card rewards after avoiding qualifying damage. | C: repeated modification [G-upgrades] | `AfterRoomEntered; AfterDamageReceived` |
| 144 | `LeesWaffle` — Lee's Waffle | Raises max HP and fully heals. | N | `AfterObtained` |
| 145 | `MembershipCard` — Membership Card | Merchant price discount. | R: cumulative discount [G-shop] | `ModifyMerchantPrice` |
| 146 | `MiniatureTent` — Miniature Tent | Allows further rest-site choices after taking an option. | C: redundant permission [G-idempotent] | `ShouldDisableRemainingRestSiteOptions` |
| 147 | `MysticLighter` — Mystic Lighter | Extra damage from enchanted Attacks. | N | `ModifyDamageAdditive` |
| 148 | `NinjaScroll` — Ninja Scroll | Opening Shivs. | N | `BeforeHandDraw` |
| 149 | `Orrery` — Orrery | Multiple card rewards on pickup. | R: repeat acquisition [G-reference] | `AfterObtained` |
| 150 | `PunchDagger` — Punch Dagger | Enchants an Attack with Momentum. | N | `AfterObtained` |
| 151 | `RingingTriangle` — Ringing Triangle | Keeps the first-turn hand instead of discarding it. | C: redundant permission [G-idempotent] | `ShouldFlush` |
| 152 | `RoyalStamp` — Royal Stamp | Adds Royally Approved to a selected Attack or Skill. | N | `AfterObtained` |
| 153 | `RunicCapacitor` — Runic Capacitor | Additional combat Orb slots. | N | `AfterSideTurnStart` |
| 154 | `ScreamingFlagon` — Screaming Flagon | Area damage for ending with an empty hand. | N | `BeforeSideTurnEnd` |
| 155 | `SlingOfCourage` — Sling of Courage | Opening Strength against elites. | C: remaining-route value [G-opportunity] | `AfterRoomEntered` |
| 156 | `TheAbacus` — The Abacus | Block when the draw pile is shuffled. | N | `AfterShuffle` |
| 157 | `Toolbox` — Toolbox | Opening choice of a colorless card. | N | `BeforeHandDraw` |
| 158 | `UndyingSigil` — Undying Sigil | Reduces damage from enemies whose Doom meets their HP. | N | `ModifyDamageMultiplicative` |
| 159 | `VitruvianMinion` — Vitruvian Minion | Multiplies damage and block of Minion-named cards. | N | `ModifyDamageMultiplicative; ModifyBlockMultiplicative` |
| 160 | `WingCharm` — Wing Charm | Adds Swift to a random eligible reward card. | N | `TryModifyCardRewardOptionsLate` |

### Event — 35

| # | Native model / display name | Effect paraphrase | Review status | Source member(s) |
| ---: | --- | --- | --- | --- |
| 161 | `BigMushroom` — Big Mushroom | Raises max HP; reduces opening draw. | N | `AfterObtained; AfterRoomEntered` |
| 162 | `BingBong` — Bing Bong | Adds an extra copy when a non-cloned card enters the deck. | R: stacking/tradeoff [G-reference] | `AfterCardChangedPiles` |
| 163 | `BoneTea` — Bone Tea | Limited-use upgrade of an opening hand. | C: repeated modification [G-upgrades] | `AfterPlayerTurnStart` |
| 164 | `Byrdpip` — Byrdpip | Hatches existing Byrdonis Eggs into Byrd Swoop; summons a companion. | C: pet paths unproven [G-pets] | `AfterObtained; SummonPet` |
| 165 | `ChosenCheese` — The Chosen Cheese | Post-combat max-HP growth. | N | `AfterCombatEnd` |
| 166 | `DarkstonePeriapt` — Darkstone Periapt | Max-HP gain when a curse enters the deck. | N | `AfterCardChangedPiles` |
| 167 | `DaughterOfTheWind` — Daughter of the Wind | Block after attacks. | N | `AfterCardPlayed` |
| 168 | `DreamCatcher` — Dream Catcher | Card reward from resting. | N | `TryModifyRestSiteHealRewards; ModifyExtraRestSiteHealText` |
| 169 | `EmberTea` — Ember Tea | Limited-use opening Strength. | R: fresh uses/tradeoff [G-counters] | `AfterRoomEntered` |
| 170 | `FakeAnchor` — Anchor??? | Counterfeit opening block. | N | `BeforeCombatStart` |
| 171 | `FakeBloodVial` — Blood Vial??? | Counterfeit opening healing. | N | `AfterPlayerTurnStartLate` |
| 172 | `FakeHappyFlower` — Happy Flower??? | Counterfeit periodic energy. | N | `AfterSideTurnStart; AfterCombatEnd` |
| 173 | `FakeLeesWaffle` — Lee's Waffle??? | Counterfeit percentage healing on pickup. | N | `AfterObtained` |
| 174 | `FakeMango` — Mango??? | Counterfeit max-HP increase. | N | `AfterObtained` |
| 175 | `FakeMerchantsRug` — The Merchant's Rug??? | Intentionally nonfunctional counterfeit rug. | C: inert/penalty-only [G-inert] | `Rarity` |
| 176 | `FakeOrichalcum` — Orichalcum??? | Counterfeit block after an initially blockless end turn. | R: stacking/tradeoff [G-reference] | `BeforeSideTurnEndVeryEarly; BeforeSideTurnEnd` |
| 177 | `FakeSneckoEye` — Snecko Eye??? | Opening Confused, without Snecko Eye's extra draw. | C: inert/penalty-only [G-inert] | `AfterObtained; BeforeCombatStart` |
| 178 | `FakeStrikeDummy` — Strike Dummy??? | Counterfeit bonus Strike damage. | N | `ModifyDamageAdditive` |
| 179 | `FakeVenerableTeaSet` — Venerable Tea Set??? | Counterfeit next-combat energy after entering a rest site. | N | `AfterRoomEntered; AfterEnergyReset` |
| 180 | `ForgottenSoul` — Forgotten Soul | Random-target damage when a card is exhausted. | N | `AfterCardExhausted` |
| 181 | `FragrantMushroom` — Fragrant Mushroom | HP loss in exchange for random deck upgrades. | R: normal costly effect [G-costs] | `AfterObtained` |
| 182 | `FresnelLens` — Fresnel Lens | Adds Nimble to eligible block-gaining card acquisitions. | C: repeated modification [G-upgrades] | `TryModifyCardRewardOptionsLate; ModifyMerchantCardCreationResults` |
| 183 | `HandDrill` — Hand Drill | Applies Vulnerable when enemy block is broken. | N | `AfterBlockBroken` |
| 184 | `HistoryCourse` — History Course | Autoplays a duplicate of the previous turn's last eligible Attack. | N | `AfterAutoPrePlayPhaseEntered` |
| 185 | `LostWisp` — Lost Wisp | Area damage when a Power is played. | N | `AfterCardPlayed` |
| 186 | `MawBank` — Maw Bank | Gold on room progression until a paid shop purchase. | R: fresh uses/tradeoff [G-counters] | `AfterRoomEntered; AfterItemPurchased` |
| 187 | `MrStruggles` — Mr. Struggles | Turn-start area damage increasing with turn number. | N | `AfterPlayerTurnStart` |
| 188 | `PollinousCore` — Pollinous Core | Periodic additional draw. | N | `BeforeHandDraw; AfterCombatEnd` |
| 189 | `RoyalPoison` — Royal Poison | HP damage at each combat start. | C: inert/penalty-only [G-inert] | `AfterPlayerTurnStart` |
| 190 | `SwordOfJade` — Sword of Jade | Opening Strength. | N | `AfterRoomEntered` |
| 191 | `SwordOfStone` — Sword of Stone | Becomes Sword of Jade after five elite victories. | C: remaining-route value [G-opportunity] | `AfterCombatVictory` |
| 192 | `TeaOfDiscourtesy` — Tea of Discourtesy | Adds Dazed at the next combat start. | C: inert/penalty-only [G-inert] | `BeforeCombatStart` |
| 193 | `TheBoot` — The Boot | Raises small positive attack HP damage to a fixed floor. | C: same cap/threshold [G-threshold] | `ModifyHpLostAfterOstyLate; AfterModifyingHpLostAfterOsty` |
| 194 | `WongoCustomerAppreciationBadge` — Wongo Customer Appreciation Badge | Intentionally nonfunctional event souvenir. | C: inert/penalty-only [G-inert] | `Rarity` |
| 195 | `WongosMysteryTicket` — Wongo's Mystery Ticket | Relic rewards after five combats. | C: remaining-route value [G-opportunity] | `AfterCombatEnd; TryModifyRewards` |

### Ancient — 97

| # | Native model / display name | Effect paraphrase | Review status | Source member(s) |
| ---: | --- | --- | --- | --- |
| 196 | `AlchemicalCoffer` — Alchemical Coffer | Adds potion slots and fills the new capacity with potions. | N | `AfterObtained` |
| 197 | `ArcaneScroll` — Arcane Scroll | Grants a random rare card. | N | `AfterObtained` |
| 198 | `ArchaicTooth` — Archaic Tooth | Transforms a character's special starter card into its Ancient counterpart. | C: acquisition failure [G-setup] | `AfterObtained; GetTranscendenceTransformedCard` |
| 199 | `Astrolabe` — Astrolabe | Transforms selected deck cards and upgrades the results. | R: repeat acquisition [G-reference] | `AfterObtained` |
| 200 | `BeautifulBracelet` — Beautiful Bracelet | Random deck cards gain Swift. | N | `AfterObtained` |
| 201 | `BiiigHug` — Biiig Hug | Removes deck cards; shuffling also creates Soot. | N | `AfterObtained; AfterShuffle` |
| 202 | `BlackStar` — Black Star | Extra relic from elite rewards. | C: remaining-route value [G-opportunity] | `TryModifyRewards` |
| 203 | `BlessedAntler` — Blessed Antler | Extra energy; opening Dazed cards. | N | `ModifyMaxEnergy; BeforeHandDraw` |
| 204 | `BloodSoakedRose` — Blood-Soaked Rose | Extra energy; adds Enthralled to the deck. | N | `AfterObtained; ModifyMaxEnergy` |
| 205 | `BoomingConch` — Booming Conch | Extra opening draw and energy against elites. | C: remaining-route value [G-opportunity] | `ModifyHandDraw; AfterSideTurnStart` |
| 206 | `BrilliantScarf` — Brilliant Scarf | Makes the fifth manually played card of a turn free. | C: same cap/threshold [G-threshold] | `TryModifyEnergyCostInCombatLate; TryModifyStarCost` |
| 207 | `CallingBell` — Calling Bell | Grants relic rewards and a bell curse. | R: repeat acquisition [G-reference] | `AfterObtained; GenerateRewards` |
| 208 | `ChoicesParadox` — Choices Paradox | Choose a generated card with Retain at combat start. | N | `AfterPlayerTurnStart` |
| 209 | `Claws` — Claws | Transforms selected cards into Maul. | N | `AfterObtained; CreateMaulFromOriginal` |
| 210 | `Crossbow` — Crossbow | Creates a temporary free random Attack each turn. | N | `AfterSideTurnStart` |
| 211 | `CursedPearl` — Cursed Pearl | Grants gold and a Greed curse. | N | `AfterObtained` |
| 212 | `DelicateFrond` — Delicate Frond | Fills empty potion slots at combat start. | C: shared potion capacity [G-reference] | `BeforeCombatStart` |
| 213 | `DiamondDiadem` — Diamond Diadem | Opening block with protection from the next block clear. | N | `AfterSideTurnStart` |
| 214 | `DistinguishedCape` — Distinguished Cape | Adds Apparitions and random curses. | N | `AfterObtained` |
| 215 | `DowsingRod` — Dowsing Rod | Adds Dowsing to the deck. | N | `AfterObtained` |
| 216 | `Driftwood` — Driftwood | Enables one reroll on each card reward. | C: redundant permission [G-idempotent] | `TryModifyRewardsLate` |
| 217 | `DustyTome` — Dusty Tome | Grants an upgraded Ancient card selected during event setup. | C: acquisition failure [G-setup] | `SetupForPlayer; AfterObtained` |
| 218 | `Ectoplasm` — Ectoplasm | Extra energy; prevents gold income. | R: stacking/tradeoff [G-reference] | `ModifyGoldGained; AfterModifyingGoldGained` |
| 219 | `ElectricShrymp` — Electric Shrymp | Enchants a selected Skill with Imbued. | N | `AfterObtained` |
| 220 | `EmptyCage` — Empty Cage | Removes selected deck cards. | R: repeat acquisition [G-reference] | `AfterObtained` |
| 221 | `Fiddle` — Fiddle | Additional turn-start draw; prevents other drawing. | R: stacking/tradeoff [G-reference] | `ModifyHandDraw; ShouldDraw` |
| 222 | `FishingRod` — Fishing Rod | Random deck upgrades after ordinary-combat milestones. | C: remaining-route value [G-opportunity] | `AfterCombatEnd` |
| 223 | `GlassEye` — Glass Eye | Grants a mix of common, uncommon and rare cards. | N | `AfterObtained` |
| 224 | `Glitter` — Glitter | Adds Glam to eligible card rewards. | C: repeated modification [G-upgrades] | `TryModifyCardRewardOptionsLate` |
| 225 | `GoldenPearl` — Golden Pearl | Immediate gold. | N | `AfterObtained` |
| 226 | `HeftyTablet` — Hefty Tablet | Rare-card choice plus an Injury. | N | `AfterObtained` |
| 227 | `IronClub` — Iron Club | Periodic draw based on cards played. | N | `AfterCardPlayed` |
| 228 | `JeweledMask` — Jeweled Mask | Moves a Power into the opening hand and discounts it for combat. | N | `BeforeHandDraw` |
| 229 | `JewelryBox` — Jewelry Box | Adds Apotheosis. | N | `AfterObtained` |
| 230 | `Kaleidoscope` — Kaleidoscope | Grants card rewards drawn from other characters. | N | `AfterObtained` |
| 231 | `LargeCapsule` — Large Capsule | Grants relics and adds a basic Strike and Defend. | R: repeat acquisition [G-reference] | `AfterObtained; GetStrikeForCharacter` |
| 232 | `LavaRock` — Lava Rock | Extra relic rewards specifically from the Act 1 boss. | C: remaining-route value [G-opportunity] | `TryModifyRewards` |
| 233 | `LeadPaperweight` — Lead Paperweight | Choice between colorless cards on pickup. | N | `AfterObtained` |
| 234 | `LeafyPoultice` — Leafy Poultice | Max-HP cost; transforms an available basic Strike and Defend. | C: exhausted targets [G-targets] | `AfterObtained` |
| 235 | `LoomingFruit` — Looming Fruit | Immediate max-HP increase. | N | `AfterObtained` |
| 236 | `LostCoffer` — Lost Coffer | Card reward plus a potion. | N | `AfterObtained` |
| 237 | `MassiveScroll` — Massive Scroll | Choice of multiplayer-only cards; native multiplayer-only spawn. | R: multiplayer only [G-multi] | `AfterObtained` |
| 238 | `MeatCleaver` — Meat Cleaver | Unlocks the Cook rest-site option. | C: repeated menu option [G-rest] | `TryModifyRestSiteOptions` |
| 239 | `MusicBox` — Music Box | Creates an Ethereal copy of the first Attack played each turn. | N | `BeforeCardPlayed; AfterCardPlayed` |
| 240 | `NeowsBones` — Neow's Bones | Grants random eligible Neow relic rewards and curses. | R: repeat acquisition [G-reference] | `AfterObtained` |
| 241 | `NeowsSacrifice` — Neow's Sacrifice | Grants Ambergris and adds Guilty. | N | `AfterObtained` |
| 242 | `NeowsTalisman` — Neow's Talisman | Upgrades a matching basic Strike and Defend. | C: exhausted targets [G-targets] | `AfterObtained` |
| 243 | `NeowsTorment` — Neow's Torment | Adds Neow's Fury. | N | `AfterObtained` |
| 244 | `NewLeaf` — New Leaf | Transforms a selected deck card. | N | `AfterObtained` |
| 245 | `NutritiousOyster` — Nutritious Oyster | Immediate max-HP increase. | N | `AfterObtained` |
| 246 | `NutritiousSoup` — Nutritious Soup | Enchants eligible basic Strikes with Tezcatara's Ember. | C: exhausted targets [G-targets] | `AfterObtained` |
| 247 | `PaelsBlood` — Pael's Blood | Additional turn-start draw. | N | `ModifyHandDraw` |
| 248 | `PaelsClaw` — Pael's Claw | Enchants eligible Defends with Goopy. | C: exhausted targets [G-targets] | `AfterObtained` |
| 249 | `PaelsFlesh` — Pael's Flesh | Additional energy starting on the third turn. | N | `ModifyMaxEnergy; BeforeCombatStart` |
| 250 | `PaelsGrowth` — Pael's Growth | Pickup Clone enchantment and a Clone rest-site option. | C: repeated menu option [G-rest] | `AfterObtained; TryModifyRestSiteOptions` |
| 251 | `PaelsHorn` — Pael's Horn | Adds Relax cards. | N | `AfterObtained` |
| 252 | `PaelsLegion` — Pael's Legion | Pet-supported block doubling with a cooldown. | C: pet paths unproven [G-pets] | `SummonPet; ModifyBlockMultiplicative` |
| 253 | `PaelsTears` — Pael's Tears | Rewards unspent end-turn energy with next-turn energy. | N | `BeforeSideTurnEnd; AfterSideTurnStart` |
| 254 | `PaelsTooth` — Pael's Tooth | Removes cards, then returns them upgraded after combats. | N | `AfterObtained; AfterCombatEnd` |
| 255 | `PaelsWing` — Pael's Wing | Sacrifices card rewards toward relic rewards. | C: repeated menu option [G-rest] | `TryModifyCardRewardAlternatives; OnSacrifice` |
| 256 | `PandorasBox` — Pandora's Box | Transforms remaining removable basic Strikes and Defends. | C: exhausted targets [G-targets] | `AfterObtained` |
| 257 | `PhialHolster` — Phial Holster | Adds potion capacity and offers potions. | N | `AfterObtained` |
| 258 | `PhilosophersStone` — Philosopher's Stone | Extra energy; enemies gain starting Strength. | R: stacking/tradeoff [G-reference] | `ModifyMaxEnergy; AfterCreatureAddedToCombat` |
| 259 | `Pomander` — Pomander | Upgrades a selected card. | N | `AfterObtained` |
| 260 | `PrecariousShears` — Precarious Shears | Removes deck cards, then deals HP damage to the owner. | R: normal costly effect [G-costs] | `AfterObtained` |
| 261 | `PreciseScissors` — Precise Scissors | Removes selected deck cards. | N | `AfterObtained` |
| 262 | `PreservedFog` — Preserved Fog | Removes selected cards and adds Folly. | N | `AfterObtained` |
| 263 | `PrismaticGem` — Prismatic Gem | Extra energy; broadens card rewards to other colors. | R: stacking/tradeoff [G-reference] | `ModifyMaxEnergy; ModifyCardRewardCreationOptions` |
| 264 | `PumpkinCandle` — Pumpkin Candle | Energy for a limited number of combats; Kindle replenishes duration. | C: shared state [G-shared] | `AfterObtained; Rekindle` |
| 265 | `RadiantPearl` — Radiant Pearl | Opening Luminesce cards. | N | `BeforeHandDraw` |
| 266 | `RunicPyramid` — Runic Pyramid | Keeps the hand at turn end. | C: redundant permission [G-idempotent] | `ShouldFlush` |
| 267 | `Sai` — Sai | Turn-start block. | N | `AfterSideTurnStart` |
| 268 | `SandCastle` — Sand Castle | Random permanent deck upgrades. | N | `AfterObtained` |
| 269 | `ScrollBoxes` — Scroll Boxes | Choose a bundle of cards on pickup. | N | `AfterObtained; CanGenerateBundles` |
| 270 | `SeaGlass` — Sea Glass | Choose any number from a prepared character's card selection. | C: lost character choice [G-setup] | `AfterObtained; CharacterId` |
| 271 | `SealOfGold` — Seal of Gold | Spends gold each turn for energy. | R: stacking/tradeoff [G-reference] | `AfterSideTurnStart` |
| 272 | `SereTalon` — Sere Talon | Loses max HP and adds Wishes. | R: normal costly effect [G-costs] | `AfterObtained` |
| 273 | `SignetRing` — Signet Ring | Immediate gold. | N | `AfterObtained` |
| 274 | `SilkenTress` — Silken Tress | Loses all current gold; enchants the next card reward with Glam. | R: fresh uses/tradeoff [G-counters] | `AfterObtained; TryModifyCardRewardOptionsLate` |
| 275 | `SilverCrucible` — Silver Crucible | Upgrades initial card rewards but suppresses the first chest. | R: fresh uses/tradeoff [G-counters] | `TryModifyCardRewardOptionsLate; AfterModifyingCardRewardOptions` |
| 276 | `SmallCapsule` — Small Capsule | Random relic reward. | R: repeat acquisition [G-reference] | `AfterObtained` |
| 277 | `SneckoEye` — Snecko Eye | Additional draw and opening Confused. | R: stacking/tradeoff [G-reference] | `AfterObtained; BeforeCombatStart` |
| 278 | `Sozu` — Sozu | Extra energy; prevents potion acquisition. | R: stacking/tradeoff [G-reference] | `ShouldProcurePotion; ModifyMaxEnergy` |
| 279 | `SpikedGauntlets` — Spiked Gauntlets | Extra energy; Powers become more expensive. | R: stacking/tradeoff [G-reference] | `ModifyMaxEnergy; TryModifyEnergyCostInCombat` |
| 280 | `StoneHumidifier` — Stone Humidifier | Max-HP gain from resting. | N | `AfterRestSiteHeal; ModifyExtraRestSiteHealText` |
| 281 | `Storybook` — Storybook | Adds Brightest Flame, a resource card with a max-HP cost. | R: normal costly effect [G-costs] | `AfterObtained` |
| 282 | `TanxsWhistle` — Tanx's Whistle | Adds Whistle. | N | `AfterObtained` |
| 283 | `ThrowingAxe` — Throwing Axe | An additional play of the first combat card. | N | `AfterRoomEntered; ModifyCardPlayCount` |
| 284 | `ToastyMittens` — Toasty Mittens | Exhausts a hand card each turn and grants Strength. | R: stacking/tradeoff [G-reference] | `AfterPlayerTurnStart` |
| 285 | `ToyBox` — Toy Box | Provides wax relics and melts the leftmost surviving wax relic periodically. | C: shared state [G-shared] | `AfterObtained; AfterCombatEnd` |
| 286 | `TriBoomerang` — Tri-Boomerang | Enchants selected Attacks with Instinct. | N | `AfterObtained` |
| 287 | `VelvetChoker` — Velvet Choker | Extra energy; limits cards played in a turn. | R: stacking/tradeoff [G-reference] | `ModifyMaxEnergy; ShouldPlay` |
| 288 | `VeryHotCocoa` — Very Hot Cocoa | Opening energy. | N | `AfterSideTurnStart` |
| 289 | `WarHammer` — War Hammer | Random deck upgrades after elite victories. | C: remaining-route value [G-opportunity] | `AfterCombatVictory` |
| 290 | `WhisperingEarring` — Whispering Earring | Extra energy; automatically plays cards on the first turn. | C: repeated autoplay [G-reference] | `AfterAutoPrePlayPhaseEnteredLate` |
| 291 | `WingedBoots` — Winged Boots | Limited off-path map travel; native single-player-only spawn. | C: shared state [G-shared] | `ShouldAllowFreeTravel; AfterRoomEntered` |
| 292 | `YummyCookie` — Yummy Cookie | Upgrades selected deck cards. | N | `AfterObtained` |

### None — 2 (fallback / legacy)

| # | Native model / display name | Effect paraphrase | Review status | Source member(s) |
| ---: | --- | --- | --- | --- |
| 293 | `Circlet` — Circlet | Fallback collectible; no functional effect. | C: inert/penalty-only [G-inert] | `Rarity` |
| 294 | `DeprecatedRelic` — Deprecated Relic | Legacy representation of removed relic content. | C: inert/penalty-only [G-inert] | `Rarity` |


## Reproducible method and coverage

1. Fingerprint `sts2.dll` and read the adjacent installation's
   `release_info.json`. Load that assembly, **not the mod assembly**.
2. Enumerate `typeof(RelicModel).Assembly.GetTypes()` and keep
   `typeof(RelicModel).IsAssignableFrom(t) && !t.IsAbstract`, ordered by full
   name. This independently yielded **299**, including non-sealed concrete
   models. Assembly metadata/MVID was captured in the same process.
3. Read-only decompile with **ILSpy command line 9.1.0.7988**, using .NET SDK
   **9.0.318**, into session research artifacts. The relic namespace contains
   300 class files: the 299 models plus `VakuuCardSelector`. Check exact set
   equality with reflection, not merely equal counts.
4. Cross-check all native `Models.RelicPools.*.GenerateAllRelics` lists:
   their union contains **all 299** reflected models; there are no extra
   unaccounted-for reflected relics. Pool slots are not added together because
   a few models appear in more than one pool. `FallbackRelicPool` and
   `DeprecatedRelicPool` explain the two `Rarity.None` entries.
5. Read the Godot **PCK v3** directory: header file-base and directory offsets
   at byte offsets 24 and 32, then the directory's file count and entries.
   Extract only `localization/eng/relics.json` at its file-base-relative offset.
   Match every model to its `UPPER_SNAKE_CASE.title` and `.description` keys.
   **299/299 have both**; every row was screened using this text and the
   implementation member/risk-signal index, rather than a web relic list.
6. Inspect bodies for acquisition/setup, singleton lookups, modifiers versus
   vetoes, fixed/set effects, counters, map/act restrictions, options/pets,
   costs and card-generating behavior. Search cross-assembly `GetRelic<T>`,
   `GetRelicById`, pet getters and relevant consumers. Detailed chains were
   traced for candidates and reference cases above, **not all combinations
   of all 299 relics**.
7. Run isolated native method probes on a synthetic owner, empty deck and
   `NullRunState`, using no save data: missing-state Dusty Tome, targetless
   Archaic Tooth, two Giryas, first-copy paper lookup, and Sea Glass's fresh
   default. These bypass `RelicCmd.Obtain`'s UI/history/grab-bag path. Thus the
   exception and wrong-counter results are real **method-level executions**,
   while the full Mirror transaction, combat, save/resume and multiplayer
   remain untested.
8. Verify the finished catalog has exactly **294 unique remaining IDs**, no
   approved-excluded IDs, and no unknown native IDs, with category counts
   matching the table above. No production code, existing blocklist, tests,
   game files or live saves were changed for this research.

Local reproducibility artifacts are retained outside the repository at
`/home/felipe/.copilot/session-state/4554c08f-0100-4944-abb9-696b005d62cd/files/relic-research/`:
`Inventory.cs` / `Inventory.csproj`, `inventory-reflection.json`,
`inventory-static.json`, `extract_inventory.py`, `pack-index.json`,
`native-relic-localization.json`, `probe-results.txt`, and the read-only
decompilation. They are **local inspection artifacts**, not redistributed
game source. The repository document intentionally contains paraphrases and
member citations, not native source bodies.

## Source index

Primary-source references below identify the installed binary and exact
types/members rather than linking an unverifiable online list. Member names
are source navigation anchors in the ILSpy view for the fingerprinted build.

### G — installed game

`/home/felipe/.local/share/Steam/steamapps/common/Slay the Spire 2/`:
`release_info.json`; `data_sts2_linuxbsd_x86_64/sts2.dll`; and
`SlayTheSpire2.pck`. Assembly identity and SHA-256 are recorded above.
Unless qualified otherwise below, relic class names are under
`MegaCrit.Sts2.Core.Models.Relics`.
Other abbreviated namespace paths (`Models.*`, `Entities.*`, `Commands.*`,
`Hooks.*`, `Helpers.*`) are relative to `MegaCrit.Sts2.Core`.

### L — native localization

`SlayTheSpire2.pck` → `localization/eng/relics.json`, each catalog class's
`UPPER_SNAKE_CASE.title` / `.description`; native `RelicModel.Title` and
`DynamicDescription` establish the table/key convention. The table uses
paraphrases, omits most numerical tuning, and does not imply the localized
description exhaustively explains the implementation. Sea Glass has additional
character-specific title keys handled by its `Title` getter.

### G-inventory

`MegaCrit.Sts2.Core.Models.RelicModel`;
`MegaCrit.Sts2.Core.Entities.Relics.RelicRarity`;
all concrete `Models.Relics.*.Rarity` getters;
`Models.RelicPools.{Shared,Ironclad,Silent,Defect,Necrobinder,Regent,Event,Fallback,Deprecated}RelicPool.GenerateAllRelics`;
`DeprecatedRelic`, `Circlet`, `VakuuCardSelector`.

### G-acquire

`MegaCrit.Sts2.Core.Commands.RelicCmd.{Obtain,Remove,Replace,Melt}`;
`MegaCrit.Sts2.Core.Entities.Players.Player.{AddRelicInternal,GetRelic,GetRelicById}`;
`Models.AbstractModel.MutableClone`;
`Models.RelicModel.{ToMutable,IsStackable,IsUsedUp,HasUponPickupEffect}`.

### G-hooks

`MegaCrit.Sts2.Core.Runs.RunState.IterateHookListeners`;
`MegaCrit.Sts2.Core.Combat.CombatState.IterateHookListeners`;
`MegaCrit.Sts2.Core.Hooks.Hook.{ModifyMerchantPrice,ModifyRestSiteOptions,ModifyCardRewardAlternatives,ShouldDie,ShouldClearBlock,ShouldAllowFreeTravel}`.

### G-setup

`DustyTome.{SetupForPlayer,AncientCard,AfterObtained}`;
`ArchaicTooth.{SetupForPlayer,GetTranscendenceStarterCard,GetTranscendenceTransformedCard,AfterObtained}`;
`SeaGlass.{CharacterId,Title,AfterObtained}`;
`Models.Events.Orobas.{SeaGlassOptions,AllPossibleOptions}`;
`Models.Events.Darv.GenerateInitialOptions`;
`Models.ModelDb.{GetById,GetByIdOrNull}`.

### G-singleton

`Models.Powers.WeakPower.ModifyDamageMultiplicative`;
`Models.Powers.VulnerablePower.ModifyDamageMultiplicative`;
`PaperKrane.ModifyWeakMultiplier`; `PaperPhrog.ModifyVulnerableMultiplier`;
`Entities.Players.Player.GetRelic<T>`.

### G-rest

`Girya.{TimesLifted,AfterRoomEntered,TryModifyRestSiteOptions}`;
`PumpkinCandle.{AfterObtained,TryModifyRestSiteOptions,Rekindle}`;
`{Shovel,MeatCleaver,PaelsGrowth}.TryModifyRestSiteOptions`;
`PaelsGrowth.AfterObtained`;
`PaelsWing.{TryModifyCardRewardAlternatives,OnSacrifice}`;
`Entities.RestSite.RestSiteOption.{Generate,Equals}`;
`Entities.RestSite.{LiftRestSiteOption,KindleRestSiteOption}.OnSelect`;
`Hooks.Hook.{ModifyRestSiteOptions,ModifyCardRewardAlternatives}`.

### G-shared

`ToyBox.{AfterObtained,AfterCombatEnd,CombatsSeen}`;
`RelicModel.{IsWax,IsMelted}`; `Commands.RelicCmd.Melt`;
`PumpkinCandle.{KindleCount,AfterObtained,ModifyMaxEnergy,AfterCombatEnd,Rekindle}`;
`Entities.RestSite.KindleRestSiteOption.OnSelect`;
`WingedBoots.{TimesUsed,ShouldAllowFreeTravel,AfterRoomEntered}`.

### G-pets

`Byrdpip.{AfterObtained,BeforeCombatStart,SummonPet}`;
`PaelsLegion.{BeforeCombatStart,SummonPet,ModifyBlockMultiplicative,AfterCardPlayed,AfterSideTurnStart}`;
`Commands.PlayerCmd.AddPet`;
`Entities.Players.PlayerCombatState.{AddPetInternal,GetPet}`;
`Models.Cards.ByrdSwoop.OnPlay`;
`Models.Monsters.{Byrdpip,PaelsLegion}.SetupSkins`.

### G-opportunity

`LavaRock.TryModifyRewards`;
`WongosMysteryTicket.{CombatsFinished,AfterCombatEnd,TryModifyRewards}`;
`SwordOfStone.{ElitesDefeated,AfterCombatVictory}`;
`{BlackStar,WhiteStar}.TryModifyRewards`;
`WarHammer.AfterCombatVictory`; `BoomingConch.{ModifyHandDraw,AfterSideTurnStart}`;
`SlingOfCourage.AfterRoomEntered`; `Pantograph.BeforeCombatStart`;
`FishingRod.AfterCombatEnd`.

### G-idempotent

`IceCream.ShouldPlayerResetEnergy`; `{RunicPyramid,RingingTriangle}.ShouldFlush`;
`MiniatureTent.ShouldDisableRemainingRestSiteOptions`;
`DingyRug.ModifyCardRewardCreationOptions`; `Driftwood.TryModifyRewardsLate`;
`JuzuBracelet.ModifyUnknownMapPointRoomTypes`;
`WhiteBeastStatue.ShouldForcePotionReward`.

### G-threshold

`SturdyClamp.{ShouldClearBlock,AfterPreventingBlockClear}`;
`TheBoot.ModifyHpLostAfterOstyLate`;
`BeatingRemnant.{ModifyHpLostAfterOsty,AfterDamageReceived,BeforeSideTurnStart}`;
`BrilliantScarf.{TryModifyEnergyCostInCombatLate,TryModifyStarCost,AfterCardPlayed,BeforeSideTurnStart}`;
`Hooks.Hook.ShouldClearBlock`.

### G-upgrades

`{FrozenEgg,MoltenEgg,ToxicEgg}.{TryModifyCardBeingAddedToDeck,TryModifyCardRewardOptionsLate}`;
`Helpers.Models.EggRelicHelper.UpgradeValidCards`;
`Bellows.AfterPlayerTurnStart`; `RazorTooth.AfterCardPlayed`;
`{LavaLamp,FresnelLens,Glitter}.TryModifyCardRewardOptionsLate`;
`GhostSeed.{CanAffect,AfterCardEnteredCombat}`;
`BoneTea.AfterPlayerTurnStart`;
`Models.CardModel.{IsUpgradable,MaxUpgradeLevel}`;
`Models.EnchantmentModel.{IsStackable,CanEnchant}`.

### G-targets

`{PandorasBox,NeowsTalisman,LeafyPoultice,NutritiousSoup,PaelsClaw}.AfterObtained`;
`Models.Enchantments.{Goopy,TezcatarasEmber}`;
`Models.EnchantmentModel.CanEnchant`.

### G-inert

`{FakeMerchantsRug,WongoCustomerAppreciationBadge,Circlet,DeprecatedRelic}`;
their native localization; `FakeSneckoEye.{BeforeCombatStart,ApplyPower}`;
`Models.Powers.ConfusedPower.{StackType,AfterCardDrawn}`;
`RoyalPoison.AfterPlayerTurnStart`; `TeaOfDiscourtesy.BeforeCombatStart`.

### G-counters

`LizardTail.{WasUsed,ShouldDieLate,AfterPreventingDeath}`;
`Hooks.Hook.ShouldDie`;
`{BoneTea,EmberTea}.CombatsLeft`;
`MawBank.{HasItemBeenBought,AfterRoomEntered,AfterItemPurchased}`;
`SilverCrucible.{TryModifyCardRewardOptionsLate,AfterModifyingCardRewardOptions,AfterRoomEntered,ShouldGenerateTreasure}`;
`SilkenTress.{AfterObtained,TryModifyCardRewardOptionsLate,AfterModifyingCardRewardOptions}`.

### G-shop

`MembershipCard.ModifyMerchantPrice`;
`TheCourier.{ModifyMerchantPrice,ShouldRefillMerchantEntry}`;
`Hooks.Hook.ModifyMerchantPrice`.

### G-reference

`BingBong.AfterCardChangedPiles`;
`{Orichalcum,FakeOrichalcum}.{BeforeSideTurnEndVeryEarly,BeforeSideTurnEnd}`;
`UnceasingTop.AfterHandEmptied`;
`SneckoEye.{ModifyHandDraw,ApplyPower}`; `Models.Powers.ConfusedPower.StackType`;
`{Ectoplasm,Sozu,VelvetChoker,PrismaticGem,PhilosophersStone,SpikedGauntlets}.ModifyMaxEnergy`;
`Fiddle.{ModifyHandDraw,ShouldDraw}`;
`Brimstone.AfterSideTurnStart`; `SealOfGold.AfterSideTurnStart`;
`ToastyMittens.AfterPlayerTurnStart`;
`DelicateFrond.BeforeCombatStart`;
`WhisperingEarring.AfterAutoPrePlayPhaseEnteredLate`;
`{EmptyCage,Astrolabe,DollysMirror,CallingBell,SmallCapsule,LargeCapsule,Orrery,NeowsBones}.AfterObtained`.

### G-costs

`{PrecariousShears,FragrantMushroom,LeafyPoultice,SereTalon,Storybook}.AfterObtained`;
`Models.Cards.BrightestFlame.OnPlay`;
`Commands.CreatureCmd.{LoseMaxHp,Damage,SetMaxHp}`.

### G-multi

`MassiveScroll.{IsAllowed,AfterObtained}`;
`{SilverCrucible,WingedBoots}.IsAllowed`;
`Entities.Cards.CardMultiplayerConstraint`;
`MembershipCard.ModifyMerchantPrice`.

### P — isolated inventory / method probes

Local research `Inventory.cs` and `probe-results.txt`, run using the
fingerprinted assemblies, .NET 9.0.318, and synthetic unsaved objects.
Recorded exceptions originate in the native methods identified above.
No gameplay simulation or full acquisition transaction is claimed.

### M — Mirror behavior under review

Repository working-tree sources:
[`MirrorDuplication.cs`](../../TheArchitectCode/Relics/MirrorDuplication.cs),
[`HandheldMirror.cs`](../../TheArchitectCode/Relics/HandheldMirror.cs),
and [`HandheldMirrorTests.cs`](../../tests/Architect/HandheldMirrorTests.cs).
These pre-existing working-tree changes were not edited by this research.

### R — repository context

[`README.md`](../../README.md) and
[`native-ancient-extension-points.md`](native-ancient-extension-points.md):
version/build target and the Architect's appended route. These are project
context, not substitutes for native implementation evidence.

[G]: #g--installed-game
[L]: #l--native-localization
[G-inventory]: #g-inventory
[G-acquire]: #g-acquire
[G-hooks]: #g-hooks
[G-setup]: #g-setup
[G-singleton]: #g-singleton
[G-rest]: #g-rest
[G-shared]: #g-shared
[G-pets]: #g-pets
[G-opportunity]: #g-opportunity
[G-idempotent]: #g-idempotent
[G-threshold]: #g-threshold
[G-upgrades]: #g-upgrades
[G-targets]: #g-targets
[G-inert]: #g-inert
[G-counters]: #g-counters
[G-shop]: #g-shop
[G-reference]: #g-reference
[G-costs]: #g-costs
[G-multi]: #g-multi
[P]: #p--isolated-inventory--method-probes
[M]: #m--mirror-behavior-under-review
[R]: #r--repository-context
