using System.Reflection;
using System.Runtime.CompilerServices;
using BaseLib.Extensions;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.ValueProps;
using TheArchitect.TheArchitectCode.Enchantments;
using TheArchitect.TheArchitectCode.Powers;
using TheArchitect.TheArchitectCode.Relics;
using DevaFormRelic = TheArchitect.TheArchitectCode.Relics.DevaForm;
using RitualDaggerRelic = TheArchitect.TheArchitectCode.Relics.RitualDagger;

internal static class WatcherRelicTests
{
    private static readonly ThrowingPlayerChoiceContext Context = new();
    private static Func<PlayerChoiceContext, IReadOnlyList<CardModel>, Player, CardSelectorPrefs,
        IEnumerable<CardModel>>? _scryChoice;

    internal static void Run(Action<string, Action> test)
    {
        foreach (var type in new[]
        {
            typeof(TheLastWish), typeof(GoldenEye), typeof(DeusExMachina), typeof(NurembergEgg),
            typeof(RitualDaggerRelic), typeof(VioletLotus), typeof(DevaFormRelic),
            typeof(Wrath), typeof(Calm), typeof(WrathStancePower), typeof(CalmStancePower),
            typeof(Sown), typeof(Glam), typeof(StrikeIronclad), typeof(DefendIronclad),
            typeof(DemonForm), typeof(Wound), typeof(PlatingPower), typeof(StrengthPower),
            typeof(RitualPower), typeof(VulnerablePower), typeof(ArtifactPower)
        })
            ModelDb.Inject(type);

        WatcherTooltipTests.Run(test);

        test("Watcher: Ancient rarity, pickup effects, and exact native power amounts", () =>
        {
            (RelicModel Relic, string Key, int Amount)[] values =
            [
                (ModelDb.Relic<TheLastWish>(), "Gold", 99),
                (ModelDb.Relic<TheLastWish>(), "PlatingPower", 4),
                (ModelDb.Relic<TheLastWish>(), "StrengthPower", 1),
                (ModelDb.Relic<GoldenEye>(), "Scry", 8),
                (ModelDb.Relic<DeusExMachina>(), "Cards", 3),
                (ModelDb.Relic<NurembergEgg>(), "Cards", 12),
                (ModelDb.Relic<NurembergEgg>(), "Replay", 2),
                (ModelDb.Relic<RitualDaggerRelic>(), "RitualPower", 1),
                (ModelDb.Relic<RitualDaggerRelic>(), "VulnerablePower", 3),
                (ModelDb.Relic<VioletLotus>(), "Cards", 2),
                (ModelDb.Relic<DevaFormRelic>(), "Energy", 1)
            ];
            foreach (var (relic, key, amount) in values)
                Check(relic.Rarity == RelicRarity.Ancient && relic.DynamicVars[key].IntValue == amount);
            Check(ModelDb.Relic<TheLastWish>().HasUponPickupEffect);
            Check(ModelDb.Relic<DeusExMachina>().HasUponPickupEffect);
            Check(ModelDb.Relic<VioletLotus>().HasUponPickupEffect);
        });

        test("Watcher: combat-start buffs apply only to their owner with native power types", () =>
        {
            WithHeadlessCommands(() =>
            {
                var owner = Player();
                var other = Player();
                Owned<TheLastWish>(owner).BeforeCombatStart().GetAwaiter().GetResult();
                Owned<RitualDaggerRelic>(owner).BeforeCombatStart().GetAwaiter().GetResult();
                Check(owner.Creature.GetPower<PlatingPower>()?.Amount == 4);
                Check(owner.Creature.GetPower<StrengthPower>()?.Amount == 1);
                Check(owner.Creature.GetPower<RitualPower>()?.Amount == 1);
                Check(owner.Creature.GetPower<VulnerablePower>()?.Amount == 3);
                Check(owner.Creature.GetPower<VulnerablePower>()?.GetType() == typeof(VulnerablePower));
                Check(other.Creature.Powers.Count == 0);
            });
        });

        test("Watcher enchantments: seeded selection is distinct, eligible, and owner-only", () =>
        {
            var first = Player();
            var second = Player();
            foreach (var owner in new[] { first, second })
            {
                for (var i = 0; i < 9; i++) Card<StrikeIronclad>(owner, owner.Deck);
                Card<Wound>(owner, owner.Deck);
                var enchanted = Card<StrikeIronclad>(owner, owner.Deck);
                enchanted.EnchantInternal(ModelDb.Enchantment<Glam>().ToMutable(), 1);
                var foreign = Card<StrikeIronclad>(Player(), owner.Deck);
                Check(foreign.Owner != owner);
            }
            var selected = RandomDeckEnchantments.Select(first, ModelDb.Enchantment<Sown>(), 3);
            var repeated = RandomDeckEnchantments.Select(second, ModelDb.Enchantment<Sown>(), 3);
            Check(selected.Count == 3 && selected.Distinct().Count() == 3);
            Check(selected.All(c => c.Owner == first && c.Enchantment == null && c.Type == CardType.Attack));
            Check(selected.Select(c => first.Deck.Cards.ToList().IndexOf(c))
                .SequenceEqual(repeated.Select(c => second.Deck.Cards.ToList().IndexOf(c))));
            Check(first.PlayerRng.Rewards.NextInt(1000) == second.PlayerRng.Rewards.NextInt(1000));
        });

        test("Watcher enchantments: empty and zero-count selections spend no RNG; small decks are safe", () =>
        {
            var owner = Player();
            var expected = new PlayerRngSet(987UL);
            Check(RandomDeckEnchantments.Select(owner, ModelDb.Enchantment<Sown>(), 3).Count == 0);
            Card<Wound>(owner, owner.Deck);
            Check(RandomDeckEnchantments.Select(owner, ModelDb.Enchantment<Sown>(), 3).Count == 0);
            Card<StrikeIronclad>(owner, owner.Deck);
            Check(RandomDeckEnchantments.Select(owner, ModelDb.Enchantment<Sown>(), 0).Count == 0);
            Check(owner.PlayerRng.Rewards.NextInt(1000) == expected.Rewards.NextInt(1000));
            Check(RandomDeckEnchantments.Select(owner, ModelDb.Enchantment<Sown>(), 3).Count == 1);
        });

        test("Watcher enchantment offers: require three or four eligible cards without spending RNG", () =>
        {
            var owner = Player();
            var expected = new PlayerRngSet(987UL);
            Card<Wound>(owner, owner.Deck);
            var enchanted = Card<StrikeIronclad>(owner, owner.Deck);
            enchanted.EnchantInternal(ModelDb.Enchantment<Glam>().ToMutable(), 1);
            Card<StrikeIronclad>(Player(), owner.Deck);
            for (var count = 0; count <= 4; count++)
            {
                Check(DeusExMachina.CanReceive(owner) == (count >= 3));
                Check(VioletLotus.CanReceive(owner) == (count >= 4));
                if (count < 4) Card<StrikeIronclad>(owner, owner.Deck);
            }
            Check(owner.PlayerRng.Rewards.NextInt(1000) == expected.Rewards.NextInt(1000));
        });

        test("Watcher enchantment offers: BaseLib native pools apply owner gates and preserve reusable pools", () =>
        {
            var owner = Player();
            var ancient = (Neow)RuntimeHelpers.GetUninitializedObject(typeof(Neow));
            typeof(EventModel).GetProperty(nameof(EventModel.Owner))!.SetValue(ancient, owner);
            var deus = ModelDb.Relic<DeusExMachina>();
            var lotus = ModelDb.Relic<VioletLotus>();
            Check(!deus.RelicCanSpawnAtCustomAncient(ancient) && !lotus.RelicCanSpawnAtCustomAncient(ancient));
            var pool = new WeightedList<AncientOption>
            {
                (AncientOption)ModelDb.Relic<TheLastWish>(),
                (AncientOption)deus,
                (AncientOption)lotus
            };
            var pools = new OptionPools(pool,
                new WeightedList<AncientOption> { (AncientOption)ModelDb.Relic<GoldenEye>() },
                new WeightedList<AncientOption> { (AncientOption)ModelDb.Relic<NurembergEgg>() });
            Rng Random() => new(new RunRngSet("WATCHER-POOL-TEST").Seed, "THEARCHITECT-THE_UNWRITTEN");
            var first = pools.Roll(Random(), ancient);
            Check(first[0].ModelForOption is TheLastWish);
            Check(first.Select(o => o.ModelForOption.Id).SequenceEqual(
                pools.Roll(Random(), ancient).Select(o => o.ModelForOption.Id)));
            Check(pools.AllOptions.Count() == 5, "Rolling must not permanently discard ineligible options.");
            for (var i = 0; i < 3; i++) Card<StrikeIronclad>(owner, owner.Deck);
            Check(deus.RelicCanSpawnAtCustomAncient(ancient) && !lotus.RelicCanSpawnAtCustomAncient(ancient));
            Card<StrikeIronclad>(owner, owner.Deck);
            Check(deus.RelicCanSpawnAtCustomAncient(ancient) && lotus.RelicCanSpawnAtCustomAncient(ancient));
        });

        test("Watcher enchantments: native application gives three Sown or two Wrath plus two Calm", () =>
        {
            var owner = Player();
            for (var i = 0; i < 10; i++) Card<StrikeIronclad>(owner, owner.Deck);
            var existing = Card<StrikeIronclad>(owner, owner.Deck);
            existing.EnchantInternal(ModelDb.Enchantment<Glam>().ToMutable(), 1);
            var preserved = existing.Enchantment;
            Check(RandomDeckEnchantments.Apply<Sown>(owner, 3).Count == 3);
            Check(RandomDeckEnchantments.Apply<Wrath>(owner, 2).Count == 2);
            Check(RandomDeckEnchantments.Apply<Calm>(owner, 2).Count == 2);
            Check(owner.Deck.Cards.Count(c => c.Enchantment is Sown) == 3);
            Check(owner.Deck.Cards.Count(c => c.Enchantment is Wrath) == 2);
            Check(owner.Deck.Cards.Count(c => c.Enchantment is Calm) == 2);
            Check(ReferenceEquals(existing.Enchantment, preserved));
        });

        test("Watcher enchantments: native card save/load preserves Sown, Wrath, and Calm", () =>
        {
            WithSerialization(() =>
            {
                var owner = Player();
                foreach (var enchantment in new EnchantmentModel[]
                    { ModelDb.Enchantment<Sown>(), ModelDb.Enchantment<Wrath>(), ModelDb.Enchantment<Calm>() })
                {
                    var card = Card<StrikeIronclad>(owner, owner.Deck);
                    card.EnchantInternal(enchantment.ToMutable(), 1);
                    var saved = card.ToSerializable();
                    var loaded = CardModel.FromSerializable(saved);
                    Check(loaded.Enchantment?.GetType() == enchantment.GetType());
                    Check(loaded.Enchantment?.Amount == 1);
                    Check(loaded.Enchantment?.Card == loaded);
                    Check(loaded.Enchantment != card.Enchantment);
                }
            });
        });

        test("Golden Eye: reveals the top eight without changing order or RNG", () =>
        {
            var owner = Player();
            var relic = Owned<GoldenEye>(owner);
            for (var i = 0; i < 10; i++) Card<StrikeIronclad>(owner, owner.PlayerCombatState!.DrawPile);
            var original = owner.PlayerCombatState!.DrawPile.Cards.ToArray();
            var expected = new PlayerRngSet(987UL);
            Check(relic.ScryCards().SequenceEqual(original.Take(8)));
            Check(owner.PlayerCombatState.DrawPile.Cards.SequenceEqual(original));
            Check(owner.PlayerRng.Rewards.NextInt(1000) == expected.Rewards.NextInt(1000));
            relic.BeforeHandDraw(Player(), null!, null!).GetAwaiter().GetResult();
            owner.PlayerCombatState.IncrementTurnNumber();
            relic.BeforeHandDraw(owner, null!, null!).GetAwaiter().GetResult();
        });

        test("Golden Eye: empty opening consumes trigger, lifecycle resets it, small piles are bounded", () =>
        {
            var owner = Player();
            var relic = Owned<GoldenEye>(owner);
            relic.BeforeCombatStart().GetAwaiter().GetResult();
            relic.BeforeHandDraw(owner, null!, null!).GetAwaiter().GetResult();
            Card<StrikeIronclad>(owner, owner.PlayerCombatState!.DrawPile);
            Check(relic.ScryCards().Length == 1);
            // A second call must not enter the selection UI even if the pile has since changed.
            relic.BeforeHandDraw(owner, null!, null!).GetAwaiter().GetResult();
            relic.AfterCombatEnd(null!).GetAwaiter().GetResult();
            Check(!(bool)Field(relic, "_scried")!);
        });

        test("Golden Eye: synchronized optional selection discards chosen cards and preserves the rest in order", () =>
        {
            var owner = Player();
            var relic = Owned<GoldenEye>(owner);
            for (var i = 0; i < 10; i++) Card<StrikeIronclad>(owner, owner.PlayerCombatState!.DrawPile);
            var original = owner.PlayerCombatState!.DrawPile.Cards.ToArray();
            var selections = 0;
            _scryChoice = (context, cards, player, prefs) =>
            {
                selections++;
                Check(ReferenceEquals(context, Context) && player == owner);
                Check(cards.SequenceEqual(original.Take(8)));
                Check(prefs.MinSelect == 0 && prefs.MaxSelect == 8 && prefs.RequireManualConfirmation);
                Check(prefs.Comparison!(cards[0], cards[1]) < 0);
                return [cards[1], cards[5]];
            };
            var harmony = new Harmony("TheArchitect.Tests.ScryChoice");
            var select = typeof(CardSelectCmd).GetMethod(nameof(CardSelectCmd.FromSimpleGrid))!;
            var add = typeof(CardPileCmd).GetMethods().Single(m => m.Name == nameof(CardPileCmd.Add) &&
                m.GetParameters()[0].ParameterType == typeof(IEnumerable<CardModel>) &&
                m.GetParameters()[1].ParameterType == typeof(CardPile));
            try
            {
                harmony.Patch(select, prefix: new HarmonyMethod(typeof(WatcherRelicTests), nameof(SelectScryCards)));
                harmony.Patch(add, prefix: new HarmonyMethod(typeof(WatcherRelicTests), nameof(MoveScryCards)));
                relic.BeforeHandDraw(Player(), Context, null!).GetAwaiter().GetResult();
                Check(selections == 0);
                relic.BeforeHandDraw(owner, Context, null!).GetAwaiter().GetResult();
                relic.BeforeHandDraw(owner, Context, null!).GetAwaiter().GetResult();
                Check(selections == 1);
                Check(owner.PlayerCombatState.DrawPile.Cards.SequenceEqual(original.Where((_, i) => i != 1 && i != 5)));
                Check(owner.PlayerCombatState.DiscardPile.Cards.SequenceEqual(new[] { original[1], original[5] }));
            }
            finally
            {
                harmony.Unpatch(select, HarmonyPatchType.All, harmony.Id);
                harmony.Unpatch(add, HarmonyPatchType.All, harmony.Id);
                _scryChoice = null;
            }
        });

        test("Nuremberg Egg: only original card twelve gets three plays and final Exhaust", () =>
        {
            var owner = Player();
            var relic = Owned<NurembergEgg>(owner);
            var card = Card<StrikeIronclad>(owner, owner.PlayerCombatState!.Hand);
            var discard = new CardLocation(owner, PileType.Discard, CardPilePosition.Bottom);
            for (var number = 1; number <= 15; number++)
            {
                var expected = number == 12 ? 3 : 1;
                for (var preview = 0; preview < 3; preview++)
                    Check(relic.ModifyCardPlayCount(card, null, 1) == expected, "Previews are side-effect free.");
                var destination = relic.ModifyCardPlayResultLocation(card, false, default, discard);
                Check(destination.pileType == (number == 12 ? PileType.Exhaust : PileType.Discard));
                if (number == 12) relic.AfterModifyingCardPlayCount(card).GetAwaiter().GetResult();
                for (var index = 0; index < expected; index++)
                    relic.BeforeCardPlayed(Play(card, index, expected)).GetAwaiter().GetResult();
                Check(relic.DisplayAmount == Math.Min(number, 12));
            }
        });

        test("Nuremberg Egg: other players and repeats never advance the owner's counter", () =>
        {
            var owner = Player();
            var relic = Owned<NurembergEgg>(owner);
            var card = Card<StrikeIronclad>(owner, owner.PlayerCombatState!.Hand);
            var other = Card<StrikeIronclad>(Player());
            for (var i = 0; i < 11; i++) relic.BeforeCardPlayed(Play(card)).GetAwaiter().GetResult();
            for (var i = 0; i < 20; i++)
            {
                relic.BeforeCardPlayed(Play(other)).GetAwaiter().GetResult();
                relic.BeforeCardPlayed(Play(card, 1, 2)).GetAwaiter().GetResult();
            }
            Check(relic.DisplayAmount == 11);
            Check(relic.ModifyCardPlayCount(other, null, 1) == 1);
            Check(relic.ModifyCardPlayCount(card, null, 2) == 4, "Two extra plays preserve other replay bonuses.");
            Check(relic.ModifyCardPlayCount(card, null, 3) == 5);
            relic.BeforeCardPlayed(Play(card, 0, 3)).GetAwaiter().GetResult();
            Check(relic.ModifyCardPlayCount(card, null, 1) == 1, "Already-tripled cards still consume the trigger.");
        });

        test("Nuremberg Egg: Exhaust attaches only to original combat card and resets between combats", () =>
        {
            var owner = Player();
            var relic = Owned<NurembergEgg>(owner);
            var deckCard = Card<DemonForm>(owner, owner.Deck);
            var card = Card<DemonForm>(owner, owner.PlayerCombatState!.Hand);
            for (var i = 0; i < 11; i++) relic.BeforeCardPlayed(Play(card)).GetAwaiter().GetResult();
            var result = relic.ModifyCardPlayResultLocation(card, false, default,
                new CardLocation(owner, PileType.None, CardPilePosition.Bottom));
            Check(result.pileType == PileType.Exhaust, "Even Powers exhaust rather than disappearing.");
            relic.BeforeCardPlayed(Play(card, 0, 3)).GetAwaiter().GetResult();
            var keywords = new HashSet<CardKeyword>();
            Check(relic.TryModifyKeywordsInCombat(card, keywords) && keywords.Contains(CardKeyword.Exhaust));
            Check(!relic.TryModifyKeywordsInCombat(deckCard, new HashSet<CardKeyword>()));
            Check(!deckCard.Keywords.Contains(CardKeyword.Exhaust));
            WithSerialization(() =>
                Check(!CardModel.FromSerializable(card.ToSerializable()).Keywords.Contains(CardKeyword.Exhaust),
                    "Do not save a combat-only keyword."));
            relic.AfterCombatEnd(null!).GetAwaiter().GetResult();
            Check(!relic.TryModifyKeywordsInCombat(card, new HashSet<CardKeyword>()) && relic.DisplayAmount == 0);
            Check(relic.ModifyCardPlayCount(card, null, 1) == 1);
        });

        test("Watcher offers: Discipline eligibility does not change later pool RNG consumption", () =>
        {
            for (var seed = 0; seed < 100; seed++)
            {
                int RollTranscendence(int eligibleDiscipline)
                {
                    var rng = new Rng(new RunRngSet($"WATCHER-SEED-{seed}").Seed, "THEARCHITECT-THE_UNWRITTEN");
                    rng.NextInt(4);
                    rng.NextInt(eligibleDiscipline);
                    return rng.NextInt(4);
                }
                Check(RollTranscendence(2) == RollTranscendence(4));
                Check(RollTranscendence(3) == RollTranscendence(4));
            }
        });

        test("Deva Form: skips initial shuffle, owner-gates, waits one turn and never stacks", () =>
        {
            var owner = Player();
            var relic = Owned<DevaFormRelic>(owner);
            relic.ModifyShuffleOrder(owner, [], true);
            relic.ModifyShuffleOrder(Player(), [], false);
            Check(!relic.GrantsEnergy(owner));
            owner.PlayerCombatState!.IncrementTurnNumber();
            Check(!relic.GrantsEnergy(owner));
            relic.ModifyShuffleOrder(owner, [], false);
            Check(!relic.GrantsEnergy(owner));
            owner.PlayerCombatState.IncrementTurnNumber();
            Check(relic.GrantsEnergy(owner) && !relic.GrantsEnergy(Player()));
            relic.ModifyShuffleOrder(owner, [], false);
            Check(relic.GrantsEnergy(owner), "Subsequent reshuffles neither postpone nor stack the bonus.");
            relic.AfterCombatEnd(null!).GetAwaiter().GetResult();
            Check(!relic.GrantsEnergy(owner));
        });

        test("Wrath: boosts only owner's Attacks and enemy damage received by the owner", () =>
        {
            var owner = Player();
            var other = Player();
            var enemy = new Creature(new TenHpMonster().ToMutable(), CombatSide.Enemy, null);
            var wrath = (WrathStancePower)ModelDb.Power<WrathStancePower>().ToMutable();
            wrath.ApplyInternal(owner.Creature, 1);
            var attack = Card<StrikeIronclad>(owner);
            var skill = Card<DefendIronclad>(owner);
            decimal Factor(Creature target, Creature? dealer, CardModel? source, ValueProp props = ValueProp.Move) =>
                wrath.ModifyDamageMultiplicative(target, 10, props, dealer, source, null);
            Check(Factor(enemy, owner.Creature, attack) == 1.5m);
            Check(Factor(enemy, other.Creature, attack) == 1m);
            Check(Factor(enemy, owner.Creature, skill) == 1m);
            Check(Factor(enemy, owner.Creature, attack, ValueProp.Unpowered) == 1m);
            Check(Factor(owner.Creature, enemy, null) == 1.5m);
            Check(Factor(owner.Creature, enemy, null, ValueProp.Unpowered) == 1.5m);
            Check(Factor(other.Creature, enemy, null) == 1m);
            Check(Factor(owner.Creature, other.Creature, attack) == 1m);
            Check(Factor(owner.Creature, null, null, ValueProp.Unpowered | ValueProp.Unblockable) == 1m);
        });

        test("Watcher stances: transitions persist, exclude each other and pay once when leaving Calm", () =>
        {
            WithHeadlessCommands(() =>
            {
                var owner = Player();
                var card = Card<StrikeIronclad>(owner);
                WatcherStances.EnterCalm(Context, card).GetAwaiter().GetResult();
                Check(owner.Creature.GetPower<CalmStancePower>() != null);
                Check(owner.Creature.GetPower<WrathStancePower>() == null && owner.PlayerCombatState!.Energy == 0);
                WatcherStances.EnterCalm(Context, card).GetAwaiter().GetResult();
                Check(owner.PlayerCombatState!.Energy == 0);
                owner.PlayerCombatState.IncrementTurnNumber();
                Check(owner.Creature.GetPower<CalmStancePower>() != null);
                WatcherStances.EnterWrath(Context, card).GetAwaiter().GetResult();
                Check(owner.Creature.GetPower<CalmStancePower>() == null);
                Check(owner.Creature.GetPower<WrathStancePower>() != null && owner.PlayerCombatState.Energy == 1);
                WatcherStances.EnterWrath(Context, card).GetAwaiter().GetResult();
                Check(owner.PlayerCombatState.Energy == 1);
                WatcherStances.EnterCalm(Context, card).GetAwaiter().GetResult();
                Check(owner.Creature.GetPower<WrathStancePower>() == null && owner.PlayerCombatState.Energy == 1);
                WatcherStances.EnterWrath(Context, card).GetAwaiter().GetResult();
                Check(owner.PlayerCombatState.Energy == 2);
                owner.Creature.RemoveAllPowersInternalExcept().ToArray();
                Check(owner.Creature.GetPower<WrathStancePower>() == null);
            });
        });

        test("Watcher stance visuals: model-only lifecycle needs no Godot view and changes no gameplay state", () =>
        {
            var owner = Player();
            var other = Player();
            PowerModel[] powers = [ModelDb.Power<CalmStancePower>().ToMutable(),
                ModelDb.Power<WrathStancePower>().ToMutable()];
            var previousMode = TestMode.IsOn;
            var mode = AccessTools.Property(typeof(TestMode), nameof(TestMode.IsOn));
            mode.SetValue(null, true);
            try
            {
                foreach (var power in powers)
                {
                    power.ApplyInternal(owner.Creature, 1);
                    power.AfterApplied(null, null).GetAwaiter().GetResult();
                    power.AfterApplied(null, null).GetAwaiter().GetResult();
                    power.AfterCombatEnd(null!).GetAwaiter().GetResult();
                    power.RemoveInternal();
                }
                Check(owner.Creature.CurrentHp == 100 && owner.PlayerCombatState!.Energy == 0);
                Check(owner.Creature.Powers.Count == 0 && other.Creature.Powers.Count == 0);
            }
            finally
            {
                mode.SetValue(null, previousMode);
            }
        });

        test("Watcher enchantments: play hook changes only card owner's stance, never outside combat", () =>
        {
            WithHeadlessCommands(() =>
            {
                var owner = Player();
                var other = Player();
                var calmCard = Card<StrikeIronclad>(owner);
                calmCard.EnchantInternal(ModelDb.Enchantment<Calm>().ToMutable(), 1);
                calmCard.Enchantment!.OnPlay(Context, Play(calmCard)).GetAwaiter().GetResult();
                Check(owner.Creature.GetPower<CalmStancePower>() != null);
                Check(other.Creature.Powers.Count == 0);
                var wrathCard = Card<StrikeIronclad>(owner);
                wrathCard.EnchantInternal(ModelDb.Enchantment<Wrath>().ToMutable(), 1);
                wrathCard.Enchantment!.OnPlay(Context, Play(wrathCard)).GetAwaiter().GetResult();
                Check(owner.Creature.GetPower<WrathStancePower>() != null && owner.PlayerCombatState!.Energy == 1);
                SetBacking(owner, "PlayerCombatState", null!);
                calmCard.Enchantment.OnPlay(Context, Play(calmCard)).GetAwaiter().GetResult();
                Check(owner.Creature.GetPower<CalmStancePower>() == null);
            });
        });

        test("Deva Form: grants exactly one energy on later turns, once per turn", () =>
        {
            WithHeadlessCommands(() =>
            {
                var owner = Player();
                var relic = Owned<DevaFormRelic>(owner);
                relic.ModifyShuffleOrder(owner, [], false);
                relic.AfterEnergyReset(owner).GetAwaiter().GetResult();
                Check(owner.PlayerCombatState!.Energy == 0);
                for (var turn = 2; turn <= 4; turn++)
                {
                    owner.PlayerCombatState.IncrementTurnNumber();
                    relic.AfterEnergyReset(owner).GetAwaiter().GetResult();
                    relic.AfterEnergyReset(owner).GetAwaiter().GetResult();
                    Check(owner.PlayerCombatState.Energy == turn - 1);
                }
            });
        });
    }

    private static CardPlay Play(CardModel card, int index = 0, int count = 1) =>
        new()
        {
            Card = card, Player = card.Owner, PlayIndex = index, PlayCount = count,
            Target = null, ResultPile = PileType.Discard, Resources = default, IsAutoPlay = false
        };

    private static T Owned<T>(Player owner) where T : RelicModel
    {
        var relic = (T)ModelDb.Relic<T>().ToMutable();
        relic.Owner = owner;
        return relic;
    }

    private static CardModel Card<T>(Player owner, CardPile? pile = null) where T : CardModel
    {
        var card = ModelDb.Card<T>().ToMutable();
        card.Owner = owner;
        pile?.AddInternal(card, silent: true);
        return card;
    }

    private static Player Player()
    {
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        SetField(player, "_relics", new List<RelicModel>());
        SetField(player, "_potionSlots", new List<PotionModel>());
        SetBacking(player, "Creature", new Creature(player, 100, 100));
        SetBacking(player, "Deck", new CardPile(PileType.Deck));
        SetBacking(player, "PlayerRng", new PlayerRngSet(987UL));
        SetBacking(player, "IsActiveForHooks", true);
        var combat = new PlayerCombatState(player);
        SetBacking(player, "PlayerCombatState", combat);
        SetBacking(combat, "TurnNumber", 1);
        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        SetField(run, "_mapPointHistory", new List<List<MapPointHistoryEntry>>());
        SetField(run, "_players", new List<Player> { player });
        SetField(player, "_runState", run);
        var state = new CombatState(runState: run, modifiers: [], badgeModels: []);
        SetField(state, "_allies", new List<Creature> { player.Creature });
        player.Creature.CombatState = state;
        return player;
    }

    private static void WithSerialization(Action action)
    {
        var initialized = typeof(ModelIdSerializationCache).GetField("_initialized", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = initialized.GetValue(null);
        initialized.SetValue(null, true);
        try { action(); }
        finally { initialized.SetValue(null, previous); }
    }

    private static void WithHeadlessCommands(Action action)
    {
        // Keep native models/mutations; replace command presentation and global game-manager dependencies.
        var harmony = new Harmony("TheArchitect.Tests.WatcherCommands");
        var apply = typeof(PowerCmd).GetMethods().Single(m => m.Name == nameof(PowerCmd.Apply) && !m.IsGenericMethod);
        var remove = typeof(PowerCmd).GetMethod(nameof(PowerCmd.Remove), [typeof(PowerModel)])!;
        var energy = typeof(PlayerCmd).GetMethod(nameof(PlayerCmd.GainEnergy))!;
        harmony.Patch(apply, prefix: new HarmonyMethod(typeof(WatcherRelicTests), nameof(ApplyPower)));
        harmony.Patch(remove, prefix: new HarmonyMethod(typeof(WatcherRelicTests), nameof(RemovePower)));
        harmony.Patch(energy, prefix: new HarmonyMethod(typeof(WatcherRelicTests), nameof(GainEnergy)));
        try { action(); }
        finally
        {
            foreach (var method in new[] { apply, remove, energy })
                harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id);
        }
    }

    private static bool ApplyPower(PowerModel power, Creature target, decimal amount, ref Task __result)
    {
        power.ApplyInternal(target, amount);
        __result = Task.CompletedTask;
        return false;
    }

    private static bool RemovePower(PowerModel power, ref Task __result)
    {
        power.RemoveInternal();
        __result = Task.CompletedTask;
        return false;
    }

    private static bool GainEnergy(decimal amount, Player player, ref Task __result)
    {
        player.PlayerCombatState!.GainEnergy(amount);
        __result = Task.CompletedTask;
        return false;
    }

    private static bool SelectScryCards(PlayerChoiceContext context, IReadOnlyList<CardModel> cardsIn,
        Player player, CardSelectorPrefs prefs, ref Task<IEnumerable<CardModel>> __result)
    {
        __result = Task.FromResult(_scryChoice!(context, cardsIn, player, prefs));
        return false;
    }

    private static bool MoveScryCards(IEnumerable<CardModel> cards, CardPile newPile,
        ref Task<IReadOnlyList<CardPileAddResult>> __result)
    {
        foreach (var card in cards.ToArray())
        {
            card.Pile!.RemoveInternal(card, silent: true);
            newPile.AddInternal(card, silent: true);
        }
        __result = Task.FromResult<IReadOnlyList<CardPileAddResult>>([]);
        return false;
    }
    private static object? Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static void SetBacking(object target, string name, object value) =>
        SetField(target, $"<{name}>k__BackingField", value);
    private static void Check(bool condition, string message = "Assertion failed")
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
