# Native Ancient extension points

Research: 2026-09-08, [#22](https://github.com/Philip-Scott/slay-the-spire-the-architect/issues/22).
Scope: a **new** Ancient before the unchanged **Rest Site → Shop → Boss**
route; extension feasibility, not offering balance, ownership rules, or other
product decisions.

## Version and evidence boundary

- Repository target: game minimum **0.111.0**, **BaseLib 3.4.5**, .NET 9,
  Godot SDK 4.5.1. The game is referenced from the local installation, not a
  version-pinned game package. “Current” below means this target, not every
  future public-beta release. [R]
- Installed game independently checked: Steam **public-beta**, build
  **24724944**; `release_info.json`: **v0.111.0**, commit **41cef1ea**.
  `sts2.dll` SHA-256:
  `2b40d2df538db1ceb5fa48d958c80ab730ada1e07db88a870aff01a661768b9f`.
  Native evidence below is read-only assembly metadata and call/field-reference
  inspection, including generated async state machines—not a runtime playtest
  or redistributed/decompiled game source. [G]
- BaseLib's installed **3.4.5 NuGet package** identifies source commit
  **`4a97642d7843309cdf35c46a11e3f46132cee049`**. Its **v3.4.5 tag**
  points to **`22757933ba10adc4322a628519a233a567507d87`**; the comparison changes
  only `CustomPotionModel.cs`, the project, and manifest. The Ancient, event,
  save, and reward sources cited here are pinned to the package commit. [B0]
- Earlier [act-lifecycle](act-lifecycle-hooks.md) and
  [snapshot](corrupted-player-snapshot-persistence.md) notes include older snapshots
  or third-party evidence. They are context, **not proof of current native
  behavior**. The [installed-beta note](beta-lifecycle-integration.md) names the
  same game build; relevant native members were rechecked here. [R]

## Feasibility / limitations matrix

**Supported** means an explicit BaseLib extension or native accessible member;
BaseLib itself implements several extensions with Harmony patches. It does
not mean Mega Crit promises a stable mod API. **Static-confirmed** means the
named seam exists in the inspected build; runtime integration remains untested.

| Capability | Finding for these versions | Limitation / confidence |
| --- | --- | --- |
| New Ancient content | `CustomAncientModel`, `CustomContentDictionary.AddAncient`, `ModelDb.AncientEvent<T>()`; separate from ordinary `CustomEventModel`. [B1][B2][G-event] | Supported; high source confidence. Registration is not guaranteed selection in an appended act. |
| Restrict Ancient to custom act | `IsValidForAct(ActModel)` filters the shared subset; `CustomActModel.AllAncients` / `GetUnlockedAncients` are native-pool seams. [B2][B3] | The shared-subset patch returns immediately when that subset is null. Check the appended act's generation path rather than assuming auto-add suffices. |
| Force one Ancient | `ShouldForceSpawn(act, rngChosenAncient)` rewrites the generated `RoomSet.Ancient`. [B1][B2] | Supported BaseLib override, internally private-field-dependent and conflict-prone; requires an already-existing Ancient in the room set. Not a general room insertion API. |
| Ancient → existing route | `CustomCreateMap(RunState, bool)` → `ActMap.StartingMapPoint` of type `Ancient`; native room creation calls `ActModel.PullAncient()` and constructs `EventRoom`. [B3][G-map] | Static-confirmed; preserve downstream graph. Act entry/map UI integration still needs a smoke test. |
| Native Ancient icon / layout | `NAncientMapPoint`, `NAncientEventLayout`; BaseLib custom scene, map icon and outline properties. [B1][G-map] | Native Ancient map widget is special-cased for the **starting point**, not arbitrary grid nodes. |
| Three offer categories | `MakeOptionPools`, `OptionPools`, `AncientOption<T>`, weighted pools and spawn conditions. [B1][B4][B5] | Supports one pool, two-plus-one, or one-per-slot; no category semantics or balance supplied. Empty eligible pools can throw. |
| Recovery / risky / lasting effects | Relic acquisition hooks, native commands, model combat hooks, saved relic/player/run data. [G-effects][B7] | No automatic “until Architect,” “next combat,” or transactional multi-effect lifetime. Inherited Ancient entry also has healing behavior. |
| Multiplayer Ancient choices | Native `EventSynchronizer.ChooseLocalOption`, per-player event instances, shared voting path, nested `PlayerChoiceSynchronizer`. [G-net][B5][B8] | Static-confirmed locally; not proof that this mod's entire Architect Act supports multiplayer. |
| Deterministic offers | Event-local `Rng`; BaseLib pool rolls use it. [G-event][B4][B5] | Requires identical ordering, eligibility, model preparation and content on every peer; no automatic synchronization of an arbitrary offer payload. |
| Event/reward resume | Native event identity + prefinished marker; serializable reward types; BaseLib run/player/relic/reward extensions. [G-save][B7][B8] | **Not arbitrary page, callback, vote, pending-screen or partially-applied-bundle persistence.** Custom recovery semantics and fault tests remain necessary. |

## Implementation-ready seams and constraints

### 1. Registration, entry, map, and presentation

`CustomAncientModel(bool autoAdd = true, bool logDialogueLoad = false)`
derives from `AncientEventModel`. Auto-add registers it in BaseLib's Ancient
collection and compendium/shared-Ancient integration. It is **not** an
ordinary random event registered through `CustomEventModel.Acts`. BaseLib
sorts candidate custom Ancients by ordinal model entry before filtering.
`IsValidForAct` is the narrow spawn predicate; `ShouldForceSpawn` is a
separate, explicitly cautioned force-selection hook. [B1][B2]

For a non-vanilla act index, `CustomActModel.AllAncients` must be overridden:
the default throws outside indexes 0–2. Its default `GetUnlockedAncients`
returns that list. Inspect whether room generation initializes
`ActModel.SetSharedAncientSubset`; BaseLib's `IsValidForAct` injection depends
on it. Native `ActModel.GenerateRooms(Rng, UnlockState, bool)` selects the
Ancient into `RoomSet`; `PullAncient()` exposes the result. There is no public
Ancient setter on the inspected `ActModel`. [B2][B3][G-map]

The native-shaped graph is **starting Ancient → Rest Site → Shop → Boss**,
with start and boss separate from the interior grid. `NMapScreen.SetMap`
creates normal widgets for grid points, but chooses `NAncientMapPoint` for an
Ancient starting point; its `_Ready` reads icons from the **act's selected
Ancient**, not a model attached independently to each map node. Thus merely
putting `MapPointType.Ancient` in a grid cell does not establish native Ancient
presentation. `RunManager.EnterAct(int, bool)` contains the starting-coordinate
entry path as well as a map-room path; these must be exercised with the
appended act's actual run state. [G-map]

Assets: override `CustomScenePath`, `CustomMapIconPath`,
`CustomMapIconOutlinePath`, and, if needed,
`CustomRunHistoryIconOutlinePath`. `DefineDialogues`, `ButtonColor`, and
`DialogueColor` are presentation seams; BaseLib provides `ancients`
localization/dialogue loading. **Caution:** `CustomRunHistoryIconPath` is
declared but has no corresponding application patch in the inspected
`CustomAncientModel.cs`; do not assume that property alone changes the main
history icon. Custom scene loading is not proof of correct scene structure,
controller navigation, icon sizing, or compact-map spacing. [B1][G-map]

### 2. Offers, preparation, and deterministic generation

BaseLib's `GenerateInitialOptions()` rolls `OptionPools.Roll(Rng, this)` and
wraps each prepared mutable relic with native `RelicOption`. Three pools can
represent build-defining / recovery / risky categories without committing to
which offers belong in them. One pool supplies all three slots; two pools
supply two slots plus one. Removal prevents repeated selection within a
continuously reused pool, **not global duplication across separate pools**.
Each slot must have a valid eligible draw; positive weights and sufficient
eligible entries are implementation preconditions, not enforced product rules.
[B1][B4]

`AncientOption<T>(weight, relicPrep, makeAllVariants)` supports prepared relic
variants; `AllPossibleOptions` expands variants for the Ancient's presentation
surface. `ModelPrep` receives a fresh mutable relic, **before relic ownership
is assigned**. Use the event's `Owner` for eligibility/preparation, never the
local UI player. BaseLib explicitly states eligibility executes locally for
**all** players on **each** peer. [B1][B5]

Important evaluation trap: `OptionPools.Roll` accesses `ModelForOption` while
filtering eligibility, then `GenerateInitialOptions` accesses it again for
selected results. Therefore `ModelPrep` can run for unselected candidates and
more than once for a selected candidate. Side effects or conditional RNG
consumption there can change offer results. Cache/freeze variant data
explicitly if needed; no automatic variant memoization is supplied. [B4][B5]

Native `EventModel.BeginEvent` creates an event `Rng` using run seed, model
entry's deterministic hash, and shared/per-player slot handling. BaseLib's
weighted selection consumes that RNG through `Rng.NextInt`. This is evidence
for reproducible generation given equal inputs—not a guarantee after changed
pool ordering, mod versions, owner state, or arbitrary page reconstruction.
The run and player RNG sets have serialization methods, but the event RNG
itself is not an arbitrary saved-event state payload. [G-event][G-save][B4]

### 3. Award execution and effect lifetimes

Native Ancient `RelicOption` obtains the relic through `RelicCmd.Obtain` and
then marks the Ancient done. `RelicModel.AfterObtained()` is the one-shot
acquisition seam; `AfterRemoved()` and `RelicCmd.Remove` are removal seams.
Longer effects can use a custom relic's inherited `AbstractModel` hooks such
as `BeforeCombatStart`, `AfterCombatVictory`, or `AfterCombatEnd(CombatRoom)`.
These are lifecycle mechanisms, not an automatic duration policy. [G-effects]

**Inherited side effect to account for:** `AncientEventModel.BeforeEventStarted
(bool isPreFinished)` contains native HP/healing and ascension handling,
records `HealedAmount`, and the Ancient layout has healing VFX. A new Ancient
is not an offers-only shell by default. The exact intended recovery amount,
interaction with the following Rest Site, and whether to preserve/override
this virtual hook remain product/integration questions; this research does
not choose them. [G-event][G-map]

For immediate changes, native command seams include `CreatureCmd.Heal`,
`RelicCmd.Obtain/Remove`, and card-selection/upgrade commands. For lasting
counters or delayed penalties, acquired relics serialize model ID and
`SavedProperties`; loading fills properties rather than calling acquisition
again in `RelicModel.FromSerializable`. Player save data carries HP, deck,
relics, etc., but not a general live combat-power list. A combat power alone
is therefore not evidence of a persistent cross-room effect carrier.
Use an explicitly serialized carrier if a duration spans rooms or reloads.
[G-effects][G-save][B7]

An Ancient choice is not necessarily a reward-screen item. For an actual
reward screen, native `RewardsSet.WithCustomRewards(...).Offer()` and
`Reward` subclasses exist; BaseLib `CustomReward` adds a static
`DeserializeMethod` plus initialization/registration, not automatic
synchronization of every custom side effect. Its `CardUpgradeReward` provides
first-party evidence of delegating nested selection to synchronized
`CardSelectCmd.FromDeckForUpgrade`. [G-rewards][B8]

### 4. Multiplayer choices: local capability, not whole-act readiness

`EventModel.IsShared` defaults to false. `EventSynchronizer.BeginEvent`
creates a mutable event for each player; `ChooseLocalOption(int)` uses either
the per-player choice path or shared voting. The shared path has page-index
checks, a host/network-type-gated selection step, and
`_multiplayerOptionSelectionRng` / `Rng.NextItem` before broadcasting the chosen
index. Do **not** describe it as a majority-vote contract. A shared option is
applied through the per-player event path for all participants. [G-net]

Consequences: options must have matching index meaning on all peers; shared
choices need compatible slots across participants; callbacks must not apply
the entire party's mutation once per player instance. Nested card choices
must use native synchronized selectors/choice contexts. Raw UI delegates,
local-player-dependent filtering, and arbitrary host-only state are not
made deterministic merely by inheriting `CustomAncientModel`. These are
integration deductions from the per-player execution and indexed protocol,
not observed failures in this mod. [G-net][B5][B8]

Rewards use **`RewardsSetSynchronizer`** for set/index choice and
**`RewardSynchronizer`** for acquisition/removal operations; they are distinct.
BaseLib's `CustomLinkedRewardSet` needs its own message containing set,
container, and nested indexes because the native set-choice message lacks
nested selection identity. Its source explicitly requires consistent nested
resolution order and prohibits nesting linked sets. This supports reuse of
that facility where applicable, not inventing an unverified bundled-award
protocol. [G-net][B9]

The current production Architect Act is **single-player-only**, with
multiplayer retaining the vanilla route. Ancient-local networking capability
does not remove that restriction or prove Challenger/lifecycle readiness.
[R]

#### Repository integration prerequisites (baseline `4fb54f5`)

- **Entry and terminal lifecycle are SP-only.** `AppendAct`,
  `EnterArchitectActPatch`, entry snapshot capture and `IsEncounter` enforce
  one player. Consequently reward suppression, completion/resume, epoch and
  game-over handling do not establish a multiplayer path. The victory
  interception is scoped to the vanilla Architect event, act index 2,
  an ordinary three-act run and non-abandonment; transition readiness already
  uses native `ActChangeSynchronizer.SetLocalPlayerReady`. Outcome capture
  selects `run.Players[0]`. Removing one entry guard is insufficient.
  [R-lifecycle]
- **Snapshot state is local, not replicated authority.** `ArchitectRun` saves
  SP-only `TheArchitect.Run.v1` data (`Id`, `Entered`, `EntrySnapshot`,
  `Outcome`, `SnapshotRevision`), with a locally generated GUID and profile
  snapshot loaded on entry. `ChallengerStore` supplies profile-scoped storage,
  revision/content hash and terminal-run-ID idempotence, not peer replication.
  The encounter's seed includes the run seed, snapshot revision/hash and
  encounter ID: different peer snapshots would produce different inputs.
  [R-state]
- **Repeat Visit multiplayer needs combat integration.** `NativeChallenger`
  construction and `AssertIdentity` explicitly require one real party member;
  its enemy-owned synthetic player uses `ulong.MaxValue - 17` in an isolated
  run. Room backgrounds and the developer console also remain SP-scoped.
  Native Ancient choice synchronization does not address these restrictions.
  [R-combat]
- **Map/content integration is required even for SP.** `ArchitectAct` has
  empty `AllAncients` and bypasses ordinary room generation with a boss-only
  room set. Its `ArchitectMap` is starting Rest Site row 0 → Shop row 1 →
  Boss row 2, with a 7×2 interior grid. Thus registration alone cannot supply
  this act's Ancient. `ArchitectMapLayout` replaces four beta-specific layout
  constants and bypasses the native intro; the current icon shader targets
  `NNormalMapPoint`, not the native Ancient widget. Preserve the downstream
  route while explicitly adapting these integrations. [R-map]

Broader multiplayer prerequisites, ownership/host/disconnect questions remain
in [#24](https://github.com/Philip-Scott/slay-the-spire-the-architect/issues/24);
Ancient persistence remains in
[#26](https://github.com/Philip-Scott/slay-the-spire-the-architect/issues/26).
These are unimplemented dependencies, not decisions made by this report.

### 5. Save/resume boundaries

Native `EventRoom.ToSerializable()` adds **event ID and prefinished flag** to
the room payload. `SerializableRoom` does not contain arbitrary event pages,
generated options, pending callbacks, votes, or event RNG state.
`EventRoom.OnEventStateChanged` checks the participants' completion state and
uses `MarkPreFinished` / `SaveManager.SaveRun`; `EventRoom.Resume` delegates to
`EventSynchronizer.ResumeEvents` for an exited sub-room. That in-memory resume
hook is **not** proof of disk continuation at an arbitrary event page.
`RunManager.ToSave` stores a supplied prefinished room plus players, acts,
map/RNG and other run state. [G-save]

BaseLib `ExtendedSaveTypes.RegisterSavedValue` supports card, relic, potion,
enchantment, player, reward and `IRunState` holders—but **not `EventModel`**.
Putting `[SavedProperty]` or a `SavedSpireField` on an Ancient is not an
established persistence solution. A mod-owned versioned run/player/relic
payload is available for offer IDs, prepared variants and resolution phase;
registration must occur before serializer metadata initialization, with
matching packet serializers on peers. This supplies storage, not a durable
transaction or automatic event-page restore. [B7]

Native `SerializableReward` stores reward type and selected construction
parameters, not a universal snapshot of an open screen or an applied flag.
For example, `CardReward.ToSerializable` saves pool/source/odds/count, whereas
`SpecialCardReward` has a serialized card slot; `RelicReward` supports a
predetermined model ID, not arbitrary prepared relic-instance state.
BaseLib custom/extended reward serialization can add data, but it does not
make an event's transient `RewardsSet` reachable from the disk run save.
[G-rewards][G-save][B7][B8]

`CustomLinkedRewardSet` serializes child reward payloads and bundle type;
its registered payload does **not** include `_selectionStarted`,
`_pendingSelection`, or completed-child flags. Therefore serialization
support must not be advertised as crash-safe continuation of a partially
claimed bundle. The same caution applies to a risky offer with separately
awaited cost and benefit. [B9]

## What is proven, and what still needs execution

No decisive source-access blocker remains for identifying these extension
points: the exact installed native assembly and first-party BaseLib sources
were available. **High confidence:** member presence, BaseLib pool/registration
logic, and inspected payload boundaries. **Moderate confidence:** composed
native entry/network/save behavior inferred from metadata and call references.
**Unverified:** end-to-end integration, reload equivalence, disconnect behavior,
and crash atomicity; no game launch or gameplay implementation was performed.
[G][B1][B2][B4][B7]

Implementation validation gates, not product decisions:

1. Instantiate/register the new Ancient and enter it through the custom act's
   starting point; check native dialogue, icons, controller focus, and the
   unchanged downstream route. [B1][G-map]
2. Verify native entry healing deliberately, all eligible-pool edge cases,
   prepared variants, and repeated generation with fixed seed/owner state.
   [G-event][B4][B5]
3. Compare offer IDs/order/variants and callback effects across peers; exercise
   shared and individual choice paths plus nested card selection. Whole-act
   multiplayer prerequisites must be handled separately. [G-net][B8][R]
4. Reload before choosing, after choosing, during a nested reward/selection,
   after one bundle component, and after only some players finish. Establish
   explicit reconstruction/idempotency behavior rather than relying on
   transient event or reward fields. [G-save][B7][B9]

## Sources / reproduction locators

Every `[G-*]` locator refers to **the fingerprinted installed build above**,
not a public source mirror. Only API names and factual observations are
recorded here; native binaries, extracted assets, and game method bodies are
not included.

- **[R]** Repository [manifest](../../TheArchitect.json),
  [project](../../TheArchitect.csproj),
  [path discovery](../../Sts2PathDiscovery.props), [README](../../README.md);
  earlier research linked in the version section.
- **[R-lifecycle]** Repository baseline **`4fb54f576196205e26bea1252f87797fe9ab9e2b`**:
  [`ArchitectLifecycle.cs`](https://github.com/Philip-Scott/slay-the-spire-the-architect/blob/4fb54f576196205e26bea1252f87797fe9ab9e2b/TheArchitectCode/Lifecycle/ArchitectLifecycle.cs).
- **[R-state]** Same baseline:
  [`ArchitectRun.cs`](https://github.com/Philip-Scott/slay-the-spire-the-architect/blob/4fb54f576196205e26bea1252f87797fe9ab9e2b/TheArchitectCode/Persistence/ArchitectRun.cs),
  [`ChallengerStore.cs`](https://github.com/Philip-Scott/slay-the-spire-the-architect/blob/4fb54f576196205e26bea1252f87797fe9ab9e2b/TheArchitectCode/Persistence/ChallengerStore.cs),
  [`ArchitectEncounter.cs`](https://github.com/Philip-Scott/slay-the-spire-the-architect/blob/4fb54f576196205e26bea1252f87797fe9ab9e2b/TheArchitectCode/Encounters/ArchitectEncounter.cs).
- **[R-combat]** Same baseline:
  [`NativeChallenger.cs`](https://github.com/Philip-Scott/slay-the-spire-the-architect/blob/4fb54f576196205e26bea1252f87797fe9ab9e2b/TheArchitectCode/Challenger/NativeChallenger.cs),
  [`ArchitectRoomBackgrounds.cs`](https://github.com/Philip-Scott/slay-the-spire-the-architect/blob/4fb54f576196205e26bea1252f87797fe9ab9e2b/TheArchitectCode/Lifecycle/ArchitectRoomBackgrounds.cs),
  [`ArchitectConsolePlaytest.cs`](https://github.com/Philip-Scott/slay-the-spire-the-architect/blob/4fb54f576196205e26bea1252f87797fe9ab9e2b/TheArchitectCode/Playtest/ArchitectConsolePlaytest.cs).
- **[R-map]** Same baseline:
  [`ArchitectAct.cs`](https://github.com/Philip-Scott/slay-the-spire-the-architect/blob/4fb54f576196205e26bea1252f87797fe9ab9e2b/TheArchitectCode/Acts/ArchitectAct.cs),
  [`ArchitectMapLayout.cs`](https://github.com/Philip-Scott/slay-the-spire-the-architect/blob/4fb54f576196205e26bea1252f87797fe9ab9e2b/TheArchitectCode/Lifecycle/ArchitectMapLayout.cs),
  [`ArchitectMapIcons.cs`](https://github.com/Philip-Scott/slay-the-spire-the-architect/blob/4fb54f576196205e26bea1252f87797fe9ab9e2b/TheArchitectCode/Lifecycle/ArchitectMapIcons.cs).
- **[G]** Local Steam `steamapps/appmanifest_2868840.acf` (`buildid`, `BetaKey`);
  `steamapps/common/Slay the Spire 2/release_info.json`; assembly
  `data_sts2_linuxbsd_x86_64/sts2.dll`. Inspected with `dnfile 0.18.0` /
  `dncil 1.0.2` after host `dotnet` was unavailable; no production build or
  dependency changes.
- **[G-event]** `MegaCrit.Sts2.Core.Models.{EventModel,AncientEventModel}`:
  `BeginEvent`, `BeforeEventStarted`, `GenerateInitialOptionsWrapper`,
  `SetInitialEventState`, `RelicOption`, `StartPreFinished`, `get_IsShared`;
  async state machines `<BeginEvent>d__28`, `<BeforeEventStarted>d__62`.
- **[G-map]** `Core.Models.ActModel`: `GenerateRooms`, `Ancient`,
  `PullAncient`, `SetSharedAncientSubset`; `Core.Rooms.RoomSet`;
  `Core.Runs.RunManager`: `CreateRoom` (private), `EnterAct`,
  `<EnterAct>d__208`; `Core.Map.{ActMap,MapPointType,StandardActMap}`;
  `Core.Nodes.Screens.Map.{NMapScreen.SetMap,NAncientMapPoint._Ready}`;
  `Core.Nodes.Events.NAncientEventLayout`.
- **[G-effects]** `Core.Models.{RelicModel,AbstractModel}`:
  `AfterObtained`, `AfterRemoved`, `BeforeCombatStart`, `AfterCombatVictory`,
  `AfterCombatEnd`, `ToSerializable`, `FromSerializable`;
  `Core.Commands.{RelicCmd,CreatureCmd}`;
  Ancient `<<RelicOption>g__OnChosen|0>d` call references.
- **[G-net]** `Core.Multiplayer.Game.EventSynchronizer`: `BeginEvent`,
  `ChooseLocalOption`, `PlayerVotedForSharedOptionIndex`,
  `ChooseSharedEventOption`, `ChooseOptionForSharedEvent`,
  `ChooseOptionForEvent`, handlers and fields; adjacent
  `RewardsSetSynchronizer` / `RewardSynchronizer`;
  `Core.GameActions.Multiplayer.PlayerChoiceSynchronizer.SyncLocalChoice`.
- **[G-save]** `Core.Rooms.EventRoom`: `ToSerializable`,
  `OnEventStateChanged`, `Resume`, `<EnterInternal>d__18`;
  `Core.Saves.SerializableRun`; `Core.Saves.Runs.{SerializableRoom,
  SerializablePlayer,SerializableReward,SavedProperties}`;
  `Core.Runs.{RunManager.ToSave,RunRngSet}`;
  `Core.Random.PlayerRngSet`.
- **[G-rewards]** `Core.Rewards.{Reward,RewardsSet,CardReward,RelicReward}`:
  `FromSerializable`, `ToSerializable`, `Populate`,
  `WithCustomRewards`, `Offer`; `Core.Saves.Runs.SerializableReward`.
- **[B0]** Installed
  `~/.nuget/packages/alchyr.sts2.baselib/3.4.5/alchyr.sts2.baselib.nuspec`;
  [v3.4.5 tag](https://github.com/Alchyr/BaseLib-StS2/tree/v3.4.5);
  [package-to-tag comparison](https://github.com/Alchyr/BaseLib-StS2/compare/4a97642d7843309cdf35c46a11e3f46132cee049...22757933ba10adc4322a628519a233a567507d87).
- **[B1]** BaseLib
  [`CustomAncientModel.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/4a97642d7843309cdf35c46a11e3f46132cee049/Abstracts/CustomAncientModel.cs).
- **[B2]** BaseLib
  [`ContentPatches.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/4a97642d7843309cdf35c46a11e3f46132cee049/Patches/Content/ContentPatches.cs),
  [`CustomEventModel.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/4a97642d7843309cdf35c46a11e3f46132cee049/Abstracts/CustomEventModel.cs).
- **[B3]** BaseLib
  [`CustomActModel.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/4a97642d7843309cdf35c46a11e3f46132cee049/Abstracts/CustomActModel.cs).
- **[B4]** BaseLib
  [`OptionPools.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/4a97642d7843309cdf35c46a11e3f46132cee049/Utils/OptionPools.cs),
  [`WeightedList.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/4a97642d7843309cdf35c46a11e3f46132cee049/Utils/WeightedList.cs).
- **[B5]** BaseLib
  [`AncientOption.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/4a97642d7843309cdf35c46a11e3f46132cee049/Utils/AncientOption.cs),
  [`RelicModelExtensions.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/4a97642d7843309cdf35c46a11e3f46132cee049/Extensions/RelicModelExtensions.cs).
- **[B7]** BaseLib
  [`ExtendedSaveTypes.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/4a97642d7843309cdf35c46a11e3f46132cee049/Patches/Saves/ExtendedSaveTypes.cs),
  [`ExtendedSaveHandlers.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/4a97642d7843309cdf35c46a11e3f46132cee049/Patches/Saves/ExtendedSaveHandlers.cs),
  [`PostModInitPatch.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/4a97642d7843309cdf35c46a11e3f46132cee049/Patches/PostModInitPatch.cs).
- **[B8]** BaseLib
  [`CustomReward.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/4a97642d7843309cdf35c46a11e3f46132cee049/Abstracts/CustomReward.cs),
  [`CustomRewardPatches.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/4a97642d7843309cdf35c46a11e3f46132cee049/Patches/Content/CustomRewardPatches.cs),
  [`CardUpgradeReward.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/4a97642d7843309cdf35c46a11e3f46132cee049/Common/Rewards/CardUpgradeReward.cs).
- **[B9]** BaseLib
  [`CustomLinkedRewardSet.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/4a97642d7843309cdf35c46a11e3f46132cee049/Common/Rewards/LinkedRewardSet/CustomLinkedRewardSet.cs).
