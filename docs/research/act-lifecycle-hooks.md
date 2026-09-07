# Research note: Act-4 lifecycle hooks (append act, map path, encounter entry, run termination, successor snapshot)

Status: research only — no production code in this note. Confirms which Slay the Spire 2 (`sts2.dll`,
`MegaCrit.Sts2.Core.*`) and BaseLib (`Alchyr.Sts2.BaseLib`) hooks exist to: (1) add a fixed Architect
Act after Act 3, (2) build a Rest Site → Shop → boss map path, (3) enter a custom encounter, (4)
terminate the run on win or loss, and (5) capture a "successor" snapshot of the run without corrupting
normal save/stat bookkeeping.

No local copy of `sts2.dll`/`0Harmony.dll` or a decompiled build was available in this workspace or on
this machine (searched for `sts2.dll`, `0Harmony.dll`, and any `BaseLib*` under the whole filesystem;
nothing found — see Gaps). Findings below are instead built from two primary/first-party-adjacent
sources plus one working third-party mod that already implements this exact scenario, all fetched
live from GitHub:

- **BaseLib** (`Alchyr/BaseLib-StS2`) — the standardized base-content library this repo already takes
  a hard NuGet dependency on (`TheArchitect.csproj:39`, `TheArchitect.json:9`). Read at commit
  [`2275793`](https://github.com/Alchyr/BaseLib-StS2/commit/22757933ba10adc4322a628519a233a567507d87).
- **ModTemplate-StS2** (`Alchyr/ModTemplate-StS2`) — the template this repo was scaffolded from
  (README.md:3). Read at commit
  [`55ca2c6`](https://github.com/Alchyr/ModTemplate-StS2/commit/55ca2c606e6c78dd39689a5cf979b243a49652e7).
  It contains no act/run-lifecycle code (only C#/Godot project scaffolding), so it is not cited further.
- **Act4FinalAscent** (`kphxgames/Act4FinalAscent`, MIT, published on NexusMods) — a shipping,
  unrelated third-party STS2 mod that adds "The Architect" as a fixed Act 4 boss after Act 3. It is
  the single closest known prior art to this repo's premise and directly exercises every one of the
  five hooks below against real `MegaCrit.Sts2.Core` types. Read at commit
  [`05c251a`](https://github.com/kphxgames/Act4FinalAscent/commit/05c251a4186b323fc2a7fef5dab3cf586b856767).
  Treat this as **third-party demonstration code, not an official API contract** — see Gaps/Risks.

Independent decompiled mirrors (`zhiyue/sts2-rl-agent`, `Zamiell/slay-the-spire-2-emulator`,
`hongyipan152/STS2SourceCode`) were used **only** to cross-check that specific method names quoted from
Act4FinalAscent (e.g. `RunManager.DebugOnlyGetState()`) really exist in `MegaCrit.Sts2.Core.Runs.RunManager`
and were not reflection guesses; three independently produced mirrors agree on the signature. These
mirrors are of unclear provenance/legality (likely unauthorized decompiles of Mega Crit's proprietary
game) and are **not cited as a source of truth** anywhere below — every functional claim is backed by
BaseLib or Act4FinalAscent source, which are legitimately public, MIT-licensed mod projects.

---

## Decision

Use this lifecycle, in this order:

1. Patch `EventModel.SetEventState` and narrow to the vanilla
   `MegaCrit.Sts2.Core.Models.Events.TheArchitect` instance at `CurrentActIndex == 2`. Replace or augment
   its final proceed option with the Architect Act transition. This is the demonstrated post-Act-3
   seam; do not globally append Act 4 when a run starts.
2. Before changing the live act list, call `RunManager.ToSave(null)` and copy the resulting
   `SerializableRun` into a mod-owned successor record/file. Do not write that snapshot through
   `SaveManager` or `ProgressSaveManager`.
3. Define the content with BaseLib `CustomActModel` and `CustomEncounterModel`. Append the act to the
   current `RunState.Acts` collection, generate its rooms, then allow the normal
   `RunManager.EnterNextAct()` path to enter index 3. Today the append requires reflection over
   `RunState.<Acts>k__BackingField`; isolate and version-check that adapter because it is not a
   supported BaseLib API.
4. Return a hand-built `ActMap` from `CustomActModel.CustomCreateMap`: start → Rest Site → Shop → boss,
   with consecutive rows and child links. Let vanilla create Rest Site and Shop rooms; intercept
   `RunManager.CreateRoom` only for the boss and return
   `new CombatRoom(customEncounter.ToMutable(), runState)`.
5. On boss victory, call `RunManager.OnEnded(true)` once and pass its `SerializableRun` to
   `NRun.ShowGameOverScreen`. On an Act-4 defeat, let the ordinary defeat path reach
   `OnEnded(false)`, but prefix that call and change `isVictory` to `true` for base bookkeeping while
   recording the Architect loss separately in mod-owned data. This preserves the already-earned Act-3
   victory/ascension rather than turning the extended challenge into a base-game loss.

The last point is a product decision as much as an API decision: the working precedent treats Act 4 as
a bonus challenge, so both Act-4 outcomes are base-game victories and only mod-owned statistics
distinguish Architect win from loss. If the design instead intends an Architect loss to erase the Act-3
win, leave `isVictory == false`; no additional termination hook is needed.

---

## 1. Add a fixed Architect Act after Act 3

**Confirmed hook: `BaseLib.Abstracts.CustomActModel`**, an abstract subclass of the core
`MegaCrit.Sts2.Core.Models.ActModel` that auto-registers itself with the content dictionary.

> `protected CustomActModel(int actNumber, bool autoAdd = true)` — *"Set to -1 to prevent your act from
> spawning naturally. Otherwise, use 1/2/3 for the corresponding act."* `Index` is set to
> `actNumber - 1` (0-based).
> — [`Abstracts/CustomActModel.cs:33-40`](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Abstracts/CustomActModel.cs#L33-L40)

Registration happens through `CustomContentDictionary.AddAct`, hooked into `ModelDb.InitIds`:
[`Patches/Content/ContentPatches.cs:22,101-105`](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Patches/Content/ContentPatches.cs#L101-L105).
Passing `actNumber: 4` yields `Index == 3` and, per the doc comment, does **not** spawn the act
naturally in the normal Act 1→2→3 rotation — it must be appended explicitly to a specific run.

BaseLib does not itself append a `CustomActModel` instance to a live run's act list; that is a
run-lifecycle operation outside BaseLib's scope (BaseLib only standardizes *content registration*, not
*run flow*). Act4FinalAscent demonstrates the append explicitly, confirmed as the pattern this repo's
"Act 4" mod needs:

> ```csharp
> internal static void AppendAct4Placeholder(RunState runState)
> {
>     if (((IReadOnlyCollection<ActModel>)runState.Acts).Count <= 3)
>     {
>         ActModel val = ((ActModel)ModelDb.Act<Glory>()).ToMutable();
>         val.SetSecondBossEncounter(null);
>         val.SetBossEncounter(ModelDb.Encounter<Act4ArchitectBossEncounter>());
>         List<ActModel> val2 = runState.Acts.ToList();
>         val2.Add(val);
>         RunStateActsField.SetValue(runState, val2.ToArray());   // "<Acts>k__BackingField", via reflection
>         val.GenerateRooms(runState.Rng.UpFront, runState.UnlockState, ...);
>     }
> }
> ```
> — [`src/Act4Placeholder/Core/ModSupport.cs:2792-2809`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Core/ModSupport.cs#L2792-L2809)

Key facts this establishes:
- `RunState.Acts` is exposed as a read-only collection at the public API surface; there is **no public
  mutator**. Act4FinalAscent appends a 4th act by grabbing the private auto-property backing field
  `<Acts>k__BackingField` via `HarmonyLib.AccessTools.Field` reflection and overwriting it —
  [`ModSupport.cs:185`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Core/ModSupport.cs#L185).
  This is a **fragile, non-API-contract mechanism** (see Risks).
- The appended act reuses a clone of the vanilla `Glory` (Act 3) act model
  (`ModelDb.Act<Glory>().ToMutable()`) rather than a bespoke `CustomActModel`, then overrides only its
  boss encounter. A `CustomActModel`-based act (this repo's evident intent, given
  `TheArchitect.json`'s description) is equally appendable the same way — the append mechanism doesn't
  care whether the `ActModel` came from `ModelDb` or a custom act instance, only that it's a valid
  `ActModel` and rooms are generated for it (`ActModel.GenerateRooms(...)`).
- Entry into the appended act still goes through the normal act-transition call,
  `RunManager.EnterAct(int actIndex, bool ...)` /
  `RunManager.EnterNextAct()` — both are patched (Prefix/Postfix) rather than replaced, confirming they
  are the intended integration points:
  [`Patches/RunManagerEnterNextActPatch.cs:14-42`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Patches/RunManagerEnterNextActPatch.cs#L14-L42),
  and `RunManager.Instance.EnterAct(2, true)` called directly from a custom `GameAction` at
  [`Patches/AdminSkipToAct3Action.cs:64-74`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Patches/AdminSkipToAct3Action.cs#L64-L74).
- The demonstrated trigger point for "what happens after Act 3" is the base-game event model
  `MegaCrit.Sts2.Core.Models.Events.TheArchitect`. Act4FinalAscent prefixes
  `EventModel.SetEventState`, narrows to that concrete model plus `CurrentActIndex == 2` and at most
  three acts, then injects an `EventOption` whose callback snapshots the run and appends Act 4:
  [`EventModelSetEventStatePatch.cs:31-105,122-151`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Patches/EventModelSetEventStatePatch.cs#L31-L151).
  The callback finally marks the local player ready through `ActChangeSynchronizer`; the ordinary
  `EnterNextAct` flow sees four acts and enters index 3. This is preferable to a global
  `RunManager.EnterNextAct` replacement because it preserves the vanilla post-Act-3 event and
  multiplayer readiness flow. A separate `EventModel.IsShared` postfix is required by the precedent
  for co-op:
  [`TheArchitectIsSharedPatch.cs:16-36`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Patches/TheArchitectIsSharedPatch.cs#L16-L36).
- Map generation for the appended act is separately hookable and does **not** require a
  `CustomActModel` override — `MegaCrit.Sts2.Core.Hooks.Hook.ModifyGeneratedMap` is a `Harmony`-patchable
  static entry point, confirmed usable stand-alone:
  [`Patches/HookModifyGeneratedMapPatch.cs:14-22`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Patches/HookModifyGeneratedMapPatch.cs#L14-L22).
  If the act *is* a `CustomActModel`, BaseLib offers the same override as a first-class virtual method
  instead of a Harmony patch:
  `CustomActModel.CustomCreateMap(RunState, bool)`, prefixed onto `ActModel.CreateMap`
  — [`Abstracts/CustomActModel.cs:158-172,193-207`](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Abstracts/CustomActModel.cs#L158-L172).
  Returning `null` from `CustomCreateMap` falls through to vanilla generation; returning a map object
  short-circuits it — this is the cleanest, most future-proof integration point for hook #2 below.
- `AchievementsHelper.CheckForDefeatedAllEnemiesAchievement` is explicitly skipped for any
  `CustomActModel` act (`SkipModdedActAchievementPatch`), and the room-count defaults
  (`BaseNumberOfRooms`, `GetMapPointTypes`) only special-case `Index` 0/1/2, falling back to a
  15-room/Act-1-shaped default for any other index (including `3`) —
  [`Abstracts/CustomActModel.cs:114-148,~280-290`](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Abstracts/CustomActModel.cs#L114-L148).

## 2. Construct a Rest Site → Shop → boss map path

**Confirmed mechanism: a custom `MegaCrit.Sts2.Core.Map.ActMap` subclass built from `MapPoint` nodes
with an explicit `PointType`/adjacency graph**, installed either via `CustomActModel.CustomCreateMap`
(BaseLib, act-scoped, clean) or via a `Hook.ModifyGeneratedMap` Harmony postfix (act-index-scoped,
works even without a `CustomActModel`).

Act4FinalAscent's map is a hand-built 9-row graph ending in Rest Site → (an Unknown/event room) → boss,
with a Rest Site → Shop pairing also present mid-route:

> ```csharp
> public sealed class ShortAct4Map : ActMap
> {
>     public override MapPoint BossMapPoint { get; }
>     public override MapPoint StartingMapPoint { get; }
>     protected override MapPoint?[,] Grid { get; }
>     // Row layout: 1 Unknown(event) → 2 Monster → 3 branch(Treasure/Unknown/RestSite/Shop)
>     //           → 4 Elite → 5 Unknown(event) → 6 Shop → 7 RestSite → 8 Unknown(event) → 9 Boss
>     ...
>     MapPoint val6 = CreatePoint(3, 4, MapPointType.Elite);
>     MapPoint val7 = CreatePoint(3, 5, MapPointType.Unknown);
>     MapPoint valShop = CreatePoint(3, 6, MapPointType.Shop);
>     MapPoint val8 = CreatePoint(3, 7, MapPointType.RestSite);
>     MapPoint valLibrary = CreatePoint(3, 8, MapPointType.Unknown);
>     val6.AddChildPoint(val7);
>     val7.AddChildPoint(valShop);
>     valShop.AddChildPoint(val8);
>     val8.AddChildPoint(valLibrary);
>     valLibrary.AddChildPoint(BossMapPoint);
> }
> ```
> — [`src/Act4Placeholder/Map/ShortAct4Map.cs:17-84`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Map/ShortAct4Map.cs#L17-L84)

This confirms:
- `MapPointType` has public named members `Monster`, `Elite`, `Boss`, `Treasure`, `RestSite`, `Shop`,
  `Unknown` (all seen used by name across both repos; grepped and cross-checked — no invented values
  needed for a Rest Site → Shop → boss chain).
- `MapPoint(int col, int row)` is a plain constructor; `AddChildPoint(MapPoint)` builds the traversal
  graph; `ActMap.BossMapPoint` / `ActMap.StartingMapPoint` are the two required overrides that anchor
  the graph, and `Grid` (`MapPoint?[,]`) is the backing store `ActMap` itself reads from for the
  interactive map screen.
- A code comment warns the boss point must be placed exactly one row beyond
  `GetRowCount() - 1` (here row 9, with the grid's last populated row at 8) because vanilla
  `RecalculateTravelability`'s "last row" check is what unlocks the boss node via the direct
  `_bossPointNode` path, and that consecutive rows must have no gaps or the "Flight"-style modifier
  (which reads `GetPointsInRow(currentRow+1)`) leaves the next row unreachable —
  [`ShortAct4Map.cs:33-45`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Map/ShortAct4Map.cs#L33-L45).
  This is empirical/reverse-engineered behavior from a third party, not documented by Mega Crit — flag
  as a risk to re-verify against the actual installed `sts2.dll` before relying on it.
- The map is installed with a one-line postfix, independent of `CustomActModel`:
  `Hook.ModifyGeneratedMap(IRunState, int actIndex, ref ActMap __result)` —
  [`Patches/HookModifyGeneratedMapPatch.cs:14-22`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Patches/HookModifyGeneratedMapPatch.cs#L14-L22).
  For a `CustomActModel`-based act, the equivalent first-class override is
  `CustomActModel.CustomCreateMap(RunState, bool replaceTreasureWithElites)` returning an `ActMap?`
  (BaseLib citation above in §1) — prefer this over the raw Harmony hook when the act is already a
  `CustomActModel`, since it's a supported extension point rather than a patch of internal engine code.
- Room *instantiation* for map point types still goes through `RunManager.CreateRoom(RoomType, MapPointType)`,
  patched with a prefix that special-cases `RoomType.Boss`/`MapPointType.Boss` to force the custom boss
  encounter and `RoomType.Treasure` to use an Act-4-specific `TreasureRoom` variant, falling through
  (`return true`) to vanilla for every other room type including Rest Site and Shop —
  [`Patches/RunManagerCreateRoomAct4TreasurePatch.cs:16-34`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Patches/RunManagerCreateRoomAct4TreasurePatch.cs#L16-L34).
  I.e. **Rest Site and Shop rooms need no special construction hook at all** — the vanilla
  `RunManager.CreateRoom` already handles any `MapPointType.RestSite` / `MapPointType.Shop` node
  correctly once it exists in the `ActMap` graph; only content that doesn't exist in the base game
  (the boss encounter, an Act-4-flavored treasure room) needs a `CreateRoom` override.
- A currently-unresolved base-game quirk: `NRestSiteCharacter._Ready()` throws
  `InvalidOperationException("Unexpected act")` for `CurrentActIndex == 3` because its internal switch
  only handles acts 0–2, requiring a `[HarmonyFinalizer]` to swallow the exception and manually finish
  the visual setup that the throw aborted —
  [`Patches/NRestSiteCharacterReadyAct4Patch.cs:18-72`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Patches/NRestSiteCharacterReadyAct4Patch.cs#L18-L72).
  This is a **confirmed gap/risk for any 4th act, regardless of hook design**: the Rest Site screen's
  character presentation logic is hard-coded to 3 acts and needs its own patch to avoid a crash when a
  Rest Site is reached in an act index ≥ 3.

## 3. Enter a custom encounter

**Two confirmed, non-exclusive mechanisms:**

**(a) BaseLib's `CustomEncounterModel`** — abstract subclass of `MegaCrit.Sts2.Core.Models.EncounterModel`,
auto-registered the same way as acts:

> ```csharp
> protected CustomEncounterModel(RoomType roomType, bool autoAdd = true)
> {
>     if (roomType is not (RoomType.Monster or RoomType.Elite or RoomType.Boss))
>         BaseLibMain.Logger.Warn($"Encounter {Id.Entry} sets unexpected room type {roomType}");
>     RoomType = roomType;
>     if (autoAdd) CustomContentDictionary.AddEncounter(this);
> }
> ```
> — [`Abstracts/CustomEncounterModel.cs:14-27`](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Abstracts/CustomEncounterModel.cs#L14-L27)

Required overrides (per the class's own doc comment) are `AllPossibleMonsters` and `GenerateMonsters()`;
optional overrides cover scene path, background, boss map icon path (`BossNodePath`), tags, and
weak/early-fight flags —
[`CustomEncounterModel.cs:36-65,144-149`](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Abstracts/CustomEncounterModel.cs#L36-L65).
An `IsValidForAct(ActModel act)` abstract method is the documented way to scope the encounter to a
specific (custom) act rather than adding it to that act's normal encounter list —
[`CustomEncounterModel.cs:29-35`](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Abstracts/CustomEncounterModel.cs#L29-L35).

**(b) A direct `EncounterModel` subclass with no BaseLib wrapper at all** — this is what Act4FinalAscent
actually ships, showing the BaseLib wrapper is convenience, not a requirement:

> ```csharp
> public sealed class Act4ArchitectBossEncounter : EncounterModel
> {
>     public override RoomType RoomType => (RoomType)3;              // Boss
>     public override string BossNodePath => "res://images/map/placeholder/act4_architect_icon";
>     public override string CustomBgm => "event:/music/act3_boss_test_subject";
>     public override IEnumerable<MonsterModel> AllPossibleMonsters => new MonsterModel[] { ... };
>     protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
>         => new (MonsterModel, string)[1] { (ModelDb.Monster<Act4ArchitectBoss>().ToMutable(), null) };
> }
> ```
> — [`src/Act4Placeholder/Architect/Act4ArchitectBossEncounter.cs:12-38`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Architect/Act4ArchitectBossEncounter.cs#L12-L38)

"Entering" the encounter is then just forcing a `CombatRoom` for that `EncounterModel` — confirmed by
the same `RunManager.CreateRoom` prefix cited in §2:

> ```csharp
> if (roomType == RoomType.Boss || mapPointType == MapPointType.Boss)
> {
>     __result = new CombatRoom(ModelDb.Encounter<Act4ArchitectBossEncounter>().ToMutable(), state);
>     return false;
> }
> ```
> — [`RunManagerCreateRoomAct4TreasurePatch.cs:23-27`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Patches/RunManagerCreateRoomAct4TreasurePatch.cs#L23-L27)

`CombatRoom(EncounterModel, RunState)` is therefore the confirmed constructor shape for entering any
encounter — custom or vanilla — outside of normal map-node traversal (e.g. forced/debug entry), and is
also how the vanilla path enters combat once a player travels onto a `Monster`/`Elite`/`Boss` map node,
since `RunManager.CreateRoom` is the single choke point patched here. A companion patch also confirms
`ActModel.SetBossEncounter(EncounterModel)` / `SetSecondBossEncounter(EncounterModel?)` are the
supported mutators for wiring an encounter onto an act as *the* boss (§1's `AppendAct4Placeholder`
snippet), separate from map-node placement.

## 4. Terminate the run after either win or loss

**Confirmed hook: `RunManager.OnEnded(bool isVictory)` → `SerializableRun`, feeding
`NRun.Instance.ShowGameOverScreen(SerializableRun)`.**

Win path, driven from boss-defeat mechanics code directly (not a patch — an ordinary method call):

> ```csharp
> internal static async Task FinishRunAfterAct4BossAsync(RunManager runManager)
> {
>     using NetLoadingHandle _ = new NetLoadingHandle(runManager.NetService);
>     await ShowPinnedFullscreenTextAsync("THE ARCHITECT FALLS", 2f);
>     MarkAct4BossVictory(runManager.DebugOnlyGetState());
>     SerializableRun serializableRun = runManager.OnEnded(true);
>     NRun.Instance?.ShowGameOverScreen(serializableRun);
>     await Cmd.Wait(0.1f, false);
> }
> ```
> — [`ModSupport.cs:2886-2895`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Core/ModSupport.cs#L2886-L2895),
> invoked from boss-death mechanics at
> [`Architect/Act4ArchitectBossMechanics.cs:1358`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Architect/Act4ArchitectBossMechanics.cs#L1358).

Loss/normal-ending path is a Harmony patch on the same method, letting the mod override the
`isVictory` flag before the base game's own ending logic runs:

> ```csharp
> [HarmonyPatch(typeof(RunManager), "OnEnded")]
> internal static class RunManagerOnEndedPatch
> {
>     private static void Prefix(RunManager __instance, ref bool isVictory)
>     {
>         Act4Settings.ResetForNewRun();
>         RunState runState = __instance.DebugOnlyGetState();
>         if (!isVictory && ModSupport.ShouldTreatCurrentAct4BossRoomAsVictory(runState))
>         {
>             ModSupport.MarkAct4BossVictory(runState);
>             isVictory = true;
>         }
>         else if (!isVictory && ModSupport.IsAct4Placeholder(runState))
>         {
>             ModSupport.RecordAct4EnteredWithoutVictory(runState);
>             isVictory = true;   // counted as a base-game win; mod's own stats track the Act-4 loss separately
>         }
>     }
> }
> ```
> — [`Patches/RunManagerOnEndedPatch.cs:13-30`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Patches/RunManagerOnEndedPatch.cs#L13-L30)

This confirms:
- `RunManager.OnEnded(bool isVictory)` is the single termination choke point for **both** win and loss —
  there's no separate "lose" method; both routes funnel through this call with a boolean.
- It's Harmony-patchable (`Prefix(ref bool isVictory)`), so a mod can flip a would-be loss into a
  recorded win (or vice versa) before the game commits stats/achievements, and can read/mutate
  `RunManager.DebugOnlyGetState()` (the live `RunState`) at that instant for last-moment bookkeeping.
- `NRun.Instance?.ShowGameOverScreen(SerializableRun)` is the confirmed call that actually surfaces the
  end-of-run UI once `OnEnded` has produced the serialized result — i.e. "terminate the run" is a
  two-step `OnEnded(...)` → `ShowGameOverScreen(...)` sequence, not implicit.
- Separately, `MegaCrit.Sts2.Core.Saves.ScoreUtility.CalculateScore` has two overloads
  (`(IRunState, ulong, bool)` / `(IRunState, bool)`, and matching `SerializableRun` overloads) both
  patched with a version-tolerant `AccessTools.Method(...) ?? AccessTools.Method(...)` fallback lookup
  to survive a game-beta signature change —
  [`Patches/ScoreUtilityCalculateScorePatch.cs:14-46`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Patches/ScoreUtilityCalculateScorePatch.cs#L14-L46) —
  concrete, dated evidence that `RunManager`/`ScoreUtility` method signatures are not stable across game
  versions and any hook here needs defensive reflection or a version pin (see Risks).

## 5. Capture a successor snapshot without corrupting normal run bookkeeping

**Confirmed pattern: serialize the run through the normal save path (`RunManager.ToSave`), wrap the
result in a `RunHistory` tagged with a mod-owned marker, and persist it to a private mod-owned file —
never touching the game's own save/progress files.**

> ```csharp
> internal static void SaveAct3SnapshotFromRunState(RunState? runState)
> {
>     RunManager instance = RunManager.Instance;
>     SerializableRun serializableRun = instance.ToSave(null);
>     ApplyAct4SaveMarkers(serializableRun, runState);
>     RunHistory runHistory = CreateRunHistoryFromSerializable(serializableRun, victory: true, isAbandoned: false);
>     if (runHistory.Acts == null || runHistory.Acts.Count == 0) return;
>     string key = BuildAct3SnapshotKey(runHistory.StartTime, runHistory.Seed, runHistory.Ascension, runHistory.Players?.Count ?? 0);
>     Act3SnapshotsByKey[key] = runHistory;
>     PersistAct3Snapshots();     // mod-owned JSON file, not the game's save slot
> }
> ```
> — [`ModSupport.cs:997-1017`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Core/ModSupport.cs#L997-L1017),
> called from the Act-4 transition entry point at
> [`ModSupport.cs:2957`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Core/ModSupport.cs#L2957)
> (`ProceedToAct4Async`, run *before* `AppendAct4Placeholder` mutates `RunState.Acts`).

Key facts this establishes for a "successor" snapshot (e.g. capturing this run's deck/relics/state to
seed a future "past self" boss, which is this repo's stated premise):
- `RunManager.ToSave(AbstractRoom? currentRoom)` → `SerializableRun` is the confirmed, already-used
  serialization entry point — it's the same method the game's own save-and-quit path uses
  (`RunManager.ToSave` is Harmony-patched directly for marker injection in
  [`Patches/Act4SaveStatePatches.cs:15-24`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Patches/Act4SaveStatePatches.cs#L15-L24)),
  so capturing a snapshot via `ToSave` is guaranteed to reflect the same shape of data the game itself
  persists (deck, relics, potions, map history, ascension, seed, players) — see the field mapping in
  `CreateRunHistoryFromSerializable`
  ([`ModSupport.cs:1058-1088`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Core/ModSupport.cs#L1058-L1088)),
  which explicitly copies `Deck`, `Relics`, `Potions`, `MaxPotionSlotCount` per player plus
  `MapPointHistory`, `Ascension`, `Seed`, `Acts`.
- The snapshot is stamped with a mod-private marker (`BuildId = "ACT4_PLACEHOLDER_ACT3_SNAPSHOT"`,
  [`ModSupport.cs:1082`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Core/ModSupport.cs#L1082))
  and keyed by a composite of `(startTime, seed, ascension, playerCount)` rather than being written into
  the base game's own run-history slot — it's stored in the mod's **own** in-memory dictionary
  (`Act3SnapshotsByKey`) and persisted to the mod's own JSON file via `PersistAct3Snapshots()`
  (pattern mirrored by `PersistAct4BossVictories`, `PersistBookChoices`, etc., all writing to
  `ResolveAccountScopedPath`/`ResolveProfileScopedPath`-computed files under the mod's own directory —
  [`ModSupport.cs:2559-2586`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Core/ModSupport.cs#L2559-L2586)).
  This is the concrete mechanism for "without corrupting normal run bookkeeping": the snapshot never
  goes through `ProgressSaveManager`/`SaveManager` at all for storage, only reads `RunManager.ToSave`
  for its data shape.
- `ApplyAct4SaveMarkers`/`RestoreAct4FlagsFromSave`, patched onto the *real* save/load path
  (`RunManager.ToSave` postfix and `RunState.FromSerializable` postfix — both **additive, non-destructive
  postfixes**, never prefixes that replace behavior), show the safe way to piggyback mod-only flags onto
  the actual save file when that *is* desired: mutate the already-produced `SerializableRun`/`RunState`
  result afterward rather than intercepting/replacing the base serialization —
  [`Act4SaveStatePatches.cs:15-40`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Patches/Act4SaveStatePatches.cs#L15-L40).
- A related, explicitly-flagged base-game gap: `ProgressSaveManager.ObtainCharUnlockEpoch` has **no**
  case for `act == 3`, logging "Act 4 is not yet implemented"/"EpochModel was not found" errors on every
  Act-4 boss kill; the mod silences this with a `Prefix` returning `false` for `act == 3` —
  [`Act4SaveStatePatches.cs:56-70`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Patches/Act4SaveStatePatches.cs#L56-L70).
  Any Act-4 mod capturing a "run completed" epoch/unlock needs to account for this gap explicitly —
  the base game's own progress-epoch bookkeeping does not expect a 4th act to exist.
- `ProgressSaveManager.LoadProgress` is separately post-patched to detect and silently rewrite a
  corrupted/stale `progress.save` (comparing a normalized re-serialization against the loaded data) —
  [`Act4SaveStatePatches.cs:42-54`](https://github.com/kphxgames/Act4FinalAscent/blob/05c251a4186b323fc2a7fef5dab3cf586b856767/src/Act4Placeholder/Patches/Act4SaveStatePatches.cs#L42-L54).
  Useful precedent for "don't corrupt bookkeeping": the mod author felt the need for defensive
  self-healing around the account-level progress file, implying Act-4-adjacent save mutations have
  caused at least transient corruption in practice for this prior mod.

---

## Summary table

| # | Goal | Confirmed hook(s) | Source |
|---|------|--------------------|--------|
| 1 | Fixed Act after Act 3 | Prefix `EventModel.SetEventState` for vanilla `TheArchitect` at act index 2; `BaseLib.Abstracts.CustomActModel(actNumber, autoAdd)` defines content; append to a live run by writing private `RunState.<Acts>k__BackingField` (no public mutator), call `ActModel.GenerateRooms`, then use normal `EnterNextAct` | BaseLib `Abstracts/CustomActModel.cs:33-40`; Act4FinalAscent `EventModelSetEventStatePatch.cs`, `ModSupport.cs:2792-2809`, `RunManagerEnterNextActPatch.cs` |
| 2 | Rest Site → Shop → boss path | Custom `MegaCrit.Sts2.Core.Map.ActMap` subclass (`MapPoint`/`AddChildPoint` graph, named `MapPointType.RestSite`/`Shop`/`Boss`), installed via `CustomActModel.CustomCreateMap` (BaseLib, preferred) or `Hook.ModifyGeneratedMap` postfix; room instantiation for Rest Site/Shop needs no override, only Boss/Treasure do (`RunManager.CreateRoom` prefix) | BaseLib `Abstracts/CustomActModel.cs:158-172`; Act4FinalAscent `Map/ShortAct4Map.cs`, `Patches/HookModifyGeneratedMapPatch.cs`, `Patches/RunManagerCreateRoomAct4TreasurePatch.cs` |
| 3 | Enter a custom encounter | `BaseLib.Abstracts.CustomEncounterModel` (wrapper) or a bare `MegaCrit.Sts2.Core.Models.EncounterModel` subclass; entered via `new CombatRoom(EncounterModel, RunState)` from `RunManager.CreateRoom`; wired onto an act via `ActModel.SetBossEncounter`/`SetSecondBossEncounter` | BaseLib `Abstracts/CustomEncounterModel.cs:14-27`; Act4FinalAscent `Architect/Act4ArchitectBossEncounter.cs`, `Patches/RunManagerCreateRoomAct4TreasurePatch.cs:23-27` |
| 4 | Terminate run on win or loss | `RunManager.OnEnded(bool isVictory) → SerializableRun`, Harmony-patchable via `Prefix(ref bool isVictory)`; finish with `NRun.Instance.ShowGameOverScreen(SerializableRun)` | Act4FinalAscent `ModSupport.cs:2886-2895`, `Patches/RunManagerOnEndedPatch.cs:13-30` |
| 5 | Successor snapshot, no bookkeeping corruption | `RunManager.ToSave(AbstractRoom?) → SerializableRun`, wrapped in a `RunHistory` tagged with a mod-private `BuildId` marker and persisted to a mod-owned file (never the game's save slot); additive postfixes on `ToSave`/`RunState.FromSerializable` for any flags that *do* need to live in the real save | Act4FinalAscent `ModSupport.cs:997-1017,1058-1088`, `Patches/Act4SaveStatePatches.cs` |

## Gaps and uncertainties

- **No local `sts2.dll`/`0Harmony.dll`/BaseLib assembly and no decompiled build in this workspace or
  machine.** Searched the entire filesystem for `sts2.dll`, `0Harmony.dll`, `BaseLib*` — none found.
  Every method/type/enum-member name above is corroborated by at least one of (a) BaseLib source
  compiled against the real assembly, (b) Act4FinalAscent source (a shipping mod, implying it compiles
  and runs against a real game build), and where noted (c) cross-referenced against independent
  decompiled mirrors purely for name/signature existence, not behavior. **None of this has been
  compiled or run against the actual game in this workspace.** Before relying on any of this in
  production code, build against the real `sts2.dll` referenced via `Sts2PathDiscovery.props` and
  verify these signatures still match — game version drift is already evidenced (see below).
- **Act4FinalAscent is third-party demonstration code, not a documented/supported API contract.** Its
  README states it targets a specific beta build and was built in ~3 weeks; its own code shows at least
  one confirmed cross-version signature change already handled defensively
  (`ScoreUtility.CalculateScore`, `Patches/ScoreUtilityCalculateScorePatch.cs:14-24`) and at least one
  base-game bug it works around rather than a sanctioned hook (`NRestSiteCharacter._Ready` throwing for
  `CurrentActIndex == 3`). Treat every signature/behavior here as **needing re-verification** against
  this repo's exact `min_game_version` (`TheArchitect.json:8` currently pins `"0.107.0"`).
- **The `RunState.Acts` append mechanism (§1) uses reflection on a private backing field
  (`<Acts>k__BackingField`) with no public alternative found.** This is the single highest-risk hook
  identified: it is not an intentional extension point, it depends on the C# compiler's
  auto-property-backing-field naming convention holding across game versions, and it silently breaks if
  Mega Crit ever manually implements the `Acts` property or adds a public appender. No lower-risk
  alternative was found in either BaseLib or Act4FinalAscent — BaseLib's `CustomActModel` only covers
  *defining* an act, not *appending one to a specific live run*. The vanilla `TheArchitect` event
  provides the transition trigger but, in the working precedent, still does not provide an act-list
  mutator. Re-check the real assembly on every supported game version and fail loudly if the backing
  field cannot be resolved.
- **Exact `MapPointType`/`RoomType` full enum member lists were not independently confirmed** — only the
  members actually referenced by name across BaseLib and Act4FinalAscent were observed:
  `RoomType.{Monster, Elite, Boss, Treasure}` and `MapPointType.{Monster, Elite, Boss, Treasure, RestSite,
  Shop, Unknown}`. Other members (e.g. an explicit "Rest"/"Event"/"Start" distinct from `Unknown`) may
  exist; `ShortAct4Map.cs` uses raw numeric casts (`(MapPointType)7`, `(MapPointType)8`) for its boss and
  starting points instead of named constants, which this note cannot explain from source alone (possibly
  just author style, possibly those specific ordinals lack a convenient named alias at the point of
  writing) — re-verify the full enum against the real assembly rather than assuming completeness here.
- **Multiplayer/co-op synchronization concerns** (checksum divergence, `ActionQueueSynchronizer`,
  `ClientLoadJoinResponseMessage` state sync) are extensively handled in Act4FinalAscent but were only
  skimmed, not analyzed in depth, since the research question did not ask about multiplayer. If this
  repo's mod supports co-op, revisit `Patches/ClientLoadJoinSyncPatch.cs` and
  `Patches/AdminSkipToAct3Action.cs`'s checksum-suppression pattern before shipping.
- **No official Mega Crit modding documentation was found or consulted** beyond the BaseLib wiki stub
  page for `CustomActModel` (https://github.com/Alchyr/BaseLib-StS2/blob/master/Abstracts/CustomActModel.cs
  — the wiki page itself is just a pointer back to source, confirmed by fetching
  https://alchyr.github.io/BaseLib-Wiki/ and https://github.com/Alchyr/BaseLib-Wiki/blob/main/docs/models/custom-act.md's
  listing). No first-party "Slay the Spire 2 modding API" reference beyond the template wiki
  (https://github.com/Alchyr/ModTemplate-StS2/wiki) was located; that template wiki was not exhaustively
  crawled in this pass.
