using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
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
    : HookPlayerChoiceContext(player, player.NetId, MegaCrit.Sts2.Core.Entities.Multiplayer.GameActionType.Combat)
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

// Native selection routines look up their chooser's selector stack. A private actor is not in the
// run's player collection, so it gets its own selector instead of the human party's stacks.
[HarmonyPatch(typeof(CardSelectorStack), nameof(CardSelectorStack.GetSelector))]
internal static class NativeCardSelectionPatch
{
    private static bool Prefix(Player player, ref ICardSelector? __result)
    {
        if (!NativeCorruptedPlayer.TryGet(player, out _))
            return true;
        __result = NativeCardSelector.Instance;
        return false;
    }
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
