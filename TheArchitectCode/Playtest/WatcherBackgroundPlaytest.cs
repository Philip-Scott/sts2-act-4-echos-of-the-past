using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using TheArchitect.TheArchitectCode.Ancients;
using TheArchitect.TheArchitectCode.Lifecycle;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class WatcherBackgroundPlaytest
{
    internal static async Task Run(NGame game)
    {
        Require(NativeDemoSafety.Enabled && !NativeDemoSafety.SharedVisible,
            "Background layout probe requires a private, disposable instance.");
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
        var run = RunState.CreateForNewRun([player],
            [ModelDb.Act<Overgrowth>().ToMutable(), ModelDb.Act<Hive>().ToMutable(), ModelDb.Act<Glory>().ToMutable()],
            [], GameMode.Standard, 0, "ARCHITECT-WATCHER-BACKGROUND");
        var manager = RunManager.Instance;
        manager.SetUpTest(run, new NetSingleplayerGameService(), disableCombatStateSync: true, shouldSave: false);
        manager.GenerateRooms();
        ArchitectLifecycle.AppendAct(run);
        await PreloadManager.LoadRunAssets([player.Character]);
        manager.Launch();
        game.RootSceneContainer.SetCurrentScene(NRun.Create(run));
        await manager.EnterAct(3, doTransition: false);
        if (run.CurrentRoom is not EventRoom { CanonicalEvent: TheUnwritten })
            await RunManager.Instance.EnterMapCoord(run.Map.StartingMapPoint.coord);
        var layout = game.FindChildren("*", recursive: true, owned: false).OfType<NAncientEventLayout>().Single();
        var container = (NAncientBgContainer)AccessTools.Field(typeof(NAncientEventLayout), "_ancientBgContainer")
            .GetValue(layout)!;
        var image = container.GetNode<Control>("TheWatcher").GetNode<TextureRect>("ArchitectBackdrop");
        var window = game.GetWindow();
        foreach (var size in new[] { new Vector2I(1920, 1080), new Vector2I(1920, 1440),
                     new Vector2I(2520, 1080), new Vector2I(1920, 1080) })
        {
            // The private 1080p launcher otherwise keeps a fixed logical viewport while the OS window resizes.
            window.ContentScaleSize = size;
            window.Size = size;
            for (var frame = 0; frame < 4; frame++)
                await game.AwaitProcessFrame();
            var viewport = game.GetViewport().GetVisibleRect();
            var bounds = image.GetGlobalRect();
            Require(viewport.Size.IsEqualApprox(new Vector2(size.X, size.Y)),
                $"{size}: the logical viewport actually resized.");
            Require(container.Scale.IsEqualApprox(Vector2.One) &&
                container.Position.IsEqualApprox(Vector2.Zero), $"{size}: portrait transforms are neutral.");
            Require(bounds.Position.IsEqualApprox(viewport.Position) && bounds.Size.IsEqualApprox(viewport.Size),
                $"{size}: background fills the viewport ({bounds} vs {viewport}).");
            Require(image.StretchMode == TextureRect.StretchModeEnum.KeepAspectCovered &&
                image.ExpandMode == TextureRect.ExpandModeEnum.IgnoreSize,
                $"{size}: image covers the area without stretching its aspect ratio.");
            await NativeDemoPlaytest.Capture($"watcher-background-{size.X}x{size.Y}");
        }

        WatcherBackgroundLayout.Configure(container, fullBleed: false);
        Require(!container.Scale.IsEqualApprox(Vector2.One) &&
            !container.Position.IsEqualApprox(Vector2.Zero),
            "Reusing a layout for an ordinary Ancient restores its native portrait framing.");
        WatcherBackgroundLayout.Configure(container, fullBleed: true);
        Require(image.GetGlobalRect().Size.IsEqualApprox(game.GetViewport().GetVisibleRect().Size),
            "Switching back to Watcher restores full-screen coverage.");
        Require(run.CurrentRoom is EventRoom { LocalMutableEvent: TheUnwritten { IsFinished: false } },
            "The background probe leaves the Ancient offer unselected.");
        MainFile.Logger.Info("WATCHER BACKGROUND PASSED: full viewport at 4:3, 16:9 and 21:9; native framing restored for other Ancients.");
        game.GetTree().Quit();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Watcher background: " + message);
        MainFile.Logger.Info("WATCHER BACKGROUND PASS: " + message);
    }
}
