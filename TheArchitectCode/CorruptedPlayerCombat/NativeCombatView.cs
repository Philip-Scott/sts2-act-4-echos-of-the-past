using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Singleton;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Runs;
using TheArchitect.TheArchitectCode.Lifecycle;
using TheArchitect.TheArchitectCode.UI;

namespace TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

// A card's perspective, not a second combat. Creature identity, hooks, mutations and
// absolute sides stay in the real combat; player-oriented collections are relative.
internal sealed class NativeCombatView(NativeCorruptedPlayer actor, ICombatState live) : ICombatState, ICardScope
{
    internal ICombatState Live => live;
    public IRunState RunState => actor.Player.RunState;
    public IReadOnlyList<Creature> Allies => live.GetTeammatesOf(actor.Body);
    public IReadOnlyList<Creature> Enemies => live.GetOpponentsOf(actor.Body);
    public IReadOnlyList<Creature> HittableEnemies => Enemies.Where(c => c.IsHittable).ToArray();
    public IReadOnlyList<Creature> Creatures => live.Creatures;
    public IReadOnlyList<Creature> PlayerCreatures => [actor.Body];
    public IReadOnlyList<Player> Players => [actor.Player];
    public IReadOnlyList<ModifierModel> Modifiers => live.Modifiers;
    public MultiplayerScalingModel? MultiplayerScalingModel => live.MultiplayerScalingModel;
    public int RoundNumber { get => live.RoundNumber; set => throw new NotSupportedException("Cards cannot advance the encounter round."); }
    public CombatSide CurrentSide { get => live.CurrentSide; set => throw new NotSupportedException("Cards cannot change the encounter side."); }
    public EncounterModel? Encounter => live.Encounter;
    public IReadOnlyList<Creature> EscapedCreatures => live.EscapedCreatures;
    public IReadOnlyList<Creature> CreaturesOnCurrentSide => live.CreaturesOnCurrentSide;
    public event Action<ICombatState>? CreaturesChanged
    {
        add => live.CreaturesChanged += value;
        remove => live.CreaturesChanged -= value;
    }
    public T CreateCard<T>(Player owner) where T : CardModel => live.CreateCard<T>(owner);
    public CardModel CreateCard(CardModel canonicalCard, Player owner) => live.CreateCard(canonicalCard, owner);
    public CardModel CloneCard(CardModel mutableCard) => live.CloneCard(mutableCard);
    public void AddCard(CardModel card, Player owner) => live.AddCard(card, owner);
    public void RemoveCard(CardModel card) => live.RemoveCard(card);
    public bool ContainsCard(CardModel card) => live.ContainsCard(card);
    public void AddPlayer(Player player) => throw new NotSupportedException("The Corrupted Player is not a party participant.");
    public Creature CreateCreature(MonsterModel monster, CombatSide side, string? slot)
    {
        NativePetFactory.Register(monster, actor.Player);
        return live.CreateCreature(monster, side, slot);
    }
    public void CreatureEscaped(Creature creature) => live.CreatureEscaped(creature);
    public void RemoveCreature(Creature creature, bool unattach = true) => live.RemoveCreature(creature, unattach);
    public bool ContainsCreature(Creature creature) => live.ContainsCreature(creature);
    public bool ContainsMonster<T>() where T : MonsterModel => live.ContainsMonster<T>();
    public Creature? GetCreature(uint? combatId) => live.GetCreature(combatId);
    public Task<Creature?> GetCreatureAsync(uint? combatId, double timeoutSec) => live.GetCreatureAsync(combatId, timeoutSec);
    public IReadOnlyList<Creature> GetCreaturesOnSide(CombatSide side) => live.GetCreaturesOnSide(side);
    public IReadOnlyList<Creature> GetOpponentsOf(Creature creature) => live.GetOpponentsOf(creature);
    public IReadOnlyList<Creature> GetTeammatesOf(Creature creature) => live.GetTeammatesOf(creature);
    public Player? GetPlayer(ulong playerId) => playerId == actor.Player.NetId ? actor.Player : live.GetPlayer(playerId);
    public IEnumerable<AbstractModel> IterateHookListeners() => live.IterateHookListeners();
    public void SortEnemiesBySlotName() => live.SortEnemiesBySlotName();
    public void SetEnemyIndex(Creature creature, int index) => live.SetEnemyIndex(creature, index);
    public void AddCreature(Creature creature) => live.AddCreature(creature);
    public bool IsLiveCombat() => live.IsLiveCombat();
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.CombatState), MethodType.Getter)]
internal static class NativeCardCombatViewPatch
{
    private static void Postfix(CardModel __instance, ref ICombatState? __result)
    {
        if (__result != null && __instance.IsMutable && __instance.Owner is { } player &&
            NativeCorruptedPlayer.TryGet(player, out var actor))
            __result = actor.View;
    }
}

[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.CombatState), MethodType.Getter)]
internal static class NativePowerCombatViewPatch
{
    private static void Postfix(PowerModel __instance, ref ICombatState __result)
    {
        if (__instance.Owner is { } owner && NativeCorruptedPlayer.TryGet(owner, out var actor))
            __result = actor.View;
    }
}

[HarmonyPatch(typeof(OrbModel), nameof(OrbModel.CombatState), MethodType.Getter)]
internal static class NativeOrbCombatViewPatch
{
    private static void Postfix(OrbModel __instance, ref ICombatState __result)
    {
        if (__instance.Owner is { } owner && NativeCorruptedPlayer.TryGet(owner, out var actor))
            __result = actor.View;
    }
}

[HarmonyPatch(typeof(CombatState), "AddCard", [typeof(CardModel)])]
internal static class NativeCardScopeIdentityPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        instructions.MethodReplacer(AccessTools.PropertyGetter(typeof(CardModel), nameof(CardModel.CombatState)),
            AccessTools.Method(typeof(NativeCardScopeIdentityPatch), nameof(ActualCombat)));

    private static ICombatState? ActualCombat(CardModel card) =>
        card.CombatState is NativeCombatView view ? view.Live : card.CombatState;
}

// BaseLib can precompile native effects before our getter detours are installed.
// Rewriting the callers also covers their already-inlined combat-state getters.
internal static class NativeCombatCallSites
{
    private static readonly Dictionary<MethodInfo, MethodInfo> Replacements = new()
    {
        [AccessTools.Method(typeof(MultiplayerScalingModel), nameof(MultiplayerScalingModel.GetMultiplayerScaling))] =
            AccessTools.Method(typeof(ArchitectMultiplayerScaling), nameof(ArchitectMultiplayerScaling.GetScaling)),
        [AccessTools.PropertyGetter(typeof(CardModel), nameof(CardModel.CombatState))] =
            AccessTools.Method(typeof(NativeCombatCallSites), nameof(CardScope)),
        [AccessTools.PropertyGetter(typeof(PowerModel), nameof(PowerModel.CombatState))] =
            AccessTools.Method(typeof(NativeCombatCallSites), nameof(PowerScope)),
        [AccessTools.PropertyGetter(typeof(OrbModel), nameof(OrbModel.CombatState))] =
            AccessTools.Method(typeof(NativeCombatCallSites), nameof(OrbScope)),
        [AccessTools.PropertyGetter(typeof(Creature), nameof(Creature.Player))] =
            AccessTools.Method(typeof(NativeCombatCallSites), nameof(CreatureOwner)),
        [AccessTools.PropertyGetter(typeof(Creature), nameof(Creature.IsPlayer))] =
            AccessTools.Method(typeof(NativeCombatCallSites), nameof(IsPartyPlayer)),
        [AccessTools.Method(typeof(Hook), nameof(Hook.AfterBlockCleared))] =
            AccessTools.Method(typeof(NativeTurnPhases), nameof(NativeTurnPhases.AfterBlockCleared)),
        [AccessTools.Method(typeof(Hook), nameof(Hook.AfterSideTurnStart))] =
            AccessTools.Method(typeof(NativeTurnPhases), nameof(NativeTurnPhases.AfterSideTurnStart)),
        [AccessTools.Method(typeof(Hook), nameof(Hook.BeforeSideTurnEnd))] =
            AccessTools.Method(typeof(NativeTurnPhases), nameof(NativeTurnPhases.BeforeSideTurnEnd)),
        [AccessTools.Method(typeof(Hook), nameof(Hook.AfterSideTurnEnd))] =
            AccessTools.Method(typeof(NativeTurnPhases), nameof(NativeTurnPhases.AfterSideTurnEnd))
    };
    private static readonly MethodInfo AddPet = typeof(MegaCrit.Sts2.Core.Commands.PlayerCmd).GetMethods()
        .Single(m => m.Name == "AddPet" && m.IsGenericMethodDefinition);

    private static MethodInfo? Replacement(MethodInfo called, bool cardEffect)
    {
        if (Replacements.TryGetValue(called, out var replacement))
            return replacement;
        if (cardEffect && CorruptedPlayerAttackVfx.Replacement(called) is { } visual)
            return visual;
        return called.IsGenericMethod && called.GetGenericMethodDefinition() == AddPet
            ? AccessTools.Method(typeof(NativePetFactory), nameof(NativePetFactory.AddPet)).MakeGenericMethod(called.GetGenericArguments())
            : null;
    }

    internal static void Install(Harmony harmony)
    {
        var patched = 0;
        foreach (var type in typeof(CardModel).Assembly.GetTypes().Where(t =>
            t.Namespace?.StartsWith("MegaCrit.Sts2.Core.Models", StringComparison.Ordinal) == true ||
            t.Namespace?.StartsWith("MegaCrit.Sts2.Core.Entities", StringComparison.Ordinal) == true ||
            t.Namespace?.StartsWith("MegaCrit.Sts2.Core.Localization.DynamicVars", StringComparison.Ordinal) == true ||
            t.Namespace == "MegaCrit.Sts2.Core.Combat" ||
            t.Namespace == "MegaCrit.Sts2.Core.Commands" ||
            t.Namespace == "MegaCrit.Sts2.Core.Nodes.Orbs"))
        {
            foreach (var method in AccessTools.GetDeclaredMethods(type).Where(m =>
                !m.ContainsGenericParameters && m.GetMethodBody() != null))
            {
                if (!PatchProcessor.GetOriginalInstructions(method).Any(instruction =>
                    instruction.operand is MethodInfo called &&
                    Replacement(called, type.Namespace == "MegaCrit.Sts2.Core.Models.Cards") != null))
                    continue;
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(NativeCombatCallSites), nameof(Transpiler)));
                patched++;
            }
        }
        MainFile.Logger.Info($"Native Corrupted Player installed {patched} owner-scoped combat call sites.");
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo called &&
                Replacement(called, __originalMethod.DeclaringType?.Namespace == "MegaCrit.Sts2.Core.Models.Cards")
                    is { } replacement)
            {
                instruction.opcode = System.Reflection.Emit.OpCodes.Call;
                instruction.operand = replacement;
            }
            yield return instruction;
        }
    }

    private static ICombatState? CardScope(CardModel card) =>
        card.IsMutable && card.Owner is { } owner && NativeCorruptedPlayer.TryGet(owner, out var actor) &&
        card.CombatState != null ? actor.View : card.CombatState;
    private static ICombatState PowerScope(PowerModel power) =>
        power.Owner is { } owner && NativeCorruptedPlayer.TryGet(owner, out var actor) ? actor.View : power.CombatState;
    private static ICombatState OrbScope(OrbModel orb) =>
        orb.Owner is { } owner && NativeCorruptedPlayer.TryGet(owner, out var actor) ? actor.View : orb.CombatState;
    internal static Player? CreatureOwner(Creature creature) =>
        NativeCorruptedPlayer.TryGet(creature, out var actor) ? actor.Player : creature.Player;
    internal static bool IsPartyPlayer(Creature creature) =>
        !NativeCorruptedPlayer.TryGet(creature, out _) && creature.IsPlayer;
}
