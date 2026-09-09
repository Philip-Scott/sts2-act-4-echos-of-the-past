using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using TheArchitect.TheArchitectCode.Relics;

internal static class HandheldMirrorTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        void Check(bool condition, string message = "Assertion failed") => check(condition, message);

        static void EnsureModels(params Type[] types)
        {
            var missing = types.Where(type =>
                ModelDb.GetByIdOrNull<RelicModel>(ModelDb.GetId(type)) is null).ToArray();
            if (missing.Length > 0) ModelDb.Init(missing);
        }

        test("mirror: native and modded relic types need no compatibility list", () =>
        {
            Type[] types = [typeof(CallingBell), typeof(EmptyCage), typeof(Orichalcum),
                typeof(DollysMirror), typeof(LooseThread)];
            EnsureModels(types);
            var owned = types.Select(type => ModelDb.GetById<RelicModel>(ModelDb.GetId(type)).ToMutable()).ToList();
            var population = MirrorDuplication.CapturePopulation(MakePlayer(owned));
            Check(population.Length == types.Length && owned.All(relic => population.Contains(relic.Id)));
            Check(HandheldMirror.CanOffer(MakePlayer(owned)));
        });

        test("mirror: excludes itself even when several copies are owned", () =>
        {
            EnsureModels(typeof(HandheldMirror), typeof(Anchor), typeof(PenNib));
            var owner = MakePlayer(
            [
                ModelDb.Relic<HandheldMirror>().ToMutable(), ModelDb.Relic<HandheldMirror>().ToMutable(),
                ModelDb.Relic<Anchor>().ToMutable(), ModelDb.Relic<PenNib>().ToMutable()
            ]);
            var population = MirrorDuplication.CapturePopulation(owner);
            Check(population.Length == 2 && !population.Contains(ModelDb.GetId<HandheldMirror>()));
            Check(!HandheldMirror.CanOffer(owner));
        });

        test("mirror: three distinct types are mandatory before consuming RNG", () =>
        {
            var first = new ModelId("relic", "FIRST");
            var second = new ModelId("relic", "SECOND");
            var draws = 0;
            foreach (var population in new ModelId[][] { [], [first], [first, second], [first, first, second] })
            {
                var rejected = false;
                try { MirrorDuplication.SelectThree(population, _ => { draws++; return 0; }); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected);
            }
            Check(draws == 0);
        });

        test("mirror: duplicates do not weight draws and the source list stays unchanged", () =>
        {
            var ids = Enumerable.Range(0, 4).Select(i => new ModelId("relic", $"TYPE_{i}")).ToArray();
            var population = new List<ModelId> { ids[0], ids[0], ids[1], ids[2], ids[3] };
            var original = population.ToArray();
            var sizes = new List<int>();
            var selected = MirrorDuplication.SelectThree(population, count =>
            {
                sizes.Add(count);
                return count - 1;
            });
            Check(sizes.SequenceEqual([4, 3, 2]));
            Check(selected.SequenceEqual([ids[3], ids[2], ids[1]]));
            Check(population.SequenceEqual(original));
            population.Add(new ModelId("relic", "REWARD_PRODUCED"));
            Check(selected.Length == 3 && selected.All(original.Contains));
        });

        test("mirror: each distinct ordered triple has exactly one uniform draw path", () =>
        {
            var ids = Enumerable.Range(0, 4).Select(i => new ModelId("relic", $"TYPE_{i}")).ToArray();
            var outcomes = new HashSet<string>();
            for (var first = 0; first < 4; first++)
            for (var second = 0; second < 3; second++)
            for (var third = 0; third < 2; third++)
            {
                var draws = new Queue<int>([first, second, third]);
                var selected = MirrorDuplication.SelectThree(ids, _ => draws.Dequeue());
                Check(selected.Distinct().Count() == 3);
                Check(outcomes.Add(string.Join(",", selected.Select(id => id.Entry))));
            }
            Check(outcomes.Count == 24);
        });

        test("mirror: native RNG repeats the same three draws and consumes exactly three", () =>
        {
            var ids = Enumerable.Range(0, 8).Select(i => new ModelId("relic", $"TYPE_{i}")).ToArray();
            var left = new Rng(125UL);
            var right = new Rng(125UL);
            Check(MirrorDuplication.SelectThree(ids, left.NextInt)
                .SequenceEqual(MirrorDuplication.SelectThree(ids, right.NextInt)));
            Check(left.ToSerializable().counter == 3 && right.ToSerializable().counter == 3);
        });

        test("mirror: population is stable and excludes melted copies without filtering the owner's deck", () =>
        {
            EnsureModels(typeof(Anchor), typeof(PenNib), typeof(HappyFlower), typeof(DollysMirror));
            var anchor = ModelDb.Relic<Anchor>().ToMutable();
            var duplicateAnchor = ModelDb.Relic<Anchor>().ToMutable();
            var penNib = ModelDb.Relic<PenNib>().ToMutable();
            var flower = ModelDb.Relic<HappyFlower>().ToMutable();
            var dolly = ModelDb.Relic<DollysMirror>().ToMutable();
            RelicModel[] owned = [flower, anchor, duplicateAnchor, penNib, dolly];
            var first = MirrorDuplication.CapturePopulation(owned);
            Check(first.Length == 4 && first.SequenceEqual(MirrorDuplication.CapturePopulation(owned.Reverse())));
            Check(MirrorDuplication.CapturePopulation(MakePlayer(owned.ToList())).Contains(dolly.Id));
            Check(first.SequenceEqual(first.OrderBy(id => id.Category, StringComparer.Ordinal)
                .ThenBy(id => id.Entry, StringComparer.Ordinal)));
            anchor.IsMelted = true;
            duplicateAnchor.IsMelted = true;
            Check(MirrorDuplication.CapturePopulation(owned).Length == 3);
        });

        test("mirror: owner-only eligibility is RNG-free at one HP with zero Gold", () =>
        {
            EnsureModels(typeof(Anchor), typeof(PenNib), typeof(HappyFlower));
            var owned = new List<RelicModel>
            {
                ModelDb.Relic<Anchor>().ToMutable(),
                ModelDb.Relic<PenNib>().ToMutable(),
                ModelDb.Relic<HappyFlower>().ToMutable()
            };
            var owner = MakePlayer(owned);
            var other = MakePlayer([]);
            var before = owner.PlayerRng.Rewards.ToSerializable().counter;
            Check(owner.Gold == 0 && owner.Creature.CurrentHp == 1 && owner.Creature.MaxHp == 1);
            Check(HandheldMirror.CanOffer(owner) && HandheldMirror.CanOffer(owner));
            Check(!HandheldMirror.CanOffer(null));
            Check(!HandheldMirror.CanOffer(other));
            Check(owner.PlayerRng.Rewards.ToSerializable().counter == before);
            Check(owner.Relics.SequenceEqual(owned) && owner.Gold == 0 && owner.Creature.CurrentHp == 1);
            owner.Creature.SetCurrentHpInternal(0);
            Check(!HandheldMirror.CanOffer(owner));
        });

        test("mirror: canonical acquisition starts fresh without changing original saved counters", () =>
        {
            EnsureModels(typeof(PenNib), typeof(HappyFlower), typeof(Nunchaku));
            var pen = (PenNib)ModelDb.Relic<PenNib>().ToMutable();
            var flower = (HappyFlower)ModelDb.Relic<HappyFlower>().ToMutable();
            var nunchaku = (Nunchaku)ModelDb.Relic<Nunchaku>().ToMutable();
            for (var i = 0; i < 7; i++) pen.NotifyAttackPlayed();
            flower.TurnsSeen = 2;
            nunchaku.AttacksPlayed = 6;
            foreach (var original in new RelicModel[] { pen, flower, nunchaku })
            {
                var fresh = ModelDb.GetById<RelicModel>(original.Id).ToMutable();
                Check(!ReferenceEquals(fresh, original) && fresh.Id == original.Id && fresh.IsMutable);
                Check(fresh.DisplayAmount == 0);
            }
            Check(pen.AttacksPlayed == 7 && flower.TurnsSeen == 2 && nunchaku.AttacksPlayed == 6);
        });
    }

    private static Player MakePlayer(List<RelicModel> relics)
    {
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        void Set(string field, object value) =>
            typeof(Player).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, value);
        Set("_relics", relics);
        Set("<Creature>k__BackingField", new Creature(player, 1, 1));
        Set("<Deck>k__BackingField", new CardPile(PileType.Deck));
        Set("<PlayerRng>k__BackingField", new PlayerRngSet(987UL));
        return player;
    }
}
