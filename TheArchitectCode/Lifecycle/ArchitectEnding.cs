using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Ancients;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using TheArchitect.TheArchitectCode.Acts;
using TheArchitect.TheArchitectCode.Persistence;
using TheArchitect.TheArchitectCode.UI;
using EndingEvent = MegaCrit.Sts2.Core.Models.Events.TheArchitect;

namespace TheArchitect.TheArchitectCode.Lifecycle;

internal static class ArchitectEnding
{
    private sealed class Presentation
    {
        public int Wins { get; init; }
        public Dictionary<ModelId, int> CharacterWins { get; init; } = [];
        public SerializableRun? Result { get; set; }
        public bool FinalAttack { get; set; }
        public Task? Completion { get; set; }
    }

    private static readonly ConditionalWeakTable<IRunState, Presentation> Presentations = new();

    internal static bool IsActive([NotNullWhen(true)] IRunState? run) =>
        run is { Act: ArchitectAct, CurrentRoom: EventRoom { CanonicalEvent: EndingEvent } } &&
        Presentations.TryGetValue(run, out var presentation) && presentation.Result != null;

    internal static void CaptureProgress(IRunState run)
    {
        var progress = SaveManager.Instance.Progress;
        Presentations.GetValue(run, _ => new Presentation
        {
            Wins = progress.Wins,
            CharacterWins = run.Players.Select(player => player.Character.Id).Distinct()
                .ToDictionary(id => id, id => progress.GetStatsForCharacter(id)?.TotalWins ?? 0)
        });
    }

    internal static bool DeferResult(SerializableRun result)
    {
        var run = RunManager.Instance.DebugOnlyGetState();
        if (!ArchitectLifecycle.IsEncounter(run) || RunManager.Instance.IsAbandoned ||
            ArchitectRun.Get(run).Outcome == null)
            return false;
        if (!Presentations.TryGetValue(run, out var presentation))
            throw new InvalidOperationException("Architect ending is missing its terminal progression state.");
        if (presentation.Result != null)
            return true;
        // Freeze the real combat result before creating any presentation-only event state.
        presentation.Result = result;
        Callable.From(() => { TaskHelper.RunSafely(Enter()); }).CallDeferred();
        return true;

        async Task Enter()
        {
            var manager = RunManager.Instance;
            if (!StillInRun(run))
                return;
            await manager.FadeOut();
            if (!StillInRun(run))
                return;
            NOverlayStack.Instance?.Clear();
            NCapstoneContainer.Instance?.Close();
            NMapScreen.Instance?.Close(animateOut: false);
            await manager.EnterRoom(new EventRoom(ModelDb.Event<EndingEvent>()));
            await manager.FadeIn();
        }
    }

    internal static AncientDialogue? SelectDialogue(EndingEvent ending)
    {
        var owner = ending.Owner ?? throw new InvalidOperationException("Architect ending has no owner.");
        var presentation = Presentations.GetValue(owner.RunState,
            _ => throw new InvalidOperationException("Architect ending has no captured progression."));
        var dialogues = ending.DialogueSet.GetValidDialogues(owner.Character.Id,
            presentation.CharacterWins[owner.Character.Id], presentation.Wins,
            allowAnyCharacterDialogues: false).ToList();
        return ending.Rng.NextItem(dialogues);
    }

    internal static void BeginFinalAttack(EndingEvent ending)
    {
        if (ending.Owner is { } owner && IsActive(owner.RunState) && LocalContext.IsMe(owner))
            Presentations.GetOrCreateValue(owner.RunState).FinalAttack = true;
    }

    internal static bool IsFinalAttack(EndingEvent ending) =>
        ending.Owner is { } owner && IsActive(owner.RunState) &&
        Presentations.TryGetValue(owner.RunState, out var presentation) && presentation.FinalAttack;

    internal static Task Complete(IRunState run)
    {
        var presentation = Presentations.GetValue(run,
            _ => throw new InvalidOperationException("Architect ending has no captured result."));
        return presentation.Completion ??= BindParty();

        async Task BindParty()
        {
            var scene = NRun.Instance ??
                throw new InvalidOperationException("Architect ending has no run presentation.");
            var room = NCombatRoom.Instance ??
                throw new InvalidOperationException("Architect ending has no combat presentation.");
            if (room.Mode != CombatRoomMode.VisualOnly)
                throw new InvalidOperationException("Architect binding requires the ending event's visual-only room.");
            foreach (var player in run.Players)
            {
                var node = room.GetCreatureNode(player.Creature) ??
                    throw new InvalidOperationException("Architect ending is missing a party member.");
                node.SetAnimationTrigger("Hit");
                CorruptedPlayerCorruption.Attach(node.Visuals, animateBinding: true, persistAfterDeath: true);
            }
            await Task.Delay(3000);
            if (!StillInRun(run) || !GodotObject.IsInstanceValid(scene))
                return;
            scene.ShowGameOverScreen(presentation.Result ??
                throw new InvalidOperationException("Architect ending lost its captured result."));
        }
    }

    private static bool StillInRun(IRunState run)
    {
        var manager = RunManager.Instance;
        if (ReferenceEquals(manager.DebugOnlyGetState(), run) && !manager.IsCleaningUp && !manager.IsAbandoned)
            return true;
        MainFile.Logger.Info("Architect ending cancelled after leaving its run.");
        return false;
    }
}

[HarmonyPatch(typeof(NRun), nameof(NRun.ShowGameOverScreen))]
internal static class DeferArchitectResultPatch
{
    private static bool Prefix(SerializableRun serializableRun) => !ArchitectEnding.DeferResult(serializableRun);
}

[HarmonyPatch]
internal static class ArchitectEndingDeadPlayerOptionsPatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.Method(AccessTools.Method(typeof(EventModel), nameof(EventModel.BeginEvent))
            .GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType, "MoveNext");

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var isDead = AccessTools.PropertyGetter(typeof(Creature), nameof(Creature.IsDead));
        var replacement = AccessTools.Method(typeof(ArchitectEndingDeadPlayerOptionsPatch), nameof(SkipEventForDeath));
        var count = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(isDead))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                count++;
            }
            yield return instruction;
        }
        if (count != 1)
            throw new InvalidOperationException("The native event initialization death check changed.");
    }

    // Change only the event-entry gate, never the creature's actual death state.
    private static bool SkipEventForDeath(Creature creature) =>
        creature.IsDead && !ArchitectEnding.IsActive(creature.Player?.RunState);
}

[HarmonyPatch(typeof(EndingEvent), "LoadDialogue")]
internal static class ArchitectEndingDialoguePatch
{
    private static bool Prefix(EndingEvent __instance, ref AncientDialogue? ____dialogue)
    {
        if (!ArchitectEnding.IsActive(__instance.Owner?.RunState))
            return true;
        ____dialogue = ArchitectEnding.SelectDialogue(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(EndingEvent), "WinRun")]
internal static class ArchitectEndingFinalAttackPatch
{
    private static void Prefix(EndingEvent __instance) => ArchitectEnding.BeginFinalAttack(__instance);
}

[HarmonyPatch(typeof(EndingEvent), "AnimArchitectAttackIfNecessary")]
internal static class ArchitectEndingAttackPatch
{
    private static void Prefix(EndingEvent __instance, ref ArchitectAttackers attackers)
    {
        if (ArchitectEnding.IsFinalAttack(__instance))
            attackers = ArchitectAttackers.Architect;
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.WinRun))]
internal static class BindArchitectSuccessorsPatch
{
    private static bool Prefix(RunManager __instance, ref Task __result)
    {
        var run = __instance.DebugOnlyGetState();
        if (!ArchitectEnding.IsActive(run) || __instance.IsAbandoned)
            return true;
        __result = ArchitectEnding.Complete(run);
        return false;
    }
}

[HarmonyPatch(typeof(NCreature), nameof(NCreature._Ready))]
internal static class ArchitectEndingPlayerVisualsPatch
{
    private static void Postfix(NCreature __instance)
    {
        if (__instance.Entity.Player is { } player && ArchitectEnding.IsActive(player.RunState))
            __instance.SetAnimationTrigger("Idle");
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.TriggerAnim))]
internal static class ArchitectEndingDeadPlayerAnimationPatch
{
    private static bool Prefix(Creature creature, string triggerName, float waitTime, ref Task __result)
    {
        if (creature.Player is not { } player || !creature.IsDead || !ArchitectEnding.IsActive(player.RunState))
            return true;
        var node = NCombatRoom.Instance?.GetCreatureNode(creature) ??
            throw new InvalidOperationException("Architect ending is missing a defeated player's visuals.");
        node.SetAnimationTrigger(triggerName);
        __result = Cmd.CustomScaledWait(Mathf.Min(waitTime * 0.5f, 0.25f), waitTime);
        return false;
    }
}
