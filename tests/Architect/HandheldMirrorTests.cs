using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
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
                ModelDb.GetByIdOrNull<AbstractModel>(ModelDb.GetId(type)) is null).ToArray();
            if (missing.Length > 0) ModelDb.Init(missing);
        }

        test("mirror: unblocked native and modded relic types remain eligible by default", () =>
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

        Type[] blockedTypes = [typeof(TouchOfOrobas), typeof(PaelsEye), typeof(GoldenCompass),
            typeof(FurCoat), typeof(LordsParasol), typeof(ArchaicTooth), typeof(PaperKrane), typeof(PaperPhrog),
            typeof(LavaRock), typeof(WingedBoots), typeof(Byrdpip)];
        foreach (var blockedType in blockedTypes)
        {
            test($"mirror: {blockedType.Name} copies cannot fill the third eligible type", () =>
            {
                EnsureModels(blockedType, typeof(Anchor), typeof(PenNib));
                var blocked = ModelDb.GetById<RelicModel>(ModelDb.GetId(blockedType));
                var owned = new List<RelicModel>
                {
                    blocked.ToMutable(), blocked.ToMutable(),
                    ModelDb.Relic<Anchor>().ToMutable(), ModelDb.Relic<PenNib>().ToMutable()
                };
                var owner = MakePlayer(owned);
                var before = owner.PlayerRng.Rewards.ToSerializable().counter;
                var population = MirrorDuplication.CapturePopulation(owner);
                Check(population.Length == 2 && !population.Contains(blocked.Id));
                Check(!HandheldMirror.CanOffer(owner));
                Check(owner.PlayerRng.Rewards.ToSerializable().counter == before);
                Check(owner.Relics.SequenceEqual(owned) && owned.All(relic => !relic.IsMelted));
            });
        }

        test("mirror: blocklisted relics are excluded from draws without suppressing eligible offers", () =>
        {
            Type[] allowedTypes = [typeof(Anchor), typeof(PenNib), typeof(HappyFlower)];
            EnsureModels([.. blockedTypes, .. allowedTypes]);
            var allowedIds = allowedTypes.Select(ModelDb.GetId).ToArray();
            var owned = blockedTypes.Concat(allowedTypes)
                .Select(type => ModelDb.GetById<RelicModel>(ModelDb.GetId(type)).ToMutable()).ToList();
            var owner = MakePlayer(owned);
            var population = MirrorDuplication.CapturePopulation(owner);
            Check(population.Length == 3 && population.All(allowedIds.Contains));
            Check(HandheldMirror.CanOffer(owner));
            var selected = MirrorDuplication.PrepareCopies(owned, owner.PlayerRng.Rewards.NextInt)
                .Select(relic => relic.Id).ToArray();
            Check(selected.Length == 3 && selected.Distinct().Count() == 3 && selected.All(allowedIds.Contains));
            Check(owner.Relics.SequenceEqual(owned) && owned.All(relic => !relic.IsMelted));
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
                var fresh = MirrorDuplication.CreateCopy(original);
                Check(!ReferenceEquals(fresh, original) && fresh.Id == original.Id && fresh.IsMutable);
                Check(fresh.DisplayAmount == 0);
            }
            Check(pen.AttacksPlayed == 7 && flower.TurnsSeen == 2 && nunchaku.AttacksPlayed == 6);
        });

        test("mirror: prepared rewards and training are frozen before any acquisition", () => WithLocalization(() =>
        {
            EnsureModels(typeof(DustyTome), typeof(SeaGlass), typeof(Girya),
                typeof(Corruption), typeof(Apotheosis), typeof(Silent), typeof(Defect));
            var tome = (DustyTome)ModelDb.Relic<DustyTome>().ToMutable();
            tome.AncientCard = ModelDb.GetId<Corruption>();
            var glass = (SeaGlass)ModelDb.Relic<SeaGlass>().ToMutable();
            glass.CharacterId = ModelDb.GetId<Silent>();
            var girya = (Girya)ModelDb.Relic<Girya>().ToMutable();
            girya.TimesLifted = Girya.maxLifts;
            var owned = new List<RelicModel> { tome, glass, girya };
            var owner = MakePlayer(owned);
            Check(HandheldMirror.CanOffer(owner));
            var copies = MirrorDuplication.PrepareCopies(owned, owner.PlayerRng.Rewards.NextInt);
            Check(copies.Length == 3 && copies.Select(relic => relic.Id).Distinct().Count() == 3);
            Check(owner.PlayerRng.Rewards.ToSerializable().counter == 3);
            Check(copies.All(copy => copy.IsMutable && !owned.Contains(copy)));

            // Earlier pickup effects must not change later copies' already-prepared choices.
            tome.AncientCard = ModelDb.GetId<Apotheosis>();
            glass.CharacterId = ModelDb.GetId<Defect>();
            girya.TimesLifted = 0;
            owned.Clear();
            Check(copies.OfType<DustyTome>().Single().AncientCard == ModelDb.GetId<Corruption>());
            Check(copies.OfType<SeaGlass>().Single().CharacterId == ModelDb.GetId<Silent>());
            Check(copies.OfType<Girya>().Single().TimesLifted == Girya.maxLifts);
            Check(ModelDb.Relic<DustyTome>().AncientCard is null &&
                ModelDb.Relic<SeaGlass>().CharacterId is null && ModelDb.Relic<Girya>().TimesLifted == 0);

            WithSavedPropertyCache(copies, () =>
            {
                foreach (var copy in copies)
                {
                    var restored = RelicModel.FromSerializable(copy.ToSerializable());
                    Check(restored.Id == copy.Id);
                    switch (restored)
                    {
                        case DustyTome savedTome:
                            Check(savedTome.AncientCard == ModelDb.GetId<Corruption>());
                            break;
                        case SeaGlass savedGlass:
                            Check(savedGlass.CharacterId == ModelDb.GetId<Silent>());
                            break;
                        case Girya savedGirya:
                            Check(savedGirya.TimesLifted == Girya.maxLifts);
                            break;
                    }
                }
            });
        }));

        test("mirror: Sea Glass preserves each character rather than falling back to Ironclad", () => WithLocalization(() =>
        {
            Type[] characters = [typeof(Ironclad), typeof(Silent), typeof(Defect), typeof(Necrobinder), typeof(Regent)];
            EnsureModels([typeof(SeaGlass), .. characters]);
            foreach (var character in characters)
            {
                var original = (SeaGlass)ModelDb.Relic<SeaGlass>().ToMutable();
                original.CharacterId = ModelDb.GetId(character);
                var copy = (SeaGlass)MirrorDuplication.CreateCopy(original);
                Check(copy.CharacterId == original.CharacterId);
                Check(copy.Title.LocEntryKey == original.Title.LocEntryKey);
            }
        }));

        test("mirror: Girya retains training without reopening a fully trained Lift option", () =>
        {
            EnsureModels(typeof(Girya));
            for (var lifts = 0; lifts <= Girya.maxLifts; lifts++)
            {
                var original = (Girya)ModelDb.Relic<Girya>().ToMutable();
                original.TimesLifted = lifts;
                var copy = (Girya)MirrorDuplication.CreateCopy(original);
                Check(copy.TimesLifted == lifts && original.TimesLifted == lifts);
                Check(!ReferenceEquals(copy, original));
                if (lifts == Girya.maxLifts)
                {
                    var owner = MakePlayer([original, copy]);
                    original.Owner = owner;
                    copy.Owner = owner;
                    var options = new List<RestSiteOption>();
                    Check(!original.TryModifyRestSiteOptions(owner, options));
                    Check(!copy.TryModifyRestSiteOptions(owner, options));
                    Check(options.Count == 0);
                }
            }
        });

        test("mirror: source state comes from the first non-melted copy without weighting duplicates", () =>
        {
            EnsureModels(typeof(Girya), typeof(Anchor), typeof(PenNib));
            var melted = (Girya)ModelDb.Relic<Girya>().ToMutable();
            melted.IsMelted = true;
            var first = (Girya)ModelDb.Relic<Girya>().ToMutable();
            first.TimesLifted = Girya.maxLifts;
            var second = (Girya)ModelDb.Relic<Girya>().ToMutable();
            second.TimesLifted = 1;
            RelicModel[] owned = [melted, first, second,
                ModelDb.Relic<Anchor>().ToMutable(), ModelDb.Relic<PenNib>().ToMutable()];
            var sizes = new List<int>();
            var copies = MirrorDuplication.PrepareCopies(owned, count => { sizes.Add(count); return 0; });
            Check(sizes.SequenceEqual([3, 2, 1]));
            Check(copies.OfType<Girya>().Single().TimesLifted == Girya.maxLifts);
            Check(first.TimesLifted == Girya.maxLifts && second.TimesLifted == 1 && melted.IsMelted);
        });

        test("mirror: missing prepared reward state fails explicitly instead of inventing a reward", () =>
        {
            EnsureModels(typeof(DustyTome), typeof(SeaGlass));
            foreach (var original in new RelicModel[]
                     { ModelDb.Relic<DustyTome>().ToMutable(), ModelDb.Relic<SeaGlass>().ToMutable() })
            {
                var rejected = false;
                try { MirrorDuplication.CreateCopy(original); }
                catch (InvalidOperationException error) { rejected = error.Message.Contains("prepared"); }
                Check(rejected);
            }
        });
    }

    private static void WithSavedPropertyCache(IEnumerable<RelicModel> relics, Action action)
    {
        // Seed native save metadata without the network cache's Godot-dependent startup.
        var cache = typeof(ModelIdSerializationCache);
        var initialized = cache.GetField("_initialized", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = initialized.GetValue(null);
        var cacheProperties = cache.GetMethod("CachePropertiesForType", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var type in relics.Select(relic => relic.GetType()).Distinct())
            cacheProperties.Invoke(null, [type, null, null]);
        initialized.SetValue(null, true);
        try { action(); }
        finally { initialized.SetValue(null, previous); }
    }

    private static void WithLocalization(Action action)
    {
        // Native prepared-reward setters refresh localized text; no Godot or save initialization is needed.
        var previous = LocManager.Instance;
        var manager = (LocManager)RuntimeHelpers.GetUninitializedObject(typeof(LocManager));
        var tables = new Dictionary<string, LocTable>
        {
            ["characters"] = new("characters", new Dictionary<string, string>
            {
                ["IRONCLAD.title"] = "Ironclad", ["SILENT.title"] = "Silent", ["DEFECT.title"] = "Defect",
                ["NECROBINDER.title"] = "Necrobinder", ["REGENT.title"] = "Regent"
            }),
            ["cards"] = new("cards", new Dictionary<string, string>
            {
                ["CORRUPTION.title"] = "Corruption", ["APOTHEOSIS.title"] = "Apotheosis"
            }),
            ["card_keywords"] = new("card_keywords", new Dictionary<string, string>
            {
                ["EXHAUST.title"] = "Exhaust", ["EXHAUST.description"] = "Remove for this combat.",
                ["INNATE.title"] = "Innate", ["INNATE.description"] = "Start in the opening hand."
            })
        };
        typeof(LocManager).GetField("_tables", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, tables);
        typeof(LocManager).GetMethod("LoadLocFormatters", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(manager, null);
        var instance = typeof(LocManager).GetProperty(nameof(LocManager.Instance))!;
        instance.SetValue(null, manager);
        try { action(); }
        finally { instance.SetValue(null, previous); }
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
