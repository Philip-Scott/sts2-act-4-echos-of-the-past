using System.Collections;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
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
                var pool = new SharedRelicPool();
                try
                {
                    _ = ModHelper.ConcatModelsFromMods<RelicModel>(pool, []).ToArray();
                    throw new InvalidOperationException("The unpatched selector should request a missing vanilla ID.");
                }
                catch (ModelNotFoundException)
                {
                    // Warm the same native delegate that bypasses BaseLib's ID-prefix patch in game.
                }

                harmony.CreateClassProcessor(typeof(ModdedRelicPoolIdentity)).Patch();
                for (var reload = 0; reload < 2; reload++)
                {
                    var actual = ModHelper.ConcatModelsFromMods(pool, relics.Take(1)).ToArray();
                    check(actual.SequenceEqual(new[] { relics[0], relics[0], relics[1] }),
                        "Preserve the original pool and registered additions in order, including retired relics.");
                    check(actual.Skip(1).Select(relic => relic.Id).SequenceEqual(registeredIds),
                        "Use registered IDs, not a guessed mod prefix or newly registered aliases.");
                }
                check(originalIds.All(id => ModelDb.GetByIdOrNull<RelicModel>(id) == null),
                    "The fix must not introduce unprefixed aliases.");
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
                pools.Remove(poolType);
                if (previousPool != null)
                    pools[poolType] = previousPool;
            }
        });

        test("Modded pools: the shared selector patch keeps card pool additions typed as cards", () =>
        {
            var pools = (IDictionary)AccessTools.Field(typeof(ModHelper), "_moddedContentForPools").GetValue(null)!;
            var poolType = typeof(IroncladCardPool);
            var previousPool = pools[poolType];
            pools.Remove(poolType);
            var harmony = new Harmony("TheArchitect.Tests.ModdedCardPoolIdentity");
            ModelDb.Inject(typeof(StrikeIronclad));
            try
            {
                ModHelper.AddModelToPool(poolType, typeof(StrikeIronclad));
                harmony.CreateClassProcessor(typeof(ModdedRelicPoolIdentity)).Patch();
                // Card and relic selectors share one native body; a relic-typed replacement fails this cast.
                CardModel[] actual = ModHelper.ConcatModelsFromMods<CardModel>(new IroncladCardPool(), []).ToArray();
                check(actual.Length == 1 && actual[0] == ModelDb.Card<StrikeIronclad>(),
                    "Modded card pool additions resolve to their canonical card models.");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
                pools.Remove(poolType);
                if (previousPool != null)
                    pools[poolType] = previousPool;
            }
        });
    }
}
