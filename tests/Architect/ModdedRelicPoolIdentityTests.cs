using System.Collections;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Exceptions;
using MegaCrit.Sts2.Core.Models.RelicPools;
using TheArchitect.TheArchitectCode.Lifecycle;
using TheArchitect.TheArchitectCode.Relics;

internal static class ModdedRelicPoolIdentityTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Modded relic pools: prefixed legacy and custom IDs survive a warmed native selector", () =>
        {
            var models = (Dictionary<ModelId, AbstractModel>)AccessTools.Field(typeof(ModelDb), "_contentById")
                .GetValue(null)!;
            var pools = (IDictionary)AccessTools.Field(typeof(ModHelper), "_moddedContentForPools").GetValue(null)!;
            var poolType = typeof(SharedRelicPool);
            var previousPool = pools[poolType];
            pools.Remove(poolType);
            var types = new[] { typeof(BorrowedTomorrow), typeof(GoldenEye) };
            var relics = types.Select(type => ModelDb.GetById<RelicModel>(ModelDb.GetId(type))).ToArray();
            var originalIds = relics.Select(relic => relic.Id).ToArray();
            var registeredIds = new[]
            {
                new ModelId("RELIC", "THEARCHITECT-BORROWED_TOMORROW"),
                new ModelId("RELIC", "OTHER_CUSTOM_RELIC_ID")
            };
            var idField = AccessTools.Field(typeof(AbstractModel), "<Id>k__BackingField");
            var cardPool = ModelDb.All.OfType<CardPoolModel>().First();
            var cardPoolType = cardPool.GetType();
            var previousCardPool = pools[cardPoolType];
            pools.Remove(cardPoolType);
            var card = ModelDb.All.OfType<CardModel>().First();
            var harmony = new Harmony("TheArchitect.Tests.ModdedRelicPoolIdentity");
            try
            {
                for (var index = 0; index < relics.Length; index++)
                {
                    models.Remove(originalIds[index]);
                    models.Add(registeredIds[index], relics[index]);
                    idField.SetValue(relics[index], registeredIds[index]);
                    ModHelper.AddModelToPool(poolType, types[index]);
                }
                ModHelper.AddModelToPool(cardPoolType, card.GetType());
                try
                {
                    _ = new SharedRelicPool().AllRelics.ToArray();
                    throw new InvalidOperationException("The unpatched selector should request a missing vanilla ID.");
                }
                catch (ModelNotFoundException)
                {
                    // Warm the same native delegate that bypasses BaseLib's ID-prefix patch in game.
                }

                harmony.CreateClassProcessor(typeof(ModdedRelicPoolIdentity)).Patch();
                var pool = new SharedRelicPool();
                for (var reload = 0; reload < 2; reload++)
                {
                    var actual = pool.AllRelics.ToArray();
                    check(actual.TakeLast(relics.Length).SequenceEqual(relics),
                        "Append registered additions in order after the native pool, including retired relics.");
                    check(actual.TakeLast(relics.Length).Select(relic => relic.Id).SequenceEqual(registeredIds),
                        "Use registered IDs, not a guessed mod prefix or newly registered aliases.");
                }
                check(originalIds.All(id => ModelDb.GetByIdOrNull<RelicModel>(id) == null),
                    "The fix must not introduce unprefixed aliases.");
                check(ModHelper.ConcatModelsFromMods<CardModel>(cardPool, []).SequenceEqual([card]),
                    "Modded card pool additions must keep the native generic selector.");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
                for (var index = 0; index < relics.Length; index++)
                {
                    models.Remove(registeredIds[index]);
                    idField.SetValue(relics[index], originalIds[index]);
                    models[originalIds[index]] = relics[index];
                }
                Restore(pools, poolType, previousPool);
                Restore(pools, cardPoolType, previousCardPool);
            }
        });
    }

    private static void Restore(IDictionary pools, Type poolType, object? previous)
    {
        pools.Remove(poolType);
        if (previous != null)
            pools[poolType] = previous;
    }
}
