# Installed beta lifecycle integration

This supplements the earlier lifecycle and persistence research with facts from
the installed Steam **public-beta v0.111.0**, commit `41cef1ea`, build
`24724944`, and **BaseLib 3.4.5**. It documents the initial playable slice, not
completion of the crash-consistency requirements in issue #12.

## Source convention

The primary sources are the installed `sts2.dll` and BaseLib 3.4.5 assemblies.
References below identify their decompiled namespace/file and method; line
numbers refer to this inspection and can change with the decompiler.
Game assemblies and decompiled game source are not redistributed in this repo.

## Act entry and fixed map

- Preserve the vanilla Act 3 Architect event. Its final callback awaits
  `RunManager.WinRun()`. Intercept that call only for the ordinary three-act,
  single-player route, append the custom act, and queue
  `ActChangeSynchronizer.SetLocalPlayerReady()`. Do not await the transition in
  the event callback: `EventRoom.Exit()` awaits pending option tasks.
  Sources: `MegaCrit.Sts2.Core.Runs/RunManager.cs`, `WinRun` and `EnterNextAct`
  (1308-1341); `MegaCrit.Sts2.Core.Rooms/EventRoom.cs`, `Exit` (90-102);
  `MegaCrit.Sts2.Core.Multiplayer.Game/ActChangeSynchronizer.cs` (38-45, 80-89).
- `RunState.Acts` has a private setter. The integration resolves that setter
  explicitly rather than relying on a compiler-generated field.
  Source: `MegaCrit.Sts2.Core.Runs/RunState.cs`, `Acts`.
- BaseLib's `CustomActModel(int actNumber, bool autoAdd = true)` supports an
  explicit, non-auto-added fourth act. Its map hook is **protected virtual**
  `CustomCreateMap(RunState, bool)`, and a non-null map replaces vanilla
  generation. Source: `BaseLib.Abstracts/CustomActModel.cs`
  (27-38, 316-323, 381-384).
- Starting and boss map points are separate from the interior grid. The slice
  uses a fixed Rest Site, one normal Shop, and the custom Boss.
  Source: `MegaCrit.Sts2.Core.Nodes.Screens.Map/NMapScreen.cs`, map construction;
  implementation: `TheArchitectCode/Acts/ArchitectAct.cs`.
- Vanilla rest-character animation selection assumes three act indexes; only
  that visual lookup is mapped to Glory for the custom act.
  Source: `MegaCrit.Sts2.Core.Nodes.RestSite/NRestSiteCharacter.cs`, `_Ready`;
  implementation: `TheArchitectCode/Lifecycle/ArchitectRestVisuals.cs`.

## Combat completion and presentation

- The win path finishes combat hooks, marks the room prefinished, saves it,
  updates combat progression, and then invokes `CombatWon`. Suppress
  `NCombatUi.OnCombatWon(CombatRoom)` for this encounter and defer the custom
  finalizer until engine notifications finish. `ShouldGiveRewards = false`
  alone is insufficient: the UI still schedules progression.
  Sources: `MegaCrit.Sts2.Core.Combat/CombatManager.cs`, `EndCombatInternal`
  (1300-1362); `MegaCrit.Sts2.Core.Nodes.Combat/NCombatUi.cs` (363-378).
- Loss calls `RunManager.OnEnded(false)` inside `CreatureCmd.Kill`. Capture the
  actual outcome before forcing the base-game victory flag. Exclude abandon
  and multiplayer. Source: `MegaCrit.Sts2.Core.Commands/CreatureCmd.cs`
  (476-489).
- Terminal outcomes open the native `NRun.ShowGameOverScreen(SerializableRun)`
  directly, without an intermediate custom dialog. The game-over and
  victory-room overrides are scoped to a terminal Architect encounter.
  Source: `MegaCrit.Sts2.Core.Nodes/NRun.cs`, `ShowGameOverScreen`;
  implementation: `TheArchitectCode/Lifecycle/ArchitectLifecycle.cs`.
- The beta has no fourth-act character epoch. Act 3 already awards the final
  character epoch, so skip only that unsupported lookup for this encounter,
  not the rest of combat or run progression.
  Source: `MegaCrit.Sts2.Core.Saves.Managers/ProgressSaveManager.cs`,
  `ObtainCharUnlockEpoch`.
- Killing a monster directly from a developer coroutine does not itself
  perform the normal action queue's win-condition check. The smoke harness
  explicitly calls `CombatManager.CheckWinCondition()` after its terminal kill.
  Source: `MegaCrit.Sts2.Core.Commands/CreatureCmd.cs`, `Kill` (461-509).

## Save boundary and snapshot persistence

Native combat saves record encounter identity/custom state and prefinished
status, not live combat piles, turns, or the active monster phase. Loading
reconstructs a fresh combat; `CombatRoom.Resume()` is unimplemented. Preserve
the entry Corrupted Player snapshot in the run extension and restart the same
encounter on nonterminal reload. Do not save a new encounter entry when the
Corrupted Player hands off to the Architect.

Sources: `MegaCrit.Sts2.Core.Rooms/CombatRoom.cs` (91-118, 158-180, 197-258).

The mod's profile-scoped snapshot uses native `SerializableCard` payloads,
an atomic replacement, a validated backup, and a terminal-run ID in the same
envelope as the revision. Retrying that run ID does not advance the snapshot
revision again. Native-card roundtrip, backup, and duplicate-commit behavior
are exercised by the opt-in smoke harness using a separate temporary file.

Sources: `MegaCrit.Sts2.Core.Saves/SaveManager.cs`, `GetProfileScopedPath`;
`TheArchitectCode/Persistence/CorruptedPlayerStore.cs`;
`TheArchitectCode/Playtest/ArchitectPlaytest.cs`.

## Remaining crash-consistency limitation

`RunManager.OnEnded` does not make progression, history, and current-run
deletion one local transaction. Progress is saved before history; history
write failures are caught, and the finalization guard is in memory. A returned
`OnEnded` call is therefore not proof of durable finalization.

Sources: `MegaCrit.Sts2.Core.Runs/RunManager.cs` (1623-1695);
`MegaCrit.Sts2.Core.Saves/SaveManager.cs` (753-763);
`MegaCrit.Sts2.Core.Saves.Managers/RunHistorySaveManager.cs` (53-69).

Snapshot idempotency alone does not solve this. A future terminal journal
needs the frozen outcome/run/successor snapshot, profile/run identity,
expected revision, and separate snapshot/finalization dispositions. Recovery
must not blindly call `OnEnded` again or assume an existing history file
proves completion. A write-ahead adapter around intended local save contents
is a possible direction, but requires implementation and fault injection.

Verified seams for that follow-up are `GodotFileIo.WriteFile(string, byte[])`,
`ProgressSaveManager.SaveProgress()`, and
`RunHistorySaveManager.SaveHistory(RunHistory)`. Sources:
`MegaCrit.Sts2.Core.Saves/GodotFileIo.cs` (93-117);
`MegaCrit.Sts2.Core.Saves.Managers/ProgressSaveManager.cs` (59-77, 224-247);
`MegaCrit.Sts2.Core.Saves.Managers/RunHistorySaveManager.cs` (53-69).
