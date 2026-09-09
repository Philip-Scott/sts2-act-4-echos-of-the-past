using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

namespace TheArchitect.TheArchitectCode.UI;

internal static class CorruptedPlayerPets
{
    internal static bool IsPrivatePet(Creature creature) =>
        creature.PetOwner is { } owner && NativeCorruptedPlayer.TryGet(owner, out _);

    // These two UI routines assume every pet belongs in the player container.
    // Keep actual ownership intact for damage, native summon/revive and all hooks.
    internal static Player? PlayerLayoutOwner(Creature creature) =>
        IsPrivatePet(creature) ? null : creature.PetOwner;

    internal static IEnumerable<CodeInstruction> PlayerLayout(IEnumerable<CodeInstruction> instructions) =>
        instructions.MethodReplacer(AccessTools.PropertyGetter(typeof(Creature), nameof(Creature.PetOwner)),
            AccessTools.Method(typeof(CorruptedPlayerPets), nameof(PlayerLayoutOwner)));

    internal static Vector2 Position(NCreature pet)
    {
        var owner = pet.Entity.PetOwner!.Creature.GetCreatureNode()
            ?? throw new InvalidOperationException("The Corrupted Player pet has no owner node.");
        var offset = pet.Entity.Monster is Osty
            ? NCreature.GetOstyOffsetFromPlayer(pet.Entity)
            : new Vector2(owner.Hitbox.Size.X / 2 + pet.Hitbox.Size.X / 2 + 40, 10);
        return owner.Position + new Vector2(-offset.X, offset.Y);
    }
}

[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom.AddCreature))]
internal static class CorruptedPlayerPetRoomPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        CorruptedPlayerPets.PlayerLayout(instructions);

    private static void Postfix(NCombatRoom __instance, Creature creature)
    {
        if (!CorruptedPlayerPets.IsPrivatePet(creature))
            return;
        var node = __instance.GetCreatureNode(creature)
            ?? throw new InvalidOperationException("The native Corrupted Player pet node was not created.");
        node.Body.Scale = new Vector2(-Math.Abs(node.Body.Scale.X), node.Body.Scale.Y);
        node.Position = CorruptedPlayerPets.Position(node);
        __instance.SetCreatureIsInteractable(creature, creature.Monster!.IsHealthBarVisible);
    }
}

[HarmonyPatch(typeof(NCreature), nameof(NCreature.Create))]
internal static class CorruptedPlayerOstyCorruptionPatch
{
    private static void Postfix(Creature entity, NCreature? __result)
    {
        if (__result != null && entity.Monster is Osty && CorruptedPlayerPets.IsPrivatePet(entity))
            CorruptedPlayerCorruption.Attach(__result.Visuals);
    }
}

[HarmonyPatch(typeof(NCreature), nameof(NCreature._Ready))]
internal static class CorruptedPlayerPetHealthDisplayPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        CorruptedPlayerPets.PlayerLayout(instructions);
}

[HarmonyPatch(typeof(NCreature), nameof(NCreature.OstyScaleToSize))]
internal static class CorruptedPlayerOstyPositionPatch
{
    private static void Postfix(NCreature __instance, double duration)
    {
        if (__instance.Entity.PetOwner is { } owner && NativeCorruptedPlayer.TryGet(owner, out var actor) &&
            !actor.Cleaned && actor.Body.IsAlive)
            AccessTools.FieldRefAccess<NCreature, Tween>(__instance, "_scaleTween").Parallel()
                .TweenProperty(__instance, "position", CorruptedPlayerPets.Position(__instance), duration);
    }
}
