using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.ValueProps;
using TheArchitect.TheArchitectCode.Enchantments;
using TheArchitect.TheArchitectCode.Powers;
using TheArchitect.TheArchitectCode.Relics;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class WatcherRelicPlaytest
{
    private static ICombatState? _openingScryCombat;

    internal static async Task CheckEggCounterDisplay(Player player)
    {
        Require(NativeDemoSafety.Enabled, "Egg counter display probe requires disposable storage.");
        var egg = (NurembergEgg)ModelDb.GetById<RelicModel>(
            new ModelId("RELIC", "THEARCHITECT-NUREMBERG_EGG")).ToMutable();
        egg.Owner = player;
        var holder = NRelicInventoryHolder.Create(egg) ??
            throw new InvalidOperationException("Cannot create the native Egg inventory holder.");
        NGame.Instance!.AddChild(holder);
        try
        {
            holder.Position = new Vector2(450, 270);
            holder.Scale = Vector2.One * 2;
            await NGame.Instance.AwaitProcessFrame();
            var counter = (Control)AccessTools.Field(typeof(NRelicInventoryHolder), "_amountLabel").GetValue(holder)!;
            await egg.BeforeCombatStart();
            var card = ModelDb.Card<DefendIronclad>().ToMutable();
            card.Owner = player;
            CardPlay Play(int index = 0, int count = 1) => new()
            {
                Card = card, Player = player, PlayIndex = index, PlayCount = count,
                Target = null, IsAutoPlay = false, ResultPile = PileType.Discard, Resources = default
            };
            for (var i = 0; i < 11; i++)
                await egg.BeforeCardPlayed(Play());
            Require(counter.Visible && egg.DisplayAmount == 11, "Native Egg counter is visible while building to twelve.");
            await NativeDemoPlaytest.Capture("egg-counter-before");
            await egg.BeforeCardPlayed(Play(0, 3));
            await egg.AfterCardPlayed(new ThrowingPlayerChoiceContext(), Play(0, 3));
            await egg.AfterCardPlayed(new ThrowingPlayerChoiceContext(), Play(1, 3));
            Require(counter.Visible, "Native Egg counter remains visible until the replay sequence finishes.");
            await egg.AfterCardPlayed(new ThrowingPlayerChoiceContext(), Play(2, 3));
            Require(!counter.Visible && !egg.ShowCounter, "Final replay hides the actual native Egg counter.");
            await NativeDemoPlaytest.Capture("egg-counter-spent");
            await egg.AfterCombatEnd(null!);
            Require(!counter.Visible, "Native Egg counter remains hidden between combats.");
            await egg.BeforeCombatStart();
            Require(counter.Visible && egg.DisplayAmount == 0, "Next combat restores the actual native counter at zero.");
            await NativeDemoPlaytest.Capture("egg-counter-rearmed");
        }
        finally
        {
            holder.QueueFree();
        }
    }

    internal static async Task Run(Player player)
    {
        Require(NativeDemoSafety.Enabled, "Watcher probes require disposable native-test storage.");
        Require(player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && CombatManager.Instance.IsInProgress,
            "Run Watcher probes after the legacy combat smoke, during a live player turn.");
        Require(ReferenceEquals(_openingScryCombat, player.Creature.CombatState),
            "OpeningScry must wrap native combat entry before the legacy combat smoke.");
        foreach (var relic in player.Relics.ToArray())
            await RelicCmd.Remove(relic);
        await CreatureCmd.SetMaxHp(player.Creature, 10000);
        await CreatureCmd.Heal(player.Creature, 10000);
        player.Creature.RemoveAllPowersInternalExcept();

        await WatcherTooltipPlaytest.Run(player);
        await Egg(player);
        await Stances(player);
        await EnergyAfterReshuffle(player);
        await PickupsAndOpeningPowers(player);
        await NativeDemoPlaytest.Capture("watcher-mechanics");
        MainFile.Logger.Info("WATCHER RELICS SMOKE PASSED: native Scry selection, replay/exhaust, stances, reshuffle energy, pickups and serialization.");
    }

    internal static async Task OpeningScry(Player player, Func<Task> enterCombat)
    {
        Require(NativeDemoSafety.Enabled, "Opening Scry requires disposable native-test storage.");
        _openingScryCombat = null;
        var eye = (GoldenEye)await Obtain("GOLDEN_EYE", player);
        try
        {
            var entering = enterCombat();
            await WaitFor(() =>
            {
                if (entering.IsFaulted) entering.GetAwaiter().GetResult();
                return SelectionScreen() != null;
            });
            var combat = player.PlayerCombatState!;
            Require(combat.TurnNumber == 1 && combat.Hand.IsEmpty,
                "Golden Eye's real combat-opening choice appears before the first normal hand draw.");
            var original = combat.DrawPile.Cards.ToArray();
            var screen = SelectionScreen()!;
            var grid = screen.FindChildren("*", recursive: true, owned: false).OfType<NCardGrid>().Single();
            await WaitFor(() => grid.CurrentlyDisplayedCards.Count() == Math.Min(8, original.Length));
            Require(original.Length >= 8 && grid.CurrentlyDisplayedCards.SequenceEqual(original.Take(8)),
                "Native opening Scry displays the top eight in their actual draw order.");
            var selected = new[] { original[1], original[5] };
            var click = AccessTools.Method(typeof(NSimpleCardSelectScreen), "OnCardClicked");
            foreach (var card in selected)
                click.Invoke(screen, [card]);
            await NativeDemoPlaytest.Capture("watcher-opening-scry");
            var confirm = (NButton)AccessTools.Field(typeof(NSimpleCardSelectScreen), "_confirmButton").GetValue(screen)!;
            confirm.EmitSignal(NClickableControl.SignalName.Released, confirm);
            await entering;
            await NativeDemoPlaytest.PlayerTurn(player, 1);
            await WaitFor(() => SelectionScreen() == null);
            var remaining = original.Except(selected).ToArray();
            Require(combat.DiscardPile.Cards.SequenceEqual(selected) &&
                combat.Hand.Cards.SequenceEqual(remaining.Take(combat.Hand.Cards.Count)) &&
                combat.DrawPile.Cards.SequenceEqual(remaining.Skip(combat.Hand.Cards.Count)),
                "Native opening draw follows Scry, retaining unselected order and discarding only the chosen cards.");
            await NativeDemoPlaytest.Capture("watcher-opening-hand");
            _openingScryCombat = player.Creature.CombatState;
        }
        finally
        {
            await RelicCmd.Remove(eye);
        }
    }

    private static async Task Egg(Player player)
    {
        await ClearCombatCards(player);
        var egg = (NurembergEgg)await Obtain("NUREMBERG_EGG", player);
        await egg.BeforeCombatStart();
        NRelicInventoryHolder? holder = null;
        await WaitFor(() =>
        {
            holder = NGame.Instance!.FindChildren("*", recursive: true, owned: false)
                .OfType<NRelicInventoryHolder>().SingleOrDefault(candidate =>
                    ReferenceEquals(AccessTools.Field(typeof(NRelicInventoryHolder), "_subscribedRelic")
                        .GetValue(candidate), egg));
            return holder != null;
        });
        var counter = (Control)AccessTools.Field(typeof(NRelicInventoryHolder), "_amountLabel").GetValue(holder)!;
        Require(counter.Visible, "Egg counter is visible before its trigger.");
        var context = new ThrowingPlayerChoiceContext();
        var permanent = player.Deck.Cards.Select(card => card.ToSerializable()).ToArray();
        CardModel? twelfth = null;
        for (var number = 1; number <= 13; number++)
        {
            var card = Card<DefendIronclad>(player);
            await CardPileCmd.Add(card, player.PlayerCombatState!.Hand);
            await CardCmd.AutoPlay(context, card, null);
            var plays = CombatManager.Instance.History.CardPlaysStarted.Count(entry => entry.CardPlay.Card == card);
            Require(plays == (number == 12 ? 3 : 1), $"Native Egg card {number} resolves the correct number of plays.");
            Require(card.Pile?.Type == (number == 12 ? PileType.Exhaust : PileType.Discard),
                $"Native Egg card {number} resolves into the correct final pile.");
            Require(egg.ShowCounter == (number < 12) && counter.Visible == egg.ShowCounter,
                $"Native Egg card {number} refreshes the counter's actual visibility.");
            if (number == 12) twelfth = card;
        }
        Require(egg.DisplayAmount == 12 && twelfth!.Keywords.Contains(CardKeyword.Exhaust),
            "Native replays neither advance nor retrigger the Egg; original retains combat-only Exhaust.");
        Require(player.Deck.Cards.Select(card => card.Id).SequenceEqual(permanent.Select(card => card.Id)) &&
            player.Deck.Cards.All(card => !card.Keywords.Contains(CardKeyword.Exhaust) ||
                CardModel.FromSerializable(permanent[player.Deck.Cards.ToList().IndexOf(card)]).Keywords.Contains(CardKeyword.Exhaust)),
            "Egg leaves permanent deck cards and their saved Exhaust state untouched.");
        await NativeDemoPlaytest.Capture("watcher-egg");
        await egg.AfterCombatEnd(null!);
        Require(egg.DisplayAmount == 0 && !twelfth!.Keywords.Contains(CardKeyword.Exhaust),
            "Egg lifecycle reset clears its counter and temporary keyword.");
        Require(!egg.ShowCounter && !counter.Visible, "Spent Egg counter stays hidden after combat.");
        await egg.BeforeCombatStart();
        Require(egg.ShowCounter && counter.Visible && egg.DisplayAmount == 0,
            "Next combat restores the native Egg counter at zero.");
        await RelicCmd.Remove(egg);
    }

    private static async Task Stances(Player player)
    {
        await ClearCombatCards(player);
        player.Creature.RemoveAllPowersInternalExcept();
        var context = new ThrowingPlayerChoiceContext();
        async Task Enchanted(EnchantmentModel enchantment)
        {
            var card = Card<DefendIronclad>(player);
            CardCmd.Enchant(enchantment.ToMutable(), card, 1);
            await CardCmd.AutoPlay(context, card, null);
        }
        var energy = player.PlayerCombatState!.Energy;
        await Enchanted(ArchitectModels.CalmEnchantment);
        await Enchanted(ArchitectModels.CalmEnchantment);
        Require(player.Creature.GetPower<CalmStancePower>() != null &&
            player.Creature.GetPower<WrathStancePower>() == null && player.PlayerCombatState.Energy == energy,
            "Native enchanted plays enter Calm; repeating Calm pays no Energy.");
        await Enchanted(ArchitectModels.WrathEnchantment);
        await Enchanted(ArchitectModels.WrathEnchantment);
        Require(player.Creature.GetPower<CalmStancePower>() == null &&
            player.Creature.GetPower<WrathStancePower>() != null && player.PlayerCombatState.Energy == energy + 1,
            "Native Calm-to-Wrath transition pays exactly one Energy; repeated Wrath does not stack.");

        var target = player.Creature.CombatState!.HittableEnemies.First();
        if (target.GetPower<StrengthPower>() is { } strength)
            await PowerCmd.Remove(strength);
        target.LoseBlockInternal(target.Block);
        var hp = target.CurrentHp;
        var strike = Card<StrikeIronclad>(player);
        await CardCmd.AutoPlay(context, strike, target);
        Require(hp - target.CurrentHp == 9, "Native Strike deals nine damage in Wrath instead of six.");
        player.Creature.LoseBlockInternal(player.Creature.Block);
        hp = player.Creature.CurrentHp;
        await CreatureCmd.Damage(context, player.Creature, 10, ValueProp.Move, target, null, null);
        Require(hp - player.Creature.CurrentHp == 15,
            $"Native enemy damage is multiplied by 1.5 in Wrath (actual damage: {hp - player.Creature.CurrentHp}).");
        await Enchanted(ArchitectModels.CalmEnchantment);
        player.Creature.LoseBlockInternal(player.Creature.Block);
        hp = player.Creature.CurrentHp;
        await CreatureCmd.Damage(context, player.Creature, 10, ValueProp.Move, target, null, null);
        Require(hp - player.Creature.CurrentHp == 10 && player.PlayerCombatState.Energy == energy + 1,
            "Leaving Wrath immediately restores ordinary incoming damage without an extra Energy payout.");
        await NativeDemoPlaytest.Capture("watcher-stances");
        player.Creature.RemoveAllPowersInternalExcept();
    }

    private static async Task EnergyAfterReshuffle(Player player)
    {
        await ClearCombatCards(player);
        var deva = (Relics.DevaForm)await Obtain("DEVA_FORM", player);
        await deva.BeforeCombatStart();
        var combat = player.PlayerCombatState!;
        var context = new ThrowingPlayerChoiceContext();
        for (var i = 0; i < 20; i++)
            await CardPileCmd.Add(Card<DefendIronclad>(player), combat.DrawPile);
        deva.ModifyShuffleOrder(player, combat.DrawPile.Cards.ToList(), true);
        Require(!deva.GrantsEnergy(player), "Initial setup shuffling never activates Deva Form.");
        await CardPileCmd.Add(combat.DrawPile.Cards[0], combat.DiscardPile);
        var energy = combat.Energy;
        await CardPileCmd.Shuffle(context, player);
        Require(combat.Energy == energy && !deva.GrantsEnergy(player),
            "First real mid-combat reshuffle does not grant immediate Energy.");
        for (var extraTurn = 0; extraTurn < 2; extraTurn++)
        {
            var nextTurn = combat.TurnNumber + 1;
            await CreatureCmd.Heal(player.Creature, 10000);
            PlayerCmd.EndTurn(player, false);
            await NativeDemoPlaytest.PlayerTurn(player, nextTurn);
            var voidPenalty = combat.Hand.Cards.OfType<MegaCrit.Sts2.Core.Models.Cards.Void>()
                .Sum(card => card.DynamicVars.Energy.IntValue);
            Require(combat.Energy == player.MaxEnergy + 1 - voidPenalty,
                $"Deva grants flat +1 Energy on native turn {nextTurn}, after native draw penalties.");
            await CardPileCmd.Shuffle(context, player);
        }
        await NativeDemoPlaytest.Capture("watcher-deva");
        await RelicCmd.Remove(deva);
    }

    private static async Task PickupsAndOpeningPowers(Player player)
    {
        player.Creature.RemoveAllPowersInternalExcept();
        var gold = player.Gold;
        var wish = (TheLastWish)await Obtain("THE_LAST_WISH", player);
        Require(player.Gold == gold + 99, "The Last Wish's real acquisition grants exactly 99 Gold.");
        await wish.BeforeCombatStart();
        var dagger = (Relics.RitualDagger)await Obtain("RITUAL_DAGGER", player);
        await dagger.BeforeCombatStart();
        Require(player.Creature.GetPowerAmount<PlatingPower>() == 4 &&
            player.Creature.GetPowerAmount<StrengthPower>() == 1 &&
            player.Creature.GetPowerAmount<RitualPower>() == 1 &&
            player.Creature.GetPowerAmount<VulnerablePower>() == 3,
            "Native opening hooks apply 4 Plating, 1 Strength, 1 Ritual and ordinary 3 Vulnerable.");
        var before = player.Deck.Cards.ToDictionary(card => card, card => card.Enchantment?.Id);
        Require(DeusExMachina.CanReceive(player) && VioletLotus.CanReceive(player),
            "Disposable base deck is eligible for both enchantment pickups.");
        await Obtain("DEUS_EX_MACHINA", player);
        await Obtain("VIOLET_LOTUS", player);
        Require(player.Deck.Cards.Count(card => card.Enchantment is Sown) == 3 &&
            player.Deck.Cards.Count(card => card.Enchantment is Wrath) == 2 &&
            player.Deck.Cards.Count(card => card.Enchantment is Calm) == 2,
            "Real pickup commands enchant three Sown and four different stance cards.");
        Require(before.Where(pair => pair.Value != null).All(pair => pair.Key.Enchantment?.Id == pair.Value),
            "Native random enchantment pickups never replace existing enchantments.");
        foreach (var card in player.Deck.Cards.Where(card => card.Enchantment != null))
        {
            var restored = CardModel.FromSerializable(card.ToSerializable());
            Require(restored.Enchantment?.Id == card.Enchantment!.Id &&
                restored.Enchantment?.Amount == card.Enchantment.Amount,
                $"Native serialized deck retains {card.Enchantment.Id}.");
        }
    }

    private static CardModel Card<T>(Player player) where T : CardModel =>
        player.Creature.CombatState!.CreateCard<T>(player);

    private static async Task ClearCombatCards(Player player)
    {
        // Move through the native command first so the human hand's visual holders leave with the cards.
        await CardPileCmd.Add(player.PlayerCombatState!.Hand.Cards.ToArray(),
            player.PlayerCombatState.DiscardPile);
        foreach (var pile in player.PlayerCombatState!.AllPiles)
            foreach (var card in pile.Cards.ToArray())
            {
                pile.RemoveInternal(card);
                player.Creature.CombatState!.RemoveCard(card);
            }
    }

    private static async Task<RelicModel> Obtain(string entry, Player player)
    {
        var relic = ModelDb.GetById<RelicModel>(new ModelId("RELIC", $"THEARCHITECT-{entry}")).ToMutable();
        await RelicCmd.Obtain(relic, player);
        return relic;
    }

    private static NSimpleCardSelectScreen? SelectionScreen() =>
        NGame.Instance!.FindChildren("*", recursive: true, owned: false)
            .OfType<NSimpleCardSelectScreen>().SingleOrDefault(screen => screen.IsVisibleInTree());

    private static async Task WaitFor(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (!ready())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Watcher native UI did not complete in 45 seconds.");
            await NGame.Instance!.AwaitProcessFrame();
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Watcher: " + message);
        MainFile.Logger.Info("WATCHER PASS: " + message);
    }
}
