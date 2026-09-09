using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.TestSupport;

namespace TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

internal sealed class NativeChoiceContext(Player player)
    : HookPlayerChoiceContext(player, player.NetId, GameActionType.Combat)
{
    public override Task SignalPlayerChoiceBegun(Player chooser, PlayerChoiceOptions options) =>
        throw new NotSupportedException("An unbridged native Corrupted Player choice attempted to enter the human action queue.");

    public override Task SignalPlayerChoiceEnded() =>
        throw new NotSupportedException("An unbridged native Corrupted Player choice attempted to resume a human action.");
}

internal sealed class NativeCardSelector : ICardSelector
{
    internal static NativeCardSelector Instance { get; } = new();
    public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,
        IReadOnlyList<CardRewardAlternative> alternatives) =>
        throw new NotSupportedException("The Corrupted Player cannot receive party card rewards.");
    public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
    {
        var cards = options.ToArray();
        var count = Math.Min(cards.Length, Math.Min(maxSelect, Math.Max(1, minSelect)));
        MainFile.Logger.Info($"NATIVE choice: {string.Join(",", cards.Take(count).Select(c => c.Id.Entry))}");
        return Task.FromResult<IEnumerable<CardModel>>(cards.Take(count).ToArray());
    }
}

// Replace selector reads inside each native selection routine, not the process-wide
// selector stack. Native filters, ordering, pile hooks and human choices stay intact.
[HarmonyPatch]
internal static class NativeCardSelectionPatch
{
    private static IEnumerable<MethodBase> TargetMethods() =>
        typeof(CardSelectCmd).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(Player)) ||
                m.Name == nameof(CardSelectCmd.FromDeckForEnchantment))
            .Select(m => m.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType)
            .OfType<Type>()
            .Where(t => t.GetField("player", AccessTools.all) != null || t.GetField("cards", AccessTools.all) != null)
            .Select(t => AccessTools.Method(t, "MoveNext"));

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var getter = AccessTools.PropertyGetter(typeof(CardSelectCmd), nameof(CardSelectCmd.Selector));
        var owner = AccessTools.Field(__originalMethod.DeclaringType, "player");
        var cards = owner == null ? AccessTools.Field(__originalMethod.DeclaringType, "cards") : null;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(getter))
            {
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.MoveLabelsFrom(instruction);
                load.MoveBlocksFrom(instruction);
                yield return load;
                yield return new CodeInstruction(OpCodes.Ldfld, owner ?? cards);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NativeCardSelectionPatch),
                    owner != null ? nameof(Selector) : nameof(DeckSelector)));
            }
            else
                yield return instruction;
        }
    }

    private static ICardSelector? Selector(Player player) =>
        NativeCorruptedPlayer.TryGet(player, out _) ? NativeCardSelector.Instance : CardSelectCmd.Selector;

    private static ICardSelector? DeckSelector(IReadOnlyList<CardModel> cards) =>
        cards.Count > 0 ? Selector(cards[0].Owner) : CardSelectCmd.Selector;
}

[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromChooseABundleScreen))]
internal static class NativeBundleChoicePatch
{
    private static bool Prefix(Player player, IReadOnlyList<IReadOnlyList<CardModel>> bundles,
        ref Task<IEnumerable<CardModel>> __result)
    {
        if (!NativeCorruptedPlayer.TryGet(player, out _))
            return true;
        if (bundles.Count == 0)
            throw new InvalidOperationException("A native Corrupted Player bundle choice has no options.");
        MainFile.Logger.Info("NATIVE choice: first offered bundle");
        __result = Task.FromResult<IEnumerable<CardModel>>(bundles[0]);
        return false;
    }
}
