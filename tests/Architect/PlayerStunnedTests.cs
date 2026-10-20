using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using TheArchitect.TheArchitectCode.Powers;

internal static class PlayerStunnedTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Player stun: applies the registered prefixed power without a generic lookup or alias", () =>
        {
            var models = (Dictionary<ModelId, AbstractModel>)AccessTools.Field(typeof(ModelDb), "_contentById")
                .GetValue(null)!;
            var canonical = new PlayerStunnedPower();
            var originalId = canonical.Id;
            var registeredId = new ModelId("POWER", "THEARCHITECT-PLAYER_STUNNED_POWER");
            models.TryGetValue(originalId, out var previous);
            var idField = AccessTools.Field(typeof(AbstractModel), "<Id>k__BackingField");
            var apply = typeof(PowerCmd).GetMethods().Single(method =>
                method.Name == nameof(PowerCmd.Apply) && !method.IsGenericMethod);
            var harmony = new Harmony("TheArchitect.Tests.PlayerStunIdentity");
            try
            {
                models.Remove(originalId);
                idField.SetValue(canonical, registeredId);
                models.Add(registeredId, canonical);
                try
                {
                    _ = ModelDb.Power<PlayerStunnedPower>();
                    throw new InvalidOperationException("The generic lookup should request the missing unprefixed ID.");
                }
                catch (KeyNotFoundException)
                {
                    // Reproduce the warmed generic lookup that killed Whistle's native turn.
                }
                harmony.Patch(apply, prefix: new HarmonyMethod(typeof(PlayerStunnedTests), nameof(ApplyPower)));
                var owner = Player();
                PlayerStunnedPower.ApplyTo(owner.Creature).GetAwaiter().GetResult();
                var applied = owner.Creature.GetPower<PlayerStunnedPower>();
                check(applied is { Amount: 1, IsMutable: true } && applied.Id == registeredId,
                    "Whistle must apply the registered power even when the unprefixed ID does not exist.");
                check(!ReferenceEquals(applied, canonical) && canonical.IsCanonical,
                    "Applying stun must mutate a fresh combat copy, not the canonical model.");
                check(ModelDb.GetByIdOrNull<PowerModel>(originalId) == null,
                    "Do not introduce an unprefixed alias to hide the lookup error.");
            }
            finally
            {
                harmony.Unpatch(apply, HarmonyPatchType.All, harmony.Id);
                models.Remove(registeredId);
                if (previous != null)
                    models[originalId] = previous;
            }
        });

        test("Player stun: prevents opening and effect draws only for its owner", () =>
        {
            var owner = Player();
            var other = Player();
            var power = (PlayerStunnedPower)new PlayerStunnedPower().ToMutable();
            power.ApplyInternal(owner.Creature, 1);
            check(power.Type == PowerType.Debuff && power.StackType == PowerStackType.Single,
                "Stun is a single-turn debuff, not a stacking turn counter.");
            foreach (var fromHandDraw in new[] { true, false })
            {
                check(!power.ShouldDraw(owner, fromHandDraw), "Stun must also block the normal opening draw.");
                check(power.ShouldDraw(other, fromHandDraw), "Other party members must still draw.");
            }
            check(power.ShouldPlay(null!, default), "Stun must not disable retained or generated cards.");
            check(power.ShouldAllowHitting(null!), "Stun must not make its owner untargetable.");
        });

        test("Player stun: expires after its owner's turn, not the attacking enemy turn", () =>
        {
            var owner = Player();
            var other = Player();
            var power = (PlayerStunnedPower)new PlayerStunnedPower().ToMutable();
            power.ApplyInternal(owner.Creature, 1);
            var remove = AccessTools.Method(typeof(PowerCmd), nameof(PowerCmd.Remove), [typeof(PowerModel)]);
            var harmony = new Harmony("TheArchitect.Tests.PlayerStun");
            harmony.Patch(remove, prefix: new HarmonyMethod(typeof(PlayerStunnedTests), nameof(RemovePower)));
            try
            {
                power.AfterSideTurnEnd(null!, CombatSide.Enemy, [other.Creature]).GetAwaiter().GetResult();
                check(owner.Creature.HasPower<PlayerStunnedPower>(), "Enemy turns must not consume the stun.");
                power.AfterSideTurnEnd(null!, CombatSide.Player, [other.Creature]).GetAwaiter().GetResult();
                check(owner.Creature.HasPower<PlayerStunnedPower>(), "An absent owner has not taken its stunned turn.");
                power.AfterSideTurnEnd(null!, CombatSide.Player, [owner.Creature, other.Creature]).GetAwaiter().GetResult();
                check(!owner.Creature.HasPower<PlayerStunnedPower>(), "The next turn must allow normal drawing.");
            }
            finally
            {
                harmony.Unpatch(remove, HarmonyPatchType.All, harmony.Id);
            }
        });
    }

    private static Player Player()
    {
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        AccessTools.Field(typeof(Player), "<Creature>k__BackingField").SetValue(player, new Creature(player, 100, 100));
        return player;
    }

    private static bool RemovePower(PowerModel power, ref Task __result)
    {
        power.RemoveInternal();
        __result = Task.CompletedTask;
        return false;
    }

    private static bool ApplyPower(PowerModel power, Creature target, decimal amount, ref Task __result)
    {
        power.ApplyInternal(target, amount);
        __result = Task.CompletedTask;
        return false;
    }
}
