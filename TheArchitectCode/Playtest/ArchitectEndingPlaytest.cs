using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using TheArchitect.TheArchitectCode.Acts;
using TheArchitect.TheArchitectCode.Lifecycle;
using TheArchitect.TheArchitectCode.Persistence;
using TheArchitect.TheArchitectCode.UI;
using EndingEvent = MegaCrit.Sts2.Core.Models.Events.TheArchitect;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class ArchitectEndingPlaytest
{
    internal static bool HoldVictoryForReload { get; private set; }

    internal static async Task Run(NGame game)
    {
        Require(NativeDemoSafety.Enabled, "ending probes require disposable storage and Steam disabled");
        game.GetWindow().Title = "The Architect - ISOLATED ENDING REVIEW";
        foreach (var loss in new[] { false, true })
        {
            var run = await game.StartNewSingleplayerRun(ModelDb.Character<Ironclad>(), true,
                [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()],
                [], "ARCHITECT-ENDING-" + loss, GameMode.Standard);
            await Exercise(run, loss, $"solo-{loss}");
            await game.ReturnToMainMenu();
        }
        foreach (var loss in new[] { false, true })
        {
            CharacterModel[] characters = [ModelDb.Character<Silent>(), ModelDb.Character<Defect>(),
                ModelDb.Character<Necrobinder>(), ModelDb.Character<Regent>()];
            var players = characters.Select((character, index) =>
                Player.CreateForNewRun(character, UnlockState.all, (ulong)index + 1)).ToArray();
            var run = RunState.CreateForNewRun(players,
                [ModelDb.Act<Overgrowth>().ToMutable(), ModelDb.Act<Hive>().ToMutable(), ModelDb.Act<Glory>().ToMutable()],
                [], GameMode.Standard, 0, "ARCHITECT-ENDING-PARTY-" + loss);
            RunManager.Instance.SetUpTest(run, new NetSingleplayerGameService(),
                disableCombatStateSync: true, shouldSave: false);
            RunManager.Instance.GenerateRooms();
            await PreloadManager.LoadRunAssets(characters);
            RunManager.Instance.Launch();
            game.RootSceneContainer.SetCurrentScene(NRun.Create(run));
            await Exercise(run, loss, $"party-{loss}");
            await game.ReturnToMainMenu();
        }
        MainFile.Logger.Info("ARCHITECT ENDING PASSED: Act 3 bypass; wins/losses; five characters; complete party binding; frozen terminal persistence.");
        game.GetTree().Quit();
    }

    private static async Task Exercise(RunState run, bool loss, string label)
    {
        MainFile.Logger.Info("ARCHITECT ENDING BEGIN: " + label);
        var manager = RunManager.Instance;
        await manager.EnterAct(2, doTransition: false);
        Require(run.Acts.Count == 3 && run.CurrentRoom is not EventRoom { CanonicalEvent: EndingEvent },
            "no ending before Act 3 handoff");
        // Use the native synchronized handoff, not the architect console shortcut.
        if (run.Players.Count == 1)
            manager.ActChangeSynchronizer.SetLocalPlayerReady();
        else
            await manager.EnterNextAct();
        await WaitFor(() => run.Act is ArchitectAct && NMapScreen.Instance?.IsOpen == true);
        Require(run.Acts.Count == 4 && ArchitectRun.Get(run).Outcome == null &&
            !ArchitectEnding.IsActive(run), "Act 3 enters Act 4 without the ending or terminal outcome");
        await manager.EnterMapCoord(run.Map.BossMapPoint.coord);
        await WaitFor(() => CombatManager.Instance.IsInProgress && !CombatManager.Instance.IsStarting);
        if (!loss && run.Players.Count > 1)
            await CreatureCmd.Kill(run.Players.Last().Creature, true);
        var revision = manager.ShouldSave ? CorruptedPlayerStore.Load()?.Revision ?? 0 : 0;
        if (loss)
            await CreatureCmd.Kill(run.Players.Select(player => player.Creature).ToArray(), true);
        else
        {
            HoldVictoryForReload = manager.ShouldSave;
            try
            {
                var combat = run.Players[0].Creature.CombatState!;
                for (var phase = 0; phase < 2 && combat.Enemies.Any(enemy => enemy.IsAlive); phase++)
                    await CreatureCmd.Kill(combat.Enemies.ToArray(), true);
                await CombatManager.Instance.CheckWinCondition();
                if (HoldVictoryForReload)
                {
                    await WaitFor(() => run.CurrentRoom is CombatRoom { IsPreFinished: true });
                    var saved = SaveManager.Instance.LoadRunSave().SaveData ??
                        throw new InvalidOperationException("Prefinished Architect save missing.");
                    Require(saved.PreFinishedRoom != null && ArchitectRun.Get(run).Outcome == null,
                        "prefinished victory is reloadable before finalization");
                    var game = NGame.Instance!;
                    await game.ReturnToMainMenu();
                    HoldVictoryForReload = false;
                    run = RunState.FromSerializable(saved);
                    await manager.SetUpSavedSingleplayer(run, saved);
                    await game.LoadRun(run, saved.PreFinishedRoom);
                }
            }
            finally
            {
                HoldVictoryForReload = false;
            }
        }

        await Complete(run, label);
        var outcome = loss ? "ArchitectLoss" : "ArchitectWin";
        Require(ArchitectRun.Get(run).Outcome == outcome, "binding does not change the combat outcome");
        if (manager.ShouldSave)
        {
            var saved = CorruptedPlayerStore.Load() ?? throw new InvalidOperationException("Ending snapshot missing.");
            Require(saved.Revision == revision + 1 && saved.Outcome == outcome &&
                saved.TerminalRunId == ArchitectRun.Get(run).Id, "one terminal snapshot, even after the animation");
        }
        var screen = NOverlayStack.Instance!.Peek();
        await manager.WinRun();
        Require(ReferenceEquals(screen, NOverlayStack.Instance.Peek()), "duplicate completion does not open another result");
        MainFile.Logger.Info("ARCHITECT ENDING CASE PASSED: " + label);
    }

    internal static async Task Complete(IRunState run, string label)
    {
        await WaitFor(() => ArchitectEnding.IsActive(run));
        await WaitFor(() => NCombatRoom.Instance is { Mode: CombatRoomMode.VisualOnly } room &&
            room.CreatureNodes.Count(node => node.Entity.IsPlayer) == run.Players.Count);
        await Task.Delay(2000);
        Require(NOverlayStack.Instance?.Peek() is not NGameOverScreen, "result waits for the ending");
        var hp = run.Players.Select(player => player.Creature.CurrentHp).ToArray();
        var snapshots = run.Players.Select(CorruptedPlayerSnapshot.Capture).ToArray();
        var binding = ObserveBinding();
        for (var step = 0; step < 10 && NOverlayStack.Instance?.Peek() is not NGameOverScreen; step++)
        {
            await WaitFor(() => run.CurrentRoom is EventRoom { LocalMutableEvent.CurrentOptions.Count: > 0 } ||
                NOverlayStack.Instance?.Peek() is NGameOverScreen);
            if (NOverlayStack.Instance?.Peek() is NGameOverScreen)
                break;
            await Task.Delay(1000);
            RunManager.Instance.EventSynchronizer.ChooseLocalOption(0);
            await RunManager.Instance.EventSynchronizer.AwaitPendingOptionTasks();
        }
        await binding;
        await WaitFor(() => NOverlayStack.Instance?.Peek() is NGameOverScreen);
        Require(run.Players.Select(player => player.Creature.CurrentHp).SequenceEqual(hp) &&
            run.Players.Select(player => CorruptedPlayerSnapshot.Capture(player).ContentHash)
                .SequenceEqual(snapshots.Select(snapshot => snapshot.ContentHash)),
            "presentation neither revives/kills players nor changes the saved successor build");
        Require(run.CurrentRoom?.IsVictoryRoom == true, "native result remains a victory");

        async Task ObserveBinding()
        {
            await WaitFor(() => PartyNodes().All(node =>
                node.Visuals.GetNodeOrNull<CorruptedPlayerCorruption>("BoundEcho") != null));
            await Task.Delay(900);
            await NGame.Instance!.AwaitProcessFrame();
            if (NativeDemoSafety.Enabled)
                await NativeDemoPlaytest.Capture("ending-" + label + "-binding");
            Require(NOverlayStack.Instance?.Peek() is not NGameOverScreen, "binding is visible before the result");
            foreach (var node in PartyNodes())
            {
                var front = node.Visuals.FindChild("BoundEchoFront", true, false) as CanvasItem;
                Require(front?.IsVisibleInTree() == true,
                    $"every party member is bound: {node.Entity.Player?.Character.Id}, " +
                    $"front={front?.GetPath()}, visible={front?.Visible}, parentVisible={node.Visuals.IsVisibleInTree()}, " +
                    $"bodyVisible={node.Body.Visible}, bodyParent={node.Body.GetParent().Name}");
                using var track = node.SpineAnimation.GetCurrentTrack();
                Require(track != null && !track.GetAnimationName().Contains("death", StringComparison.OrdinalIgnoreCase),
                    "bound players keep animating rather than playing death");
            }
        }

        NCreature[] PartyNodes() => run.Players.Select(player =>
            NCombatRoom.Instance?.GetCreatureNode(player.Creature) ??
            throw new InvalidOperationException("Ending party member missing.")).ToArray();
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(180);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Architect ending playtest timed out.");
            await NGame.Instance!.AwaitProcessFrame();
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Architect ending: " + message);
    }
}

[HarmonyPatch(typeof(ArchitectLifecycle), nameof(ArchitectLifecycle.FinishVictory))]
internal static class ArchitectEndingReloadProbePatch
{
    private static bool Prefix() => !ArchitectEndingPlaytest.HoldVictoryForReload;
}
