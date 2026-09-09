using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using TheArchitect.TheArchitectCode.Acts;
using TheArchitect.TheArchitectCode.Ancients;
using TheArchitect.TheArchitectCode.Relics;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class UnwrittenPlaytest
{
    internal static async Task RenderRelicArt(NGame game)
    {
        var relics = ArchitectModels.Ancient.OptionPools.AllOptions.Select(option => option.ModelForOption).ToArray();
        var backdrop = new ColorRect
        {
            Color = new Color("243036"),
            Size = game.GetViewportRect().Size,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        game.AddChild(backdrop);
        for (var i = 0; i < relics.Length; i++)
        {
            var relic = relics[i];
            var icon = PreloadManager.Cache.GetTexture2D(relic.PackedIconPath);
            var bigPath = (string)AccessTools.PropertyGetter(typeof(UnwrittenRelic), "BigIconPath").Invoke(relic, null)!;
            var outlinePath = (string)AccessTools.PropertyGetter(typeof(UnwrittenRelic), "PackedIconOutlinePath").Invoke(relic, null)!;
            var big = PreloadManager.Cache.GetTexture2D(bigPath);
            var outline = PreloadManager.Cache.GetTexture2D(outlinePath);
            Check(icon.GetSize() == new Vector2(94, 94) && outline.GetSize() == new Vector2(94, 94) &&
                big.GetSize() == new Vector2(256, 256), $"Packed relic art loads at native sizes: {relic.Id.Entry}");
            var origin = new Vector2(80 + i % 4 * 440, 70 + i / 4 * 480);
            backdrop.AddChild(new TextureRect { Texture = big, Position = origin, Size = new Vector2(256, 256) });
            backdrop.AddChild(new Label { Text = relic.Title.GetFormattedText(), Position = origin + new Vector2(0, 270) });
            backdrop.AddChild(new TextureRect { Texture = icon, Position = origin + new Vector2(0, 320), Size = new Vector2(94, 94) });
            backdrop.AddChild(new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Texture = icon, Position = origin + new Vector2(140, 343), Size = new Vector2(48, 48)
            });
        }
        await NativeDemoPlaytest.Capture("relic-art");
        MainFile.Logger.Info("RELIC ART SMOKE PASSED: eight inventory icons, outlines, and large textures loaded from the PCK.");
        game.GetTree().Quit();
    }

    internal static async Task ChooseBuildGift(Player player)
    {
        var run = player.RunState;
        if (run.CurrentRoom is not EventRoom { CanonicalEvent: TheUnwritten })
            await RunManager.Instance.EnterMapCoord(run.Map.StartingMapPoint.coord);
        await WaitFor(() => run.CurrentRoom is EventRoom { LocalMutableEvent.CurrentOptions.Count: 3 });
        var ancient = CurrentAncient(run);
        Check(!ancient.IsShared && ancient.Owner == player, "Native personal Ancient owner");
        var entries = ancient.CurrentOptions.Select(option => option.Relic?.Id.Entry).ToArray();
        Check(entries[0] is "THEARCHITECT-LOOSE_THREAD" or "THEARCHITECT-CROOKED_NEEDLE" or
            "THEARCHITECT-ORANGE_PEARL" or "THEARCHITECT-DIAMOND_HAND", "Build slot");
        Check(entries[1] is "THEARCHITECT-UNSPENT_POSSIBILITY" or "THEARCHITECT-LAST_MEAL", "Recovery slot");
        Check(entries[2] is "THEARCHITECT-BORROWED_TOMORROW" or "THEARCHITECT-HANDHELD_MIRROR", "Bargain slot");
        Check(ancient.CurrentOptions.All(option => !option.IsLocked && !option.IsProceed), "No decline or reroll");
        if (NativeDemoSafety.Enabled)
        {
            await Task.Delay(3000);
            await NativeDemoPlaytest.Capture("unwritten");
        }
        var before = player.Relics.Count;
        RunManager.Instance.EventSynchronizer.ChooseLocalOption(0);
        await RunManager.Instance.EventSynchronizer.AwaitPendingOptionTasks();
        Check(ancient.IsFinished && player.Relics.Count == before + 1 &&
            player.Relics.Any(relic => relic.Id.Entry == entries[0]), "Exactly one native relic acquisition");
        await NEventRoom.Proceed();
        await WaitFor(() => NMapScreen.Instance?.IsOpen == true);
    }

    internal static async Task Run(NGame game)
    {
        var run = await game.StartNewSingleplayerRun(ModelDb.Character<Ironclad>(), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()],
            [], "ARCHITECT-UNWRITTEN", GameMode.Standard);
        var player = run.Players.Single();
        player.Creature.SetCurrentHpInternal(10);
        var result = new DevConsole(shouldAllowDebugCommands: true).ProcessCommand("architect");
        Check(result.success && result.task != null, "Console entry");
        await result.task!;
        Check(run.Act is ArchitectAct && run.Map is ArchitectMap { HasAncient: true }, "Ancient Act 4 route");
        var pool = ArchitectModels.Ancient.OptionPools.AllOptions.ToArray();
        Check(pool.Length == 8 && pool.All(option => option.Weight == 1), "Eight equally weighted category candidates");
        Check(ImageHelper.GetRoomIconPath(MapPointType.Ancient, RoomType.Event, ArchitectModels.Ancient.Id)
                == ArchitectModels.Ancient.CustomMapIconPath,
            $"Ancient history icon mapping ({ArchitectModels.Ancient.Id} vs {ArchitectModels.AncientId})");

        // Save at the native room boundary: unchosen offers must reconstruct without a reroll.
        if (run.CurrentRoom is not EventRoom { CanonicalEvent: TheUnwritten })
            await RunManager.Instance.EnterMapCoord(run.Map.StartingMapPoint.coord);
        var offered = CurrentAncient(run).CurrentOptions.Select(o => o.Relic!.Id).ToArray();
        Check(offered.Length == 3, "Initial Ancient offers generated successfully");
        Check(player.Creature.CurrentHp == player.Creature.MaxHp, "Native entry healing retained");
        await Task.Delay(3000);
        run = await Reload(game);
        player = run.Players.Single();
        var resumed = CurrentAncient(run).CurrentOptions.Select(o => o.Relic!.Id);
        Check(offered.SequenceEqual(resumed), "Native reload retains offer identities and ordering");
        await ChooseBuildGift(player);
        await Task.Delay(500);
        await NativeDemoPlaytest.Capture("unwritten-map");
        AssertMapVisible();
        var rest = run.Map.StartingMapPoint.Children.Single();
        await RunManager.Instance.EnterMapCoord(rest.coord);
        Check(run.CurrentRoom is RestSiteRoom, "Ancient precedes native Rest Site");
        var relics = player.Relics.Select(relic => relic.Id).ToArray();
        run = await Reload(game);
        player = run.Players.Single();
        Check(run.CurrentRoom is RestSiteRoom && relics.SequenceEqual(player.Relics.Select(relic => relic.Id)),
            "Post-choice native reload retains one gift and the Rest Site");
        var repeatedEntry = new DevConsole(shouldAllowDebugCommands: true).ProcessCommand("architect");
        Check(!repeatedEntry.success, "Developer command cannot grant a second Ancient visit");
        await RunManager.Instance.EnterMapCoord(run.Map.StartingMapPoint.Children.Single().Children.Single().coord);
        Check(run.CurrentRoom?.RoomType == RoomType.Shop, "Shop follows Rest Site");
        await NativeDemoPlaytest.Capture("unwritten-shop");
        await AcquisitionEffects(player);
        await CombatEffects(player);
        MainFile.Logger.Info("UNWRITTEN SMOKE PASSED: native arrival, pool, healing, choices, reload, map, rewards and combat.");
        game.GetTree().Quit();
    }

    private static async Task AcquisitionEffects(Player player)
    {
        var gold = player.Gold;
        await RelicCmd.Obtain(Bonus("UNSPENT_POSSIBILITY"), player);
        Check(player.Gold == gold + 150, "Unspent Possibility immediately grants 150 Gold");
        var maxHp = player.Creature.MaxHp;
        player.Creature.SetCurrentHpInternal(maxHp - 30);
        var deckSize = player.Deck.Cards.Count;
        var potionSlots = player.MaxPotionCount;
        var mealTask = RelicCmd.Obtain(Bonus("LAST_MEAL"), player);
        await WaitFor(() => NGame.Instance!.FindChildren("*", "", true, false)
            .OfType<NRewardsScreen>().Any(screen => screen.IsVisibleInTree()));
        var screen = NGame.Instance!.FindChildren("*", "", true, false).OfType<NRewardsScreen>()
            .Single(screen => screen.IsVisibleInTree());
        var rewards = (RewardsSet)AccessTools.Field(typeof(NRewardsScreen), "_rewardsSet").GetValue(screen)!;
        Check(player.Creature.MaxHp == maxHp + 20 && player.Creature.CurrentHp == maxHp - 10,
            "Last Meal gains 20 maximum HP and heals exactly 20, not twice");
        var cards = rewards.Rewards.OfType<CardReward>().Single();
        Check(rewards.Rewards.OfType<PotionReward>().Count() == 2 && cards.Cards.Count() == 3 &&
            cards.Cards.All(card => card.Rarity == CardRarity.Rare && card.Pool == player.Character.CardPool),
            "Last Meal generates two potions and three owner-pool rare cards");
        await NativeDemoPlaytest.Capture("last-meal");
        RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
        NOverlayStack.Instance!.Remove(screen);
        await mealTask;
        Check(player.Deck.Cards.Count == deckSize && player.MaxPotionCount == potionSlots,
            "Native reward skipping adds no card or potion slots");

        foreach (var relic in player.Relics.ToArray())
            await RelicCmd.Remove(relic);
        var sources = new RelicModel[]
        {
            ModelDb.Relic<OldCoin>().ToMutable(), ModelDb.Relic<Strawberry>().ToMutable(),
            ModelDb.Relic<HappyFlower>().ToMutable()
        };
        foreach (var source in sources)
            await RelicCmd.Obtain(source, player);
        ((HappyFlower)sources[2]).TurnsSeen = 2;
        gold = player.Gold;
        maxHp = player.Creature.MaxHp;
        await RelicCmd.Obtain(Bonus("HANDHELD_MIRROR"), player);
        Check(sources.All(source => player.Relics.Count(relic => relic.Id == source.Id) == 2) &&
            player.Relics.Count == 7, "Mirror grants three different fresh copies and preserves originals");
        Check(((HappyFlower)sources[2]).TurnsSeen == 2 &&
            player.Relics.OfType<HappyFlower>().Single(relic => !ReferenceEquals(relic, sources[2])).TurnsSeen == 0,
            "Mirror leaves original counters untouched and initializes copies fresh");
        Check(player.Gold == gold + sources[0].DynamicVars.Gold.IntValue &&
            player.Creature.MaxHp == maxHp + sources[1].DynamicVars.MaxHp.IntValue,
            "Mirror repeats native acquisition benefits without an added cost");
    }

    private static async Task CombatEffects(Player player)
    {
        foreach (var relic in player.Relics.ToArray())
            await RelicCmd.Remove(relic);
        foreach (var entry in new[] { "LOOSE_THREAD", "CROOKED_NEEDLE", "ORANGE_PEARL", "DIAMOND_HAND", "BORROWED_TOMORROW" })
            await RelicCmd.Obtain(Bonus(entry), player);
        await RunManager.Instance.EnterMapCoord(player.RunState.Map.BossMapPoint.coord);
        for (var turn = 1; turn <= 5; turn++)
        {
            var expectedTurn = turn;
            await WaitFor(() => player.PlayerCombatState is { Phase: PlayerTurnPhase.Play } state &&
                state.TurnNumber == expectedTurn && CombatManager.Instance.IsPartOfPlayerTurn(player) &&
                !CombatManager.Instance.IsStarting);
            var combat = player.PlayerCombatState!;
            var voidPenalty = combat.Hand.Cards.OfType<MegaCrit.Sts2.Core.Models.Cards.Void>()
                .Sum(card => card.DynamicVars.Energy.IntValue);
            Check(combat.Energy == (turn <= 3 ? 4 : 3) - voidPenalty,
                $"Borrowed Tomorrow energy on turn {turn}: {combat.Energy}, native Void penalty {voidPenalty}");
            Check(combat.Hand.Cards.Count == (turn <= 3 ? 6 : 5), $"Loose Thread draw on turn {turn}");
            Check(player.Deck.Cards.All(card => card.Enchantment == null), "Glam never modifies the permanent deck");
            Check(combat.AllCards.Count(card => card.Enchantment is Glam) == (turn + 1) / 2,
                $"Diamond Hand enchants only on odd turns, including turn {turn}");
            if (turn == 1)
            {
                Check(player.Creature.GetPowerAmount<StrengthPower>() == 1 &&
                    player.Creature.GetPowerAmount<DexterityPower>() == 1, "Crooked Needle combat-start powers");
                Check(player.Creature.GetPowerAmount<VulnerablePower>() == 0 &&
                    player.Creature.GetPowerAmount<ArtifactPower>() == 0,
                    "Orange Pearl's ordinary Artifact blocks Borrowed Tomorrow's Vulnerable");
                Check(combat.Hand.Cards.Count(card => card.Enchantment is Glam) == 1,
                    "Diamond Hand enchants one card after the first normal draw");
            }
            if (turn == 5)
                break;
            await CreatureCmd.Heal(player.Creature, player.Creature.MaxHp);
            PlayerCmd.EndTurn(player, false);
        }
        await NativeDemoPlaytest.Capture("unwritten-combat");
    }

    private static RelicModel Bonus(string entry) =>
        ModelDb.GetById<RelicModel>(new ModelId("RELIC", $"THEARCHITECT-{entry}")).ToMutable();

    internal static void AssertMapVisible()
    {
        var points = NMapScreen.Instance!.FindChildren("*", "", true, false).OfType<NMapPoint>().ToArray();
        Check(points.Length == 4 && points.Any(point => point is NAncientMapPoint) &&
            points.All(point => NGame.Instance!.GetViewport().GetVisibleRect().Encloses(point.GetGlobalRect().Grow(12f))),
            "Four map nodes visible without scrolling: " +
            string.Join("; ", points.Select(point => $"{point.Name} {point.GetGlobalRect()}")));
    }

    private static async Task<RunState> Reload(NGame game)
    {
        await SaveManager.Instance.SaveRun(null);
        var save = SaveManager.Instance.LoadRunSave().SaveData ??
            throw new InvalidOperationException("Unwritten disposable run save missing.");
        await game.ReturnToMainMenu();
        var run = RunState.FromSerializable(save);
        await RunManager.Instance.SetUpSavedSingleplayer(run, save);
        await game.LoadRun(run, save.PreFinishedRoom);
        return run;
    }

    private static TheUnwritten CurrentAncient(IRunState run) =>
        run.CurrentRoom is EventRoom { LocalMutableEvent: TheUnwritten ancient }
            ? ancient : throw new InvalidOperationException("Expected The Unwritten's native event room.");

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                if (NativeDemoSafety.Enabled)
                    await NativeDemoPlaytest.Capture("unwritten-timeout");
                var player = RunManager.Instance.DebugOnlyGetState()?.Players.SingleOrDefault();
                throw new TimeoutException($"Unwritten native flow timed out: turn={player?.PlayerCombatState?.TurnNumber}, " +
                    $"phase={player?.PlayerCombatState?.Phase}, starting={CombatManager.Instance.IsStarting}.");
            }
            await NGame.Instance!.AwaitProcessFrame();
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Unwritten: " + message);
        MainFile.Logger.Info("UNWRITTEN PASS: " + message);
    }
}
