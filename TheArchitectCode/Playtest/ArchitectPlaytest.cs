using HarmonyLib;
using Godot;
using System.IO;
using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using TheArchitect.TheArchitectCode.Acts;
using TheArchitect.TheArchitectCode.Challenger;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using TheArchitect.TheArchitectCode.Monsters;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using TheArchitect.TheArchitectCode.Encounters;
using TheArchitect.TheArchitectCode.Lifecycle;
using TheArchitect.TheArchitectCode.Persistence;

namespace TheArchitect.TheArchitectCode.Playtest;

// Explicit opt-in uses the game's unsaved bootstrap run, never the player's current run.
public sealed class ArchitectPlaytest : IBootstrapSettings
{
    public CharacterModel Character => ModelDb.Character<Ironclad>();
    public RoomType RoomType => CommandLineHelper.HasArg("architect-route") ? RoomType.Event : RoomType.Boss;
    public EncounterModel Encounter => ArchitectModels.Encounter;
    public EventModel Event => ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.TheArchitect>();
    public ActModel Act => ModelDb.Act<Glory>();
    public int Ascension => 0;
    public bool SaveRunHistory => false;
    public string Seed => "ARCHITECT-PLAYTEST";
    public bool DoPreloading => true;
    public bool BootstrapInMultiplayer => false;
    public List<ModifierModel> Modifiers => [];

    public async Task Setup(Player localPlayer)
    {
        var run = (RunState)localPlayer.RunState;
        if (CommandLineHelper.HasArg("architect-route"))
        {
            await RunManager.Instance.SetActInternal(2);
            _ = TaskHelper.RunSafely(SmokeRoute(localPlayer));
            return;
        }
        ArchitectLifecycle.AppendAct(run);
        await RunManager.Instance.SetActInternal(3);
        ArchitectRun.Get(run).EntrySnapshot = CommandLineHelper.HasArg("architect-repeat")
            ? new ChallengerEnvelope { Revision = 1, Snapshot = ChallengerSnapshot.Capture(localPlayer) }
            : null;
        if (CommandLineHelper.HasArg("architect-feedback"))
        {
            CardModel[] cards = [ModelDb.Card<PerfectedStrike>().ToMutable(), ModelDb.Card<Impervious>().ToMutable(),
                ModelDb.Card<StrikeIronclad>().ToMutable(), ModelDb.Card<StrikeIronclad>().ToMutable(),
                ModelDb.Card<StrikeIronclad>().ToMutable()];
            cards[0].UpgradeInternal();
            cards[1].UpgradeInternal();
            var deck = cards.Select(card => JsonSerializer.SerializeToElement(card.ToSerializable(),
                JsonSerializationUtility.GetTypeInfo<SerializableCard>())).ToArray();
            var original = ChallengerSnapshot.Capture(localPlayer);
            ArchitectRun.Get(run).EntrySnapshot = new ChallengerEnvelope
            {
                Revision = 1,
                Snapshot = original with
                {
                    Deck = deck,
                    ContentHash = ChallengerSnapshot.Hash(original.CharacterId, original.MaxHp, deck)
                }
            };
        }
        MainFile.Logger.Info("Architect playtest: unsaved first-visit encounter ready.");
        if (CommandLineHelper.HasArg("architect-smoke"))
            _ = TaskHelper.RunSafely(Smoke(localPlayer));
    }

    private static async Task SmokeRoute(Player player)
    {
        var run = player.RunState;
        await WaitFor(() => run.CurrentRoom is EventRoom);
        for (var step = 0; step < 10 && run.Acts.Count == 3; step++)
        {
            await WaitFor(() => run.CurrentRoom is EventRoom room && room.LocalMutableEvent.CurrentOptions.Count > 0);
            RunManager.Instance.EventSynchronizer.ChooseLocalOption(0);
            await RunManager.Instance.EventSynchronizer.AwaitPendingOptionTasks();
        }
        await WaitFor(() => run.Act is ArchitectAct && run.Map is ArchitectMap);
        MainFile.Logger.Info("Architect smoke: vanilla Act 3 event -> Act 4 passed.");
        await SmokeMapAndRooms(player);
    }

    internal static async Task SmokeMapAndRooms(Player player)
    {
        var run = player.RunState;
        var encounter = ArchitectModels.Encounter;
        if (ImageHelper.GetRoomIconPath(MegaCrit.Sts2.Core.Map.MapPointType.Boss, RoomType.Boss, encounter.Id)
                != encounter.CustomRunHistoryIconPath ||
            ImageHelper.GetRoomIconOutlinePath(MegaCrit.Sts2.Core.Map.MapPointType.Boss, RoomType.Boss, encounter.Id)
                != encounter.CustomRunHistoryIconOutlinePath)
            throw new InvalidOperationException("Run history must use the custom Architect silhouette.");
        await WaitFor(() => NMapScreen.Instance?.IsOpen == true);
        await Task.Delay(4000);
        await Capture(".map");
        var points = NMapScreen.Instance!.FindChildren("*", "", true, false).OfType<NMapPoint>().ToArray();
        if (points.Length != 3 || points.Any(point => !NGame.Instance!.GetViewport().GetVisibleRect()
                .Encloses(point.GetGlobalRect().Grow(24f))))
            throw new InvalidOperationException("All three Act 4 map nodes must fit on screen without scrolling: " +
                string.Join("; ", points.Select(point => $"{point.Name}: {point.GetGlobalRect()}")));
        var beforeScroll = points[0].GlobalPosition.Y;
        NMapScreen.Instance._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true });
        await Task.Delay(800);
        var afterScroll = points[0].GlobalPosition.Y;
        if (Math.Abs(afterScroll - beforeScroll) < 1f)
            throw new InvalidOperationException("The compact map must still respond to wheel scrolling.");
        await Task.Delay(800);
        if (Math.Abs(points[0].GlobalPosition.Y - afterScroll) > 1f)
            throw new InvalidOperationException("The compact map must stop scrolling after input stops.");
        await RunManager.Instance.EnterMapCoord(run.Map.StartingMapPoint.coord);
        if (run.CurrentRoom is not RestSiteRoom)
            throw new InvalidOperationException("Act 4 must start at a normal Rest Site.");
        await WaitFor(() => NRestSiteRoom.Instance?.IsVisibleInTree() == true);
        await Task.Delay(1200);
        AssertBackdrop(NRestSiteRoom.Instance!, ArchitectRoomBackgrounds.RestBackgroundPath);
        await Capture(".rest");
        await RunManager.Instance.EnterMapCoord(run.Map.StartingMapPoint.Children.Single().coord);
        if (run.CurrentRoom.RoomType != RoomType.Shop)
            throw new InvalidOperationException("Act 4 must contain a normal shop.");
        await WaitFor(() => NMerchantRoom.Instance?.IsVisibleInTree() == true);
        await Task.Delay(1200);
        AssertBackdrop(NMerchantRoom.Instance!.GetNode<Control>("SceneContainer"),
            ArchitectRoomBackgrounds.ShopBackgroundPath);
        await Capture(".shop");
        await RunManager.Instance.EnterMapCoord(run.Map.BossMapPoint.coord);
        MainFile.Logger.Info("Architect smoke: Act 4 Rest -> Shop -> Boss passed.");
        await Smoke(player);
    }

    private static void AssertBackdrop(Control parent, string texturePath)
    {
        var background = parent.GetNode<TextureRect>("ArchitectBackdrop");
        if (background.Texture?.ResourcePath != texturePath ||
            background.Texture.GetSize() != new Vector2(1672, 941) ||
            !background.GetGlobalRect().Encloses(parent.GetViewport().GetVisibleRect()) ||
            background.MouseFilter != Control.MouseFilterEnum.Ignore)
            throw new InvalidOperationException($"Room backdrop must fill the viewport without blocking input: {texturePath}, " +
                $"actual texture {background.Texture?.ResourcePath}, bounds {background.GetGlobalRect()}.");
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Architect smoke timed out.");
            await NGame.Instance!.AwaitProcessFrame();
        }
    }

    private static async Task Smoke(Player player)
    {
        SmokeStorage(player);
        await WaitFor(() => player.PlayerCombatState is { TurnNumber: 1, Phase: PlayerTurnPhase.Play } &&
            CombatManager.Instance.IsPartOfPlayerTurn(player) && !CombatManager.Instance.IsStarting);
        var combat = player.Creature.CombatState!;
        var challenger = combat.Enemies.FirstOrDefault(enemy => enemy.Monster is CorruptedChallenger);
        if (challenger?.Monster is CorruptedChallenger challengerModel)
        {
            var actor = challengerModel.Native ??
                throw new InvalidOperationException("The saved snapshot must create a native Challenger.");
            var initialHp = player.Creature.CurrentHp;
            if (CommandLineHelper.HasArg("architect-feedback"))
            {
                await PowerCmd.Apply<WeakPower>(new ThrowingPlayerChoiceContext(), challenger, 2, player.Creature, null);
                await PowerCmd.Apply<FrailPower>(new ThrowingPlayerChoiceContext(), challenger, 2, player.Creature, null);
                actor.AssertIdentity();
            }
            await Capture(".challenger");
            var face = NGame.Instance!.FindChild("ChallengerCard", true, false) as Button
                ?? throw new InvalidOperationException("The Challenger card inspection UI is missing.");
            face.EmitSignal(Control.SignalName.MouseEntered);
            face.EmitSignal(Control.SignalName.FocusEntered);
            var hoverContainer = NGame.Instance?.HoverTipsContainer
                ?? throw new InvalidOperationException("The card hover container is missing.");
            var preview = hoverContainer.GetNode<Control>("ChallengerCardPreview");
            var previewRect = preview.GetGlobalRect();
            if (!NGame.Instance!.GetViewport().GetVisibleRect().Encloses(previewRect) ||
                previewRect.GetCenter().DistanceTo(face.GetGlobalRect().GetCenter()) > 600f)
                throw new InvalidOperationException("The enlarged card preview must stay on screen near its source.");
            await Capture(".hover");
            face.EmitSignal(Control.SignalName.MouseExited);
            face.EmitSignal(Control.SignalName.FocusExited);
            PlayerCmd.EndTurn(player, false);
            await WaitFor(() => player.PlayerCombatState is { TurnNumber: 2, Phase: PlayerTurnPhase.Play } &&
                CombatManager.Instance.IsPartOfPlayerTurn(player));
            if (player.Creature.CurrentHp >= initialHp)
                throw new InvalidOperationException("The Challenger starter plan did not execute its attacks.");
            var hp = player.Creature.CurrentHp;
            var hand = player.PlayerCombatState!.Hand.Cards.ToArray();
            var energy = player.PlayerCombatState.Energy;
            await CreatureCmd.Kill(challenger, true);
            if (player.Creature.CurrentHp != hp || player.PlayerCombatState.Energy != energy ||
                !player.PlayerCombatState.Hand.Cards.SequenceEqual(hand) ||
                !combat.Enemies.Any(enemy => enemy.Monster is ArchitectBoss))
                throw new InvalidOperationException("Challenger handoff did not preserve player combat state.");
            var boss = combat.Enemies.Single(enemy => enemy.Monster is ArchitectBoss);
            if (NCombatRoom.Instance!.GetCreatureNode(boss)!.GlobalPosition.X <
                NGame.Instance.GetViewport().GetVisibleRect().Size.X * 0.6f)
                throw new InvalidOperationException("The Architect must spawn in the right-hand enemy position.");
        }
        var turn = player.PlayerCombatState!.TurnNumber;
        PlayerCmd.EndTurn(player, false);
        await WaitFor(() => player.PlayerCombatState?.TurnNumber == turn + 1 &&
            player.PlayerCombatState.Phase == PlayerTurnPhase.Play &&
            CombatManager.Instance.IsPartOfPlayerTurn(player));
        if (!player.Creature.HasPower<VulnerablePower>() || !player.Creature.HasPower<WeakPower>() ||
            !player.Creature.HasPower<FrailPower>())
            throw new InvalidOperationException("Architect Rewrite did not apply its opening debuffs.");
        MainFile.Logger.Info("Architect smoke: opening and continuous combat state passed.");
        await Capture("");
        var terminal = CommandLineHelper.HasArg("architect-loss")
            ? CreatureCmd.Kill(player.Creature, true)
            : CreatureCmd.Kill(combat.Enemies.Single(enemy => enemy.Monster is ArchitectBoss), true);
        await WaitFor(() => terminal.IsCompleted);
        await terminal;
        await CombatManager.Instance.CheckWinCondition();
        await WaitFor(() => ArchitectRun.Get(player.RunState).Outcome != null);
        await WaitFor(() => NOverlayStack.Instance?.Peek() is NGameOverScreen);
        await Capture(".result");
        if (player.RunState.CurrentRoom?.IsVictoryRoom != true)
            throw new InvalidOperationException("Architect terminal outcome did not become a base-game victory.");
        MainFile.Logger.Info($"Architect smoke passed: {ArchitectRun.Get(player.RunState).Outcome}.");
        await NGame.Instance!.AwaitProcessFrame();
        NGame.Instance!.GetTree().Quit();
    }

    private static async Task Capture(string suffix)
    {
        if (CommandLineHelper.GetValue("architect-capture") is not { } screenshot)
            return;
        await NGame.Instance!.AwaitProcessFrame();
        var error = NGame.Instance!.GetViewport().GetTexture().GetImage().SavePng(screenshot + suffix + ".png");
        if (error != Error.Ok)
            throw new InvalidOperationException($"Could not capture playtest screenshot: {error}.");
    }

    private static void SmokeStorage(Player player)
    {
        var path = Path.Combine(OS.GetUserDataDir(), "TheArchitect", $"smoke-{Guid.NewGuid():N}.json");
        try
        {
            var snapshot = ChallengerSnapshot.Capture(player);
            var first = ChallengerStore.Commit(path, "first", "ArchitectWin", snapshot);
            var duplicate = ChallengerStore.Commit(path, "first", "ArchitectWin", snapshot);
            var second = ChallengerStore.Commit(path, "second", "ArchitectLoss", snapshot);
            var loaded = ChallengerStore.Load(path);
            var backup = ChallengerStore.Load(path + ".backup");
            if (first != 1 || duplicate != 1 || second != 2 || loaded?.Revision != 2 || backup?.Revision != 1 ||
                loaded.ProfileUuid != backup.ProfileUuid || loaded.Snapshot?.ContentHash != snapshot.ContentHash ||
                loaded.Snapshot.RestoreDeck().Count != player.Deck.Cards.Count)
                throw new InvalidOperationException("Challenger snapshot roundtrip, backup, or idempotent commit failed.");
            MainFile.Logger.Info("Architect smoke: native snapshot roundtrip, atomic backup, and duplicate commit passed.");
        }
        finally
        {
            foreach (var suffix in new[] { "", ".backup", ".tmp", ".backup.tmp", ".invalid" })
                File.Delete(path + suffix);
        }
    }
}

[HarmonyPatch(typeof(BootstrapSettingsUtil), nameof(BootstrapSettingsUtil.Get))]
internal static class ArchitectBootstrapPatch
{
    private static bool Prefix(ref Type? __result)
    {
        if (!CommandLineHelper.HasArg("architect-playtest"))
            return true;
        __result = typeof(ArchitectPlaytest);
        return false;
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SeenFtue))]
    internal static class ArchitectPlaytestTutorialPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!CommandLineHelper.HasArg("architect-playtest"))
                return true;
            __result = true;
            return false;
        }
    }
}
