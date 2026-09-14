using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
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
    public IReadOnlyList<Creature> PlayerCreatures => Party.Select(member => member.Body).ToArray();
    public IReadOnlyList<Player> Players => Party.Select(member => member.Player).ToArray();
    private IEnumerable<NativeCorruptedPlayer> Party =>
        NativeCorruptedPlayer.In(live).Where(member => !member.Cleaned);
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
    public Player? GetPlayer(ulong playerId) => Players.FirstOrDefault(player => player.NetId == playerId);
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
    private static readonly HashSet<Assembly> PatchedModAssemblies = [];
    private static readonly MethodInfo IsPlayerGetter =
        AccessTools.PropertyGetter(typeof(Creature), nameof(Creature.IsPlayer));
    private static readonly MethodInfo MultiplayerConstraintGetter =
        AccessTools.PropertyGetter(typeof(IRunState), nameof(IRunState.CardMultiplayerConstraint));
    private static readonly MethodInfo LocalPlayerGetter = AccessTools.Method(
        typeof(MegaCrit.Sts2.Core.Context.LocalContext), nameof(MegaCrit.Sts2.Core.Context.LocalContext.GetMe),
        [typeof(IPlayerCollection)]);
    private static readonly Dictionary<MethodInfo, MethodInfo> Replacements = new()
    {
        [AccessTools.PropertyGetter(typeof(CombatState), nameof(CombatState.Players))] =
            AccessTools.Method(typeof(NativeCombatRoster), nameof(NativeCombatRoster.Players)),
        [AccessTools.PropertyGetter(typeof(ICombatState), nameof(ICombatState.Players))] =
            AccessTools.Method(typeof(NativeCombatRoster), nameof(NativeCombatRoster.Players)),
        [AccessTools.PropertyGetter(typeof(CombatState), nameof(CombatState.PlayerCreatures))] =
            AccessTools.Method(typeof(NativeCombatRoster), nameof(NativeCombatRoster.PlayerCreatures)),
        [AccessTools.PropertyGetter(typeof(ICombatState), nameof(ICombatState.PlayerCreatures))] =
            AccessTools.Method(typeof(NativeCombatRoster), nameof(NativeCombatRoster.PlayerCreatures)),
        [AccessTools.Method(typeof(AbstractModel), "MutableClone")] =
            AccessTools.Method(typeof(NativeCardCloning), nameof(NativeCardCloning.MutableClone)),
        [AccessTools.Method(typeof(CardModel), nameof(CardModel.ToMutable))] =
            AccessTools.Method(typeof(NativeCardCloning), nameof(NativeCardCloning.ToMutable)),
        [AccessTools.Method(typeof(CardModel), nameof(CardModel.ToSerializable))] =
            AccessTools.Method(typeof(NativeCardSerialization), nameof(NativeCardSerialization.ToSerializable)),
        [AccessTools.Method(typeof(AbstractModel), nameof(AbstractModel.ClonePreservingMutability))] =
            AccessTools.Method(typeof(NativeCardCloning), nameof(NativeCardCloning.ClonePreservingMutability)),
        [AccessTools.Method(typeof(CardPile), nameof(CardPile.Get), [typeof(PileType), typeof(Player)])] =
            AccessTools.Method(typeof(NativeCombatCallSites), nameof(Pile)),
        [AccessTools.Method(typeof(PileTypeExtensions), nameof(PileTypeExtensions.GetPile))] =
            AccessTools.Method(typeof(NativeCombatCallSites), nameof(Pile)),
        [KeywordMethod("GetLocKeyPrefix")] =
            AccessTools.Method(typeof(NativeCombatCallSites), nameof(KeywordPrefix)),
        [KeywordMethod("GetTitle")] =
            AccessTools.Method(typeof(NativeCombatCallSites), nameof(KeywordTitle)),
        [KeywordMethod("GetDescription")] =
            AccessTools.Method(typeof(NativeCombatCallSites), nameof(KeywordDescription)),
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

    private static MethodInfo? Replacement(MethodInfo called, Type? declaringType)
    {
        var cardEffect = IsCardEffect(declaringType);
        if (called == LocalPlayerGetter && declaringType?.DeclaringType == typeof(MegaCrit.Sts2.Core.Commands.PlayerCmd) &&
            declaringType.Name.StartsWith("<GainGold>", StringComparison.Ordinal))
            return AccessTools.Method(typeof(NativeCombatCallSites), nameof(GoldFeedbackPlayer));
        if (called == IsPlayerGetter &&
            (cardEffect || IsCoopAllyPower(declaringType) ||
             IsCardCommand(declaringType)))
            return AccessTools.Method(typeof(NativeCombatCallSites), nameof(IsCardPlayer));
        if (called == MultiplayerConstraintGetter)
            return AccessTools.Method(typeof(NativeCombatCallSites), nameof(CardMultiplayerConstraint));
        if (Replacements.TryGetValue(called, out var replacement))
            return replacement;
        if (NativeDownfallSupport.Replacement(called, declaringType) is { } downfall)
            return downfall;
        if (cardEffect && CorruptedPlayerAttackVfx.Replacement(called) is { } visual)
            return visual;
        return called.IsGenericMethod && called.GetGenericMethodDefinition() == AddPet
            ? AccessTools.Method(typeof(NativePetFactory), nameof(NativePetFactory.AddPet)).MakeGenericMethod(called.GetGenericArguments())
            : null;
    }

    private static Player? GoldFeedbackPlayer(IPlayerCollection? collection)
    {
        // Native gold rewards still run their hooks and mutate their private player;
        // only the local-player sound lookup has no meaning in an actor-only run.
        if (collection is IRunState run && run.Players.Count > 0 &&
            run.Players.All(player => NativeCorruptedPlayer.TryGet(player, out _)))
            return null;
        return MegaCrit.Sts2.Core.Context.LocalContext.GetMe(collection);
    }

    private static MethodInfo KeywordMethod(string name) => AccessTools.Method(
        typeof(CardKeyword).Assembly.GetType("MegaCrit.Sts2.Core.Entities.Cards.CardKeywordExtensions", true), name);

    // Native pile wrappers can inline CardPile.Get before BaseLib installs its custom-pile detour.
    private static CardPile? Pile(PileType type, Player player) =>
        player.PlayerCombatState is { } state &&
        BaseLib.Patches.Content.CustomPiles.GetCustomPile(state, type) is { } custom
            ? custom : CardPile.Get(type, player);

    // BaseLib's prefix detour can be inlined away in the game's keyword text callers.
    private static string KeywordPrefix(CardKeyword keyword) =>
        BaseLib.Patches.Content.CustomKeywords.KeywordIDs.TryGetValue((int)keyword, out var info)
            ? info.Key : MegaCrit.Sts2.Core.Helpers.StringHelper.Slugify(keyword.ToString());

    private static MegaCrit.Sts2.Core.Localization.LocString KeywordTitle(CardKeyword keyword) =>
        new("card_keywords", KeywordPrefix(keyword) + ".title");

    private static MegaCrit.Sts2.Core.Localization.LocString KeywordDescription(CardKeyword keyword) =>
        new("card_keywords", KeywordPrefix(keyword) + ".description");

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
            patched += PatchType(harmony, type);
        }
        MainFile.Logger.Info($"Native Corrupted Player installed {patched} owner-scoped combat call sites.");
    }

    // Wait until an encounter is created: other content mods can initialize after us.
    internal static void InstallModdedEffects()
    {
        var harmony = new Harmony(MainFile.ModId);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(assembly =>
            !assembly.IsDynamic && assembly != typeof(CardModel).Assembly &&
            !PatchedModAssemblies.Contains(assembly) &&
            assembly.GetReferencedAssemblies().Any(reference =>
                reference.Name == typeof(CardModel).Assembly.GetName().Name)))
        {
            NativeDownfallSupport.Install(assembly, harmony);
            var patched = 0;
            foreach (var type in assembly.GetTypes().Where(IsModelEffect))
                patched += PatchType(harmony, type);
            PatchedModAssemblies.Add(assembly);
            if (patched > 0)
                MainFile.Logger.Info($"Corrupted Player installed {patched} owner-scoped call sites in {assembly.GetName().Name}.");
        }
    }

    private static bool IsModelEffect(Type? type) =>
        type != null && (typeof(AbstractModel).IsAssignableFrom(type) || IsModelEffect(type.DeclaringType));

    private static bool IsCardEffect(Type? type) =>
        type != null && (typeof(CardModel).IsAssignableFrom(type) || IsCardEffect(type.DeclaringType));

    private static int PatchType(Harmony harmony, Type type)
    {
        var patched = 0;
        foreach (var method in AccessTools.GetDeclaredMethods(type).Where(m =>
            !m.ContainsGenericParameters && m.GetMethodBody() != null))
        {
            if (!PatchProcessor.GetOriginalInstructions(method).Any(instruction =>
                instruction.operand is MethodInfo called &&
                Replacement(called, type) != null))
                continue;
            harmony.Patch(method, transpiler: new HarmonyMethod(typeof(NativeCombatCallSites), nameof(Transpiler)));
            patched++;
        }
        return patched;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo called &&
                Replacement(called, __originalMethod.DeclaringType)
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
    internal static bool IsCardPlayer(Creature creature) =>
        NativeCorruptedPlayer.TryGet(creature, out _) || creature.IsPlayer;

    private static bool IsCardCommand(Type? type) =>
        type != null && (type == typeof(MegaCrit.Sts2.Core.Commands.CardCmd) || IsCardCommand(type.DeclaringType));

    private static bool IsCoopAllyPower(Type? type) =>
        type != null && (type == typeof(MegaCrit.Sts2.Core.Models.Powers.BeaconOfHopePower) ||
            type == typeof(MegaCrit.Sts2.Core.Models.Powers.TankPower) || IsCoopAllyPower(type.DeclaringType));

    private static MegaCrit.Sts2.Core.Entities.Cards.CardMultiplayerConstraint CardMultiplayerConstraint(IRunState run) =>
        run.Players.Count == 1 && NativeCorruptedPlayer.TryGet(run.Players[0], out var actor)
            ? actor.View.Live.Players.Count > 1
                ? MegaCrit.Sts2.Core.Entities.Cards.CardMultiplayerConstraint.MultiplayerOnly
                : MegaCrit.Sts2.Core.Entities.Cards.CardMultiplayerConstraint.SingleplayerOnly
            : run.CardMultiplayerConstraint;
}
