using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Runs;
using TheArchitect.TheArchitectCode.Acts;
using TheArchitect.TheArchitectCode.Encounters;
using TheArchitect.TheArchitectCode.Persistence;

namespace TheArchitect.TheArchitectCode.Lifecycle;

public static class ArchitectLifecycle
{
    private static readonly MethodInfo SetActs = AccessTools.PropertySetter(typeof(RunState), nameof(RunState.Acts))
        ?? throw new MissingMethodException("The beta RunState.Acts setter changed.");

    public static bool IsEncounter([NotNullWhen(true)] IRunState? run) =>
        run is { Players.Count: 1, CurrentRoom: CombatRoom { Encounter: ArchitectEncounter } };

    public static void AppendAct(RunState run)
    {
        if (run.Players.Count != 1 || run.Acts.Any(act => act is ArchitectAct))
            return;
        if (run.Acts.Count != 3)
            throw new InvalidOperationException("The Architect requires an ordinary three-act single-player run.");
        var act = (ArchitectAct)ArchitectModels.Act.ToMutable();
        act.InitializeRooms();
        SetActs.Invoke(run, [run.Acts.Append(act).ToArray()]);
    }

    public static void FinishVictory()
    {
        var run = RunManager.Instance.DebugOnlyGetState();
        if (!IsEncounter(run) || ArchitectRun.Get(run).Outcome != null)
            return;
        var save = RunManager.Instance.OnEnded(true);
        NRun.Instance?.ShowGameOverScreen(save);
    }
}

[HarmonyPatch(typeof(ActModel), nameof(ActModel.GenerateRooms))]
internal static class ArchitectRoomsPatch
{
    private static bool Prefix(ActModel __instance)
    {
        if (__instance is not ArchitectAct act)
            return true;
        act.InitializeRooms();
        return false;
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.WinRun))]
internal static class EnterArchitectActPatch
{
    private static bool Prefix(RunManager __instance, ref Task __result)
    {
        var run = __instance.DebugOnlyGetState();
        if (run is not { CurrentActIndex: 2, Players.Count: 1, Acts.Count: 3,
                CurrentRoom: EventRoom { CanonicalEvent: MegaCrit.Sts2.Core.Models.Events.TheArchitect } } ||
            __instance.IsAbandoned)
            return true;
        ArchitectLifecycle.AppendAct(run);
        // The event's callback must return before its room can be exited.
        __instance.ActChangeSynchronizer.SetLocalPlayerReady();
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetActInternal))]
internal static class CaptureCorruptedPlayerAtEntryPatch
{
    private static void Prefix(RunManager __instance, int actIndex)
    {
        var run = __instance.DebugOnlyGetState();
        if (run?.Acts[actIndex] is ArchitectAct && run.Players.Count == 1)
            ArchitectRun.Get(run).Enter();
    }
}

[HarmonyPatch(typeof(NCombatUi), "OnCombatWon")]
internal static class FinishArchitectWithoutRewardsPatch
{
    private static bool Prefix(CombatRoom room)
    {
        if (!ArchitectLifecycle.IsEncounter(room.CombatState.RunState))
            return true;
        // Let the engine finish its combat-end notifications before opening the native summary.
        Callable.From(ArchitectLifecycle.FinishVictory).CallDeferred();
        return false;
    }
}

[HarmonyPatch(typeof(CombatRoom), "StartPreFinishedCombat")]
internal static class ResumeFinishedArchitectPatch
{
    private static bool Prefix(CombatRoom __instance, ref Task __result)
    {
        if (!ArchitectLifecycle.IsEncounter(__instance.CombatState.RunState))
            return true;
        Callable.From(ArchitectLifecycle.FinishVictory).CallDeferred();
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded))]
internal static class ArchitectOutcomePatch
{
    private static void Prefix(RunManager __instance, ref bool isVictory)
    {
        var run = __instance.DebugOnlyGetState();
        if (!ArchitectLifecycle.IsEncounter(run) || __instance.IsAbandoned)
            return;
        var state = ArchitectRun.Get(run);
        if (state.Outcome == null)
        {
            state.Outcome = isVictory ? "ArchitectWin" : "ArchitectLoss";
            if (__instance.ShouldSave)
                state.SnapshotRevision = CorruptedPlayerStore.Commit(state.Id, state.Outcome,
                    CorruptedPlayerSnapshot.Capture(run.Players[0]));
        }
        isVictory = true;
    }
}

[HarmonyPatch(typeof(AbstractRoom), nameof(AbstractRoom.IsVictoryRoom), MethodType.Getter)]
internal static class ArchitectVictoryRoomPatch
{
    private static void Postfix(AbstractRoom __instance, ref bool __result)
    {
        if (__instance is CombatRoom { Encounter: ArchitectEncounter } room &&
            ArchitectRun.Get(room.CombatState.RunState).Outcome != null)
            __result = true;
    }
}

[HarmonyPatch(typeof(ProgressSaveManager), "ObtainCharUnlockEpoch")]
internal static class ArchitectCharacterEpochPatch
{
    // Act 3 already awarded the final character epoch; the beta has no Act 4 epoch.
    private static bool Prefix(Player localPlayer, int act) =>
        act != 3 || !ArchitectLifecycle.IsEncounter(localPlayer.RunState);
}

[HarmonyPatch(typeof(RunState), nameof(RunState.IsGameOver), MethodType.Getter)]
internal static class ArchitectGameOverPatch
{
    private static void Postfix(RunState __instance, ref bool __result)
    {
        if (ArchitectLifecycle.IsEncounter(__instance) && ArchitectRun.Get(__instance).Outcome != null)
            __result = true;
    }
}

public sealed class ArchitectConsoleCommand : AbstractConsoleCmd
{
    public override string CmdName => "architect";
    public override string Args => "";
    public override string Description => "Playtest Act 4 from the current single-player build, including The Unwritten.";
    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length != 0 || issuingPlayer?.RunState is not RunState { Players.Count: 1 } run)
            return new CmdResult(false, "Use 'architect' during a single-player run.");
        if (run.Act is ArchitectAct)
            return new CmdResult(false, "You are already in the Architect Act.");
        ArchitectLifecycle.AppendAct(run);
        NMapScreen.Instance?.SetTravelEnabled(true);
        return new CmdResult(RunManager.Instance.EnterAct(3), true, "Entering Act 4 - The Architect.");
    }

    [HarmonyPatch(typeof(RoomSet), nameof(RoomSet.FromSave))]
    internal static class ArchitectEmptyRoomSetPatch
    {
        private static void Prefix(SerializableRoomSet save)
        {
            if (save.BossId != ArchitectModels.EncounterId)
                return;
            // Native JSON omits empty collections, but RoomSet.FromSave enumerates them.
            save.EventIds ??= [];
            save.NormalEncounterIds ??= [];
            save.EliteEncounterIds ??= [];
        }
    }
}
