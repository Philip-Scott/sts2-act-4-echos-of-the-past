using AnimatedPlayerHands;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Pooling;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.addons.mega_text;
using Environment = System.Environment;

/// <summary>
/// Development-only runner: boots a fake three-player run in the real game, enters a monster combat
/// and checks the remote hands against each remote player's simulated piles. Run tests/run-native.sh.
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class NativeTests
{
    private static int _passed;
    private static CardModel? _faulty;

    // Simulates a modded card whose text throws when previewed for its owner's hand.
    [HarmonyPatch(typeof(CardModel), nameof(CardModel.GetDescriptionForPile), [typeof(PileType), typeof(Creature)])]
    private static class FaultyCardText
    {
        private static void Prefix(CardModel __instance, PileType pileType)
        {
            if (__instance == _faulty && pileType == PileType.Hand)
                throw new InvalidOperationException("Simulated modded card failure");
        }
    }

    public static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("ANIMATED_PLAYER_HANDS_TESTS") != "1")
            throw new InvalidOperationException("Run this development mod using tests/run-native.sh.");
        var game = NGame.Instance ?? throw new InvalidOperationException("The native game root is missing.");
        game.StartOnMainMenu = false;
        new Harmony("AnimatedPlayerHandsNativeTests").PatchAll(typeof(NativeTests).Assembly);
        _ = RunAfterStartup(game);
    }

    private static async Task RunAfterStartup(NGame game)
    {
        try
        {
            await game.GameStartupComplete;
            await Run(game);
            Console.WriteLine($"ANIMATED_PLAYER_HANDS_TESTS_OK {_passed} checks");
            game.GetTree().Quit();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"ANIMATED_PLAYER_HANDS_TESTS_FAILED: {error}");
            game.GetTree().Quit(1);
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException("FAILED: " + description);
        _passed++;
        Console.WriteLine("PASS " + description);
    }

    private static async Task Frames(int count = 1)
    {
        for (var frame = 0; frame < count; frame++)
            await NGame.Instance!.AwaitProcessFrame();
    }

    private static async Task WaitFor(Func<bool> condition, string description, double seconds = 120)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Timed out: " + description);
            await Frames();
        }
    }

    private static async Task Run(NGame game)
    {
        CharacterModel[] characters = [ModelDb.Character<Ironclad>(), .. RemoteCharacters()];
        var players = characters.Select((character, index) =>
            Player.CreateForNewRun(character, UnlockState.all, (ulong)index + 1)).ToArray();
        var run = RunState.CreateForNewRun(players,
            [ModelDb.Act<Overgrowth>().ToMutable(), ModelDb.Act<Hive>().ToMutable(), ModelDb.Act<Glory>().ToMutable()],
            [], GameMode.Standard, 0, "ANIMATED-PLAYER-HANDS");
        // Tutorial overlays would cover combat; a hand under an overlay correctly refuses inspection.
        SaveManager.Instance.SetFtuesEnabled(false);
        var manager = RunManager.Instance;
        manager.SetUpTest(run, new NetSingleplayerGameService(), disableCombatStateSync: true, shouldSave: false);
        manager.GenerateRooms();
        await PreloadManager.LoadRunAssets(characters);
        manager.Launch();
        game.RootSceneContainer.SetCurrentScene(NRun.Create(run));
        await manager.EnterAct(0, doTransition: false);
        await manager.EnterRoomDebug(RoomType.Monster, showTransition: false);
        await WaitFor(() => players.All(player => player.PlayerCombatState is { TurnNumber: 1, Phase: PlayerTurnPhase.Play }) &&
            !CombatManager.Instance.IsStarting, "first player turn");

        // Debug room entry skips choosing a map node, so the act's opening map is still open.
        Console.WriteLine($"SCREEN {ActiveScreenContext.Instance.GetCurrentScreen()?.GetType().Name}");
        if (NMapScreen.Instance is { IsOpen: true } map)
            map.Close(animateOut: false);
        await WaitFor(() => ActiveScreenContext.Instance.IsCurrent(NCombatRoom.Instance!), "the combat room to be the active screen");

        var local = players[0];
        var remotes = players.Skip(1).ToArray();
        await WaitFor(() => RemoteHands.Active.Count == remotes.Length && RemoteHands.Active.All(Settled),
            "opening draws to land");
        Check(RemoteHands.Active.All(panel => panel.Player != local), "the local player has no remote hand");
        Check(remotes.All(player => RemoteHands.Active.Count(panel => panel.Player == player) == 1),
            "every remote player has exactly one hand");
        foreach (var panel in RemoteHands.Active)
        {
            AssertMatches(panel, "after the opening draw");
            // Cards generated straight into the hand grow in place instead, as natively.
            Check(panel.PileArrivals > 0 && panel.PileArrivals <= panel.Player.PlayerCombatState!.Hand.Cards.Count,
                $"player {panel.Player.NetId}'s opening draw flies in from the draw pile badge");
        }
        foreach (var panel in RemoteHands.Active)
        {
            var display = panel.Anchor.GetNode<Control>("%HealthBar");
            await WaitFor(() => display.IsVisibleInTree() && display.Modulate.A > 0.99f, "remote health bars to fade in");
            Check(true, $"player {panel.Player.NetId}'s health bar shows without hovering");
            var powers = display.GetNode<Control>("%PowerContainer");
            Check(new[] { "Energy", "DrawPile", "DiscardPile", "ExhaustPile" }.All(name =>
                    panel.StatusIcon(name) is { } icon && icon.GetParent() == powers && icon.IsVisibleInTree()),
                $"player {panel.Player.NetId}'s energy and piles sit with their powers");
        }
        await Screenshot(game, "opening");
        AssertLayout(game);
        AssertFormation(players);

        var first = Panel(remotes[0]);
        var state = remotes[0].PlayerCombatState!;
        var context = new ThrowingPlayerChoiceContext();

        var drawIcon = first.Badge(PileType.Draw).GetGlobalRect();
        var discarded = state.Hand.Cards[0];
        await CardCmd.Discard(context, discarded);
        await Frames();
        Check(first.Miniatures.All(node => node?.Model != discarded), "a discarded card leaves the remote hand");
        Check(Layer()?.GetChildren().OfType<NCardFlyVfx>().Any() == true, "a discarded card flies to the discard badge");
        await WaitFor(() => Settled(first), "the discard to settle");
        AssertMatches(first, "after a discard");
        Check(first.Badge(PileType.Draw).GetGlobalRect() == drawIcon, "pile icons stay put when the hand shrinks");

        var exhausted = state.Hand.Cards[^1];
        await CardCmd.Exhaust(context, exhausted);
        await Frames();
        Check(first.Miniatures.All(node => node?.Model != exhausted), "an exhausted card leaves the remote hand");
        var burning = NCombatRoom.Instance!.Ui.GetChildren().OfType<NCard>().SingleOrDefault(node => node.Model == exhausted);
        Check(burning != null && Descendants(burning).OfType<NCardExhaustQuickVfx>().Count() == 1,
            "an exhausted card burns in place");
        await WaitFor(() => Settled(first), "the exhaust to settle");
        AssertMatches(first, "after an exhaust");

        var emerged = first.PileArrivals;
        var drawn = await CardPileCmd.Draw(context, remotes[0]);
        Check(drawn != null, "the remote player draws a card");
        await WaitFor(() => Settled(first) && first.PileArrivals > emerged, "the draw to land");
        Check(first.PileArrivals == emerged + 1, "a drawn card flies from the draw pile badge");
        AssertMatches(first, "after a draw");

        var played = state.Hand.Cards.First(card => card.TargetType is TargetType.AnyEnemy or TargetType.Self or TargetType.None);
        var target = played.TargetType == TargetType.AnyEnemy
            ? remotes[0].Creature.CombatState!.Enemies.First(enemy => enemy.IsAlive)
            : null;
        await CardCmd.AutoPlay(context, played, target);
        await WaitFor(() => Settled(first) && !state.PlayPile.Cards.Contains(played), "the remote play to resolve");
        AssertMatches(first, "after a remote play");

        var face = first.Miniatures[0]!.GetParent<Control>();
        Console.WriteLine($"HOVER screen={ActiveScreenContext.Instance.IsCurrent(NCombatRoom.Instance!)} " +
            $"blockTips={NHoverTipSet.shouldBlockHoverTips} targeting={NTargetManager.Instance.IsInSelection} " +
            $"cardPlay={NCombatRoom.Instance!.Ui.Hand.InCardPlay} mouse={first.Root.MouseBehaviorRecursive} " +
            $"visible={first.Root.IsVisibleInTree()}");
        face.EmitSignal(Control.SignalName.MouseEntered);
        await Frames(2);
        var preview = NCombatRoom.Instance!.Ui.GetNodeOrNull<Control>("AnimatedPlayerHandPreview");
        Check(preview?.GetChildren().OfType<NCard>().SingleOrDefault()?.Model == first.Miniatures[0]!.Model,
            "hovering a remote card shows it enlarged");
        await Screenshot(game, "hover");
        face.EmitSignal(Control.SignalName.MouseExited);
        await Frames(2);
        Check(NCombatRoom.Instance.Ui.GetNodeOrNull<Control>("AnimatedPlayerHandPreview") == null,
            "leaving the card hides the enlarged preview");
        await Screenshot(game, "after-actions");

        var second = Panel(remotes[1]);
        // Rendering every card is slow under software rendering; screenshot runs skip it.
        if (Environment.GetEnvironmentVariable("ANIMATED_PLAYER_HANDS_SWEEP") != "0")
            foreach (var remote in remotes)
                await SweepCardPool(Panel(remote), context);

        var firstState = remotes[0].PlayerCombatState!;
        _faulty = remotes[0].Creature.CombatState!.CreateCard(remotes[0].Character.CardPool.AllCards.First(), remotes[0]);
        await CardPileCmd.AddGeneratedCardToCombat(_faulty, PileType.Hand, remotes[0]);
        await WaitFor(() => Settled(first) && first.Miniatures.Count == firstState.Hand.Cards.Count, "the faulty card to land");
        Check(firstState.Hand.Cards.Contains(_faulty), "a card that fails to render still reaches its owner's hand");
        var faultyNode = first.Miniatures.Single(node => node!.Model == _faulty)!;
        Check(faultyNode.GetNode<MegaRichTextLabel>("%DescriptionLabel").Text.Length > 0,
            "a card whose live text throws falls back to its deck text");
        await CardPileCmd.RemoveFromCombat([_faulty], skipVisuals: true);
        _faulty = null;

        // End combat while a drawn card is still sliding in, so teardown meets a card that is both
        // a flight and a hand miniature.
        await CardPileCmd.Draw(context, remotes[1]);
        await Frames();
        Check(second.InFlight > 0, "combat ends while a remote card is in flight");
        foreach (var enemy in local.Creature.CombatState!.Enemies.Where(enemy => enemy.IsAlive).ToArray())
            await CreatureCmd.Kill(enemy);
        await WaitFor(() => first.Stopped && second.Stopped, "hands to close when combat ends");
        await Frames(2);
        Check(!GodotObject.IsInstanceValid(first.Root) || !first.Root.IsInsideTree(), "hands leave the scene after combat");
        await Frames(2);
        var pooled = NodePool.Get<NCard>();
        Check(pooled.MouseBehaviorRecursive == Control.MouseBehaviorRecursiveEnum.Inherited &&
            pooled.FocusBehaviorRecursive == Control.FocusBehaviorRecursiveEnum.Inherited,
            "recycled cards come back from the pool unchanged");
        pooled.QueueFreeSafely();

        // Zoomed-out encounters bring the two player rows closer on screen; they must stay separate rows.
        var effigy = ModelDb.Encounter<BygoneEffigyElite>();
        await manager.EnterRoomDebug(RoomType.Elite, model: effigy.ToMutable(), showTransition: false);
        await WaitFor(() => players.All(player => player.PlayerCombatState is { TurnNumber: 1, Phase: PlayerTurnPhase.Play }) &&
            !CombatManager.Instance.IsStarting, "the Bygone Effigy turn");
        await WaitFor(() => RemoteHands.Active.Count == remotes.Length && RemoteHands.Active.All(panel =>
            Settled(panel) && panel.Miniatures.Count == panel.Player.PlayerCombatState!.Hand.Cards.Count), "Effigy hands to land");
        AssertLayout(game);
        var zoom = effigy.GetCameraScaling();
        foreach (var panel in RemoteHands.Active)
        {
            var width = (panel.Root.GetGlobalTransform() * new Rect2(Vector2.Zero, panel.Root.Size)).Size.X;
            Check(width >= HandLayoutGeometry.PanelWidth * HandLayoutGeometry.MinClearScale * zoom * 0.99f,
                $"player {panel.Player.NetId}'s hand stays readable at {zoom} camera zoom (width {width})");
        }
        await Screenshot(game, "effigy");
    }

    // Remote characters come from ANIMATED_PLAYER_HANDS_CHARACTERS (two or three comma-separated character
    // ID fragments, such as modded characters' IDs), or default to the Silent and the Defect.
    private static CharacterModel[] RemoteCharacters()
    {
        var requested = Environment.GetEnvironmentVariable("ANIMATED_PLAYER_HANDS_CHARACTERS");
        if (string.IsNullOrWhiteSpace(requested))
            return [ModelDb.Character<Silent>(), ModelDb.Character<Defect>()];
        var all = ModelDb.All.OfType<CharacterModel>().ToArray();
        var characters = requested.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(fragment => all.SingleOrDefault(character =>
                    character.Id.ToString().Contains(fragment, StringComparison.OrdinalIgnoreCase)) ??
                throw new InvalidOperationException($"No single character matches '{fragment}': " +
                    string.Join(", ", all.Select(character => character.Id))))
            .ToArray();
        if (characters.Length is < 2 or > 3)
            throw new InvalidOperationException("ANIMATED_PLAYER_HANDS_CHARACTERS must name two or three characters.");
        return characters;
    }

    // Every card in the owner's pool passes through their hand, ten at a time.
    private static async Task SweepCardPool(RemoteHandPanel panel, PlayerChoiceContext context)
    {
        var player = panel.Player;
        var state = player.PlayerCombatState!;
        var combat = player.Creature.CombatState!;
        var pool = player.Character.CardPool.AllCards.ToArray();
        await CardPileCmd.Add(state.Hand.Cards.ToArray(), PileType.Discard);
        var unformatted = new List<string>();
        foreach (var batch in pool.Chunk(10))
        {
            var cards = batch.Select(canonical => combat.CreateCard(canonical, player)).ToArray();
            await CardPileCmd.AddGeneratedCardsToCombat(cards, PileType.Hand, player);
            try
            {
                await WaitFor(() => Settled(panel) && panel.Miniatures.Count == state.Hand.Cards.Count,
                    $"{player.Character.Id} pool batch to land");
            }
            catch (TimeoutException)
            {
                Console.WriteLine($"DIAG inFlight={panel.InFlight} hand=[{string.Join(", ", state.Hand.Cards.Select(card => card.Id))}] " +
                    $"shown=[{string.Join(", ", panel.Miniatures.Select(node => $"{node?.Model?.Id}@{node?.GetParent()?.GetType().Name}/{node?.Visible}"))}]");
                throw;
            }
            var shown = panel.Miniatures.Select(node => node!.Model).ToArray();
            if (!shown.SequenceEqual(state.Hand.Cards))
                throw new InvalidOperationException($"FAILED: {player.Character.Id} hand shows " +
                    $"[{string.Join(", ", shown.Select(card => card?.Id))}] instead of " +
                    $"[{string.Join(", ", state.Hand.Cards.Select(card => card.Id))}]");
            foreach (var node in panel.Miniatures)
            {
                var text = node!.GetNode<MegaRichTextLabel>("%DescriptionLabel").Text;
                if (text.Contains('{') || text.Contains('}'))
                    unformatted.Add($"{node.Model!.Id}: {text}");
            }
            await CardPileCmd.RemoveFromCombat(state.Hand.Cards.ToArray(), skipVisuals: true);
            await WaitFor(() => Settled(panel) && panel.Miniatures.Count == 0, "the sweep batch to leave");
        }
        foreach (var line in unformatted)
            Console.WriteLine("UNFORMATTED " + line);
        Check(unformatted.Count == 0, $"all {pool.Length} {player.Character.Id} cards render formatted text in a remote hand");
    }

    private static IEnumerable<Node> Descendants(Node node) =>
        node.GetChildren().SelectMany(child => Descendants(child).Prepend(child));

    private static Node? Layer() => NCombatRoom.Instance!.Ui.GetNodeOrNull("AnimatedPlayerHandCards");

    private static RemoteHandPanel Panel(Player player) => RemoteHands.Active.Single(panel => panel.Player == player);

    // Every miniature has landed in its slot and nothing is still travelling.
    private static bool Settled(RemoteHandPanel panel) =>
        panel.InFlight == 0 && panel.Miniatures.All(node => node != null && node.GetParent() is Button);

    private static void AssertMatches(RemoteHandPanel panel, string when)
    {
        var state = panel.Player.PlayerCombatState!;
        var shown = panel.Miniatures.Select(node => node?.Model).ToArray();
        Check(shown.SequenceEqual(state.Hand.Cards),
            $"player {panel.Player.NetId}'s hand shows {state.Hand.Cards.Count} cards in order {when}");
        Check(panel.Miniatures.All(node => node!.Visible), $"player {panel.Player.NetId}'s cards are visible {when}");
        foreach (var (pile, cards) in new[] { (PileType.Draw, state.DrawPile), (PileType.Discard, state.DiscardPile),
                     (PileType.Exhaust, state.ExhaustPile) })
            Check(panel.Badge(pile).GetNode<MegaLabel>("AmountLabel").Text == cards.Cards.Count.ToString(),
                $"player {panel.Player.NetId}'s {pile} badge counts {cards.Cards.Count} {when}");
        Check(panel.StatusIcon("Energy")!.GetNode<MegaLabel>("AmountLabel").Text == state.Energy.ToString(),
            $"player {panel.Player.NetId}'s energy shows {state.Energy} {when}");
    }

    // Players standing side by side get the wider gap, so each remote hand has room above its owner, and
    // the formation stands just left of the middle line.
    private static void AssertFormation(Player[] players)
    {
        var nodes = players.Select(player => NCombatRoom.Instance!.GetCreatureNode(player.Creature)!).ToArray();
        var allies = NCombatRoom.Instance!.CreatureNodes.Where(node => node.Entity.IsPlayer || node.Entity.PetOwner != null);
        var right = allies.Max(node => node.Position.X + node.Visuals.Bounds.Size.X * 0.5f);
        Check(Math.Abs(right + PlayerFormation.MiddleMargin) < 1f,
            $"the formation stops {-right:0} left of the middle line");

        foreach (var row in nodes.GroupBy(node => node.Position.Y))
        {
            var ordered = row.OrderBy(node => node.Position.X).ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                var gap = ordered[index].Position.X - ordered[index - 1].Position.X -
                    (ordered[index].Visuals.Bounds.Size.X + ordered[index - 1].Visuals.Bounds.Size.X) / 2f;
                Check(gap >= PlayerFormation.Gap - 1f,
                    $"players {ordered[index - 1].Entity.Player!.NetId} and {ordered[index].Entity.Player!.NetId} stand {gap:0} apart");
            }
        }
    }

    private static void AssertLayout(NGame game)
    {
        var viewport = game.GetViewport().GetVisibleRect();
        var rects = new List<Rect2>();
        foreach (var status in RemoteHands.StatusBounds())
            Console.WriteLine($"STATUS player {status.Owner.Entity.Player?.NetId}: {status.Bounds}");
        foreach (var panel in RemoteHands.Active)
            Console.WriteLine($"HAND player {panel.Player.NetId}: {panel.Root.GetGlobalTransform() * new Rect2(Vector2.Zero, panel.Root.Size)}");
        foreach (var panel in RemoteHands.Active)
        {
            var rect = panel.Root.GetGlobalTransform() * new Rect2(Vector2.Zero, panel.Root.Size);
            Console.WriteLine($"LAYOUT player {panel.Player.NetId}: hand={rect} healthBarTop={RemoteHands.HealthBarTop(panel.Anchor)} viewport={viewport.Size}");
            Check(panel.Root.IsVisibleInTree(), $"player {panel.Player.NetId}'s hand is visible");
            Check(viewport.Encloses(rect), $"player {panel.Player.NetId}'s hand is on screen");
            Check(rect.End.Y <= RemoteHands.HealthBarTop(panel.Anchor).Y + 0.5f || rect.Position.Y <= RemoteHands.TopMargin() + 0.5f,
                $"player {panel.Player.NetId}'s hand rests above their health bar");
            Check(rects.All(other => !other.Intersects(rect)), $"player {panel.Player.NetId}'s hand overlaps no other hand");
            var minimum = HandLayoutGeometry.PanelWidth * HandLayoutGeometry.MinClearScale *
                Mathf.Abs(panel.Anchor.GetGlobalTransform().Scale.X) + 0.5f;
            var covered = RemoteHands.StatusBounds().Where(status => status.Owner != panel.Anchor &&
                rect.Intersects(new Rect2(status.Bounds.X, status.Bounds.Y, status.Bounds.Width, status.Bounds.Height)))
                .Select(status => status.Owner.Entity.Player?.NetId).ToArray();
            Check(covered.Length == 0 || rect.Size.X <= minimum,
                $"player {panel.Player.NetId}'s hand covers no other player's health bar or icons ({string.Join(",", covered)})");
            rects.Add(rect);
        }
    }

    private static async Task Screenshot(NGame game, string name)
    {
        var directory = Environment.GetEnvironmentVariable("ANIMATED_PLAYER_HANDS_SCREENSHOTS");
        if (string.IsNullOrEmpty(directory) || DisplayServer.GetName() == "headless")
            return;
        await Frames(30);
        await RenderingServer.Singleton.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = game.GetViewport().GetTexture().GetImage();
        var path = Path.Combine(directory, name + ".png");
        image.SavePng(path);
        Console.WriteLine($"SCREENSHOT {path}");
    }
}
