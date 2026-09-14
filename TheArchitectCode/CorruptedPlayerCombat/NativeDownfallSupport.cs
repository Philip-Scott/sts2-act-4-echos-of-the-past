using System.Reflection;
using System.Reflection.Emit;
using BaseLib.Abstracts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

internal static class NativeDownfallSupport
{
    private static Assembly? _assembly;
    private static NativeDownfallApi? _api;
    private static string? _unsupported;
    private static MethodInfo? _spellbookAccess;
    private static readonly MethodInfo CreateCreature = AccessTools.Method(typeof(ICombatState),
        nameof(ICombatState.CreateCreature), [typeof(MonsterModel), typeof(CombatSide), typeof(string)]);
    private static readonly MethodInfo MonsterCombatGetter =
        AccessTools.PropertyGetter(typeof(MonsterModel), nameof(MonsterModel.CombatState));

    internal static void Install(Assembly assembly, Harmony harmony)
    {
        if (assembly.GetName().Name != "Downfall" || _assembly != null)
            return;
        _assembly = assembly;
        if (!NativeDownfallApi.TryBind(assembly, out var api, out _unsupported))
        {
            MainFile.Logger.Warn(_unsupported + " Corrupted Downfall effects will be marked unsupported.");
            return;
        }
        if (MonsterCombatGetter.ReturnType != typeof(ICombatState) ||
            PatchProcessor.GetOriginalInstructions(api!.SlimeCreationMoveNext)
            .Count(instruction => instruction.Calls(CreateCreature)) != 1)
        {
            _unsupported = "Downfall slime creation boundary changed.";
            MainFile.Logger.Warn(_unsupported + " Corrupted Downfall effects will be marked unsupported.");
            return;
        }
        _api = api;
        _spellbookAccess = AccessTools.Method(typeof(NativeDownfallSupport), nameof(Spellbook))
            .MakeGenericMethod(api.Spellbook);
        harmony.Patch(api.GhostExecuteWithContext,
            prefix: new HarmonyMethod(typeof(NativeDownfallSupport), nameof(GhostContext)));
        harmony.Patch(api.SlimeCreationMoveNext,
            transpiler: new HarmonyMethod(typeof(NativeDownfallSupport), nameof(SlimeCreation)));
        // These static helpers are outside the model-effect call-site pass. Use BaseLib's
        // direct custom-pile API rather than an inlinable vanilla CardPile.Get switch.
        foreach (var type in assembly.GetTypes().Where(type => InType(type, api.SpellbookGetter.DeclaringType!)))
            foreach (var method in AccessTools.GetDeclaredMethods(type).Where(method =>
                !method.ContainsGenericParameters && method.GetMethodBody() != null &&
                PatchProcessor.GetOriginalInstructions(method).Any(instruction => instruction.Calls(api.SpellbookGetter))))
                harmony.Patch(method,
                    transpiler: new HarmonyMethod(typeof(NativeDownfallSupport), nameof(SpellbookAccess)));
        MainFile.Logger.Info("Optional Downfall 0.1.16 actor compatibility enabled.");
    }

    internal static string? UnsupportedReason(Player player, CardModel card) =>
        _unsupported != null && (player.Character.GetType().Assembly == _assembly ||
            card.GetType().Assembly == _assembly || card.Enchantment?.GetType().Assembly == _assembly ||
            card.Affliction?.GetType().Assembly == _assembly ||
            CardModifier.Modifiers(card).Any(modifier => modifier.GetType().Assembly == _assembly))
            ? "Unsupported: " + _unsupported
            : null;

    internal static bool Initialize(NativeCorruptedPlayer actor)
    {
        if (_api is not { } api)
            return false;
        var player = actor.Player;
        api.ResetWheel(player);
        if (api.Hexaghost.IsInstanceOfType(player.Character))
            api.ActivateWheel(player);
        if (api.SlimeBoss.IsInstanceOfType(player.Character))
            api.SetSlimeSlots(player, 3);
        api.RefreshSpellbook(player);
        if (api.GetWheel(player).Length != 6 || api.GetSpellbook(player).Cards.Count == 0)
            throw new InvalidOperationException("Downfall failed to initialize the Corrupted Player's private combat state.");
        MainFile.Logger.Info($"Downfall actor state initialized: {player.Character.Id}; wheel=6, " +
            $"spellbook={api.GetSpellbook(player).Cards.Count}, slimeSlots={api.GetSlimeSlots(player)}.");
        return true;
    }

    internal static IEnumerable<AbstractModel> HookListeners(NativeCorruptedPlayer actor)
    {
        if (!actor.DownfallInitialized || actor.Cleaned || _api is not { } api)
            yield break;
        foreach (var flame in api.GetWheel(actor.Player))
            yield return flame;
        var stance = api.GetStance(actor.Player);
        if (stance.IsMutable && !api.NoStance.IsInstanceOfType(stance))
            yield return stance;
    }

    internal static Task AfterTurn(NativeCorruptedPlayer actor, PlayerChoiceContext context)
    {
        if (actor.DownfallInitialized && _api is { } api && api.IsIgnited(actor.Player))
            return api.Advance(context, actor.Player, null, true, true);
        return Task.CompletedTask;
    }

    internal static void RefreshVisuals(NativeCorruptedPlayer actor)
    {
        if (actor.DownfallInitialized)
            _api!.RefreshWheel(actor.Player);
    }

    internal static void Cleanup(NativeCorruptedPlayer actor)
    {
        if (actor.DownfallInitialized)
            _api!.HideWheel(actor.Player);
    }

    internal static MethodInfo? Replacement(MethodInfo called, Type? declaringType)
    {
        if (_api is not { } api)
            return null;
        if (called == api.GhostCombatGetter || called == api.StanceCombatGetter)
            return AccessTools.Method(typeof(NativeDownfallSupport), nameof(ModelCombat));
        if (called == api.SpellbookGetter)
            return _spellbookAccess;
        if (called == MonsterCombatGetter &&
            IsSlimeEffect(declaringType, api.Slime))
            return AccessTools.Method(typeof(NativeDownfallSupport), nameof(SlimeCombat));
        return null;
    }

    private static bool IsSlimeEffect(Type? type, Type slime) =>
        type != null && (slime.IsAssignableFrom(type) || IsSlimeEffect(type.DeclaringType, slime));

    private static bool InType(Type? type, Type outer) =>
        type != null && (type == outer || InType(type.DeclaringType, outer));

    private static T Spellbook<T>(Player player) where T : CardPile => (T)_api!.GetSpellbook(player);

    private static IEnumerable<CodeInstruction> SpellbookAccess(IEnumerable<CodeInstruction> instructions) =>
        instructions.MethodReplacer(_api!.SpellbookGetter, _spellbookAccess!);

    private static ICombatState? ModelCombat(AbstractModel model)
    {
        var owner = _api!.Owner(model);
        if (NativeCorruptedPlayer.TryGet(owner, out var actor))
            return actor.View;
        var combat = owner.Creature.CombatState;
        if (combat == null && _api.Stance.IsInstanceOfType(model))
            throw new InvalidOperationException("Combat state not initialized");
        return combat;
    }

    private static ICombatState SlimeCombat(MonsterModel model)
    {
        var owner = model.Creature.PetOwner;
        return owner != null && NativeCorruptedPlayer.TryGet(owner, out var actor) ? actor.View : model.CombatState;
    }

    private static bool GhostContext(AbstractModel __instance, Func<PlayerChoiceContext, Task> __0, ref Task __result)
    {
        if (!NativeCorruptedPlayer.TryGet(_api!.Owner(__instance), out var actor))
            return true;
        __result = actor.RunOwnedChoice(__0);
        return false;
    }

    private static IEnumerable<CodeInstruction> SlimeCreation(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(CreateCreature))
            {
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.MoveLabelsFrom(instruction);
                load.MoveBlocksFrom(instruction);
                yield return load;
                yield return new CodeInstruction(OpCodes.Ldfld, _api!.SlimeCreationPlayer);
                yield return new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(NativeDownfallSupport), nameof(CreateSlime)));
            }
            else
                yield return instruction;
        }
    }

    private static Creature CreateSlime(ICombatState combat, MonsterModel model, CombatSide side, string? slot, Player owner)
    {
        if (NativeCorruptedPlayer.TryGet(owner, out var actor))
        {
            if ((combat != actor.View.Live && combat != actor.View) || side != actor.Body.Side)
                throw new InvalidOperationException("Downfall slime creation escaped its owning combat side.");
            return actor.View.CreateCreature(model, side, slot);
        }
        return combat.CreateCreature(model, side, slot);
    }
}
