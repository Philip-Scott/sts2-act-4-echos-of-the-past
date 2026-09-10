using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using TheArchitect.TheArchitectCode.Relics;

internal static class UnwrittenBonusTests
{
    internal static void Run(Action<string, Action> test)
    {
        ModelDb.Inject(typeof(Glam));
        ModelDb.Inject(typeof(IroncladCardPool));
        ModelDb.Inject(typeof(Ironclad));
        ModelDb.Inject(typeof(DemonForm));
        ModelDb.Inject(typeof(StrikeIronclad));

        test("Unwritten: seven bonuses are Ancient relics with approved dynamic values", () =>
        {
            (RelicModel Relic, string Key, int Amount)[] values =
            [
                (Canonical<LooseThread>(), "Cards", 1),
                (Canonical<LooseThread>(), "Turns", 3),
                (Canonical<CrookedNeedle>(), "StrengthPower", 1),
                (Canonical<CrookedNeedle>(), "DexterityPower", 1),
                (Canonical<OrangePearl>(), "ArtifactPower", 1),
                (Canonical<DiamondHand>(), "Cards", 1),
                (Canonical<UnspentPossibility>(), "Gold", 150),
                (Canonical<LastMeal>(), "MaxHp", 20),
                (Canonical<LastMeal>(), "Potions", 2),
                (Canonical<LastMeal>(), "Cards", 3),
                (Canonical<BorrowedTomorrow>(), "Energy", 1),
                (Canonical<BorrowedTomorrow>(), "Turns", 3),
                (Canonical<BorrowedTomorrow>(), "VulnerablePower", 2)
            ];
            foreach (var (relic, key, amount) in values)
            {
                Check(relic is UnwrittenRelic && relic.Rarity == RelicRarity.Ancient);
                Check(relic.DynamicVars[key].IntValue == amount, $"{relic.GetType().Name}.{key}");
            }
        });

        test("Loose Thread: native owner turns include extra turns, not other players' turns", () =>
        {
            var owner = Player();
            var other = Player();
            var relic = Owned<LooseThread>(owner);
            for (var turn = 1; turn <= 5; turn++)
            {
                Check(owner.PlayerCombatState!.TurnNumber == turn);
                Check(relic.ModifyHandDraw(owner, 5) == (turn <= 3 ? 6 : 5));
                Check(relic.ModifyHandDraw(other, 5) == 5);
                other.PlayerCombatState!.IncrementTurnNumber();
                Check(owner.PlayerCombatState.TurnNumber == turn);
                // The native turn loop also increments this counter for an extra owner turn.
                owner.PlayerCombatState.IncrementTurnNumber();
            }
            relic.BeforeCombatStart().GetAwaiter().GetResult();
            Check(relic.ModifyHandDraw(owner, 5) == 5, "Relic hooks must not reset native turn state.");
        });

        test("Loose Thread: modifies normal draw count without drawing independently", () =>
        {
            var owner = Player();
            var relic = Owned<LooseThread>(owner);
            Check(relic.ModifyHandDraw(owner, 0) == 1);
            Check(relic.ModifyHandDraw(owner, 10) == 11,
                "The native draw command, not the relic, applies draw prevention and hand caps.");
        });

        test("Diamond Hand: other-player and empty-hand triggers spend no owner RNG", () =>
        {
            var owner = Player();
            var other = Player();
            var relic = Owned<DiamondHand>(owner);
            var expected = new PlayerRngSet(100);
            relic.AfterPlayerTurnStart(null!, other).GetAwaiter().GetResult();
            relic.AfterPlayerTurnStart(null!, owner).GetAwaiter().GetResult();
            owner.PlayerCombatState!.IncrementTurnNumber();
            relic.AfterPlayerTurnStart(null!, owner).GetAwaiter().GetResult();
            Check(owner.PlayerRng.Rewards.NextInt(1000) == expected.Rewards.NextInt(1000));
        });

        test("Diamond Hand: skips later turns even if the opening hand had no eligible card", () =>
        {
            var owner = Player();
            var relic = Owned<DiamondHand>(owner);
            var expected = new PlayerRngSet(100);
            relic.AfterPlayerTurnStart(null!, owner).GetAwaiter().GetResult();

            var card = ModelDb.Card<StrikeIronclad>().ToMutable();
            card.Owner = owner;
            owner.PlayerCombatState!.Hand.AddInternal(card, silent: true);
            Check(card.Enchantment == null && ModelDb.Enchantment<Glam>().CanEnchant(card));
            for (var turn = 2; turn <= 5; turn++)
            {
                owner.PlayerCombatState.IncrementTurnNumber();
                relic.AfterPlayerTurnStart(null!, owner).GetAwaiter().GetResult();
                Check(card.Enchantment == null, $"No Glam on turn {turn}.");
            }
            Check(owner.PlayerRng.Rewards.NextInt(1000) == expected.Rewards.NextInt(1000));
        });

        test("Borrowed Tomorrow: energy hook ignores other players and turns after three", () =>
        {
            var owner = Player();
            var relic = Owned<BorrowedTomorrow>(owner);
            relic.AfterEnergyReset(Player()).GetAwaiter().GetResult();
            for (var i = 0; i < 3; i++)
                owner.PlayerCombatState!.IncrementTurnNumber();
            relic.AfterEnergyReset(owner).GetAwaiter().GetResult();
            Check(owner.PlayerCombatState!.Energy == 0);
        });

        test("Last Meal: native bundle has two potion rewards and one skippable three-card rare reward", () =>
        {
            var owner = Player();
            SetBackingField(owner, "Character", ModelDb.Character<Ironclad>());
            var rewards = Owned<LastMeal>(owner).CreateRewards();
            Check(rewards.Count == 3 && rewards.OfType<PotionReward>().Count() == 2, "Reward counts.");
            var cards = rewards.OfType<CardReward>().Single();
            var options = GetBackingField<CardCreationOptions>(cards, "Options");
            Check(GetBackingField<int>(cards, "OptionCount") == 3 && cards.CanSkip, "Option count and skipping.");
            Check(options.CardPools.Single() == owner.Character.CardPool, "Owner character pool.");
            var filter = options.CardPoolFilter ?? throw new InvalidOperationException("Missing rare-card filter.");
            Check(filter(ModelDb.Card<DemonForm>()) && !filter(ModelDb.Card<StrikeIronclad>()), "Rare-only filter.");
        });
    }

    private static T Canonical<T>() where T : RelicModel, new()
    {
        ModelDb.Inject(typeof(T));
        return ModelDb.Relic<T>();
    }

    private static T Owned<T>(Player owner) where T : RelicModel, new()
    {
        var relic = (T)Canonical<T>().ToMutable();
        relic.Owner = owner;
        return relic;
    }

    private static Player Player()
    {
        // These hook tests do not bootstrap Godot, saves, or a live RunManager.
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        var combat = (PlayerCombatState)RuntimeHelpers.GetUninitializedObject(typeof(PlayerCombatState));
        SetBackingField(combat, "TurnNumber", 1);
        SetBackingField(combat, "Hand", new CardPile(PileType.Hand));
        SetBackingField(player, "PlayerCombatState", combat);
        SetBackingField(player, "PlayerRng", new PlayerRngSet(100));
        return player;
    }

    private static void SetBackingField(object target, string property, object value) =>
        target.GetType().GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);

    private static T GetBackingField<T>(object target, string property) =>
        (T)target.GetType().GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(target)!;

    private static void Check(bool condition, string message = "Assertion failed")
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
