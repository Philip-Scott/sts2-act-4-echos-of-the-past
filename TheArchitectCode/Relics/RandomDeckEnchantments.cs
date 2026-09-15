using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace TheArchitect.TheArchitectCode.Relics;

internal static class RandomDeckEnchantments
{
    private static readonly Logger Logger = new("TheArchitect", LogType.Generic);

    internal static bool HasEligibleCards(Player owner, EnchantmentModel enchantment, int count) =>
        EligibleCards(owner, enchantment).Take(count).Count() == count;

    internal static IReadOnlyList<CardModel> Apply(Player owner, EnchantmentModel enchantment, int count)
    {
        var selected = Select(owner, enchantment, count);
        if (selected.Count < count)
            Logger.Warn($"Only {selected.Count} of {count} cards remain eligible for {enchantment.Id}; " +
                "applying the available enchantments without replacing existing ones.");
        foreach (var card in selected)
            CardCmd.Enchant(enchantment.ToMutable(), card, 1);
        return selected;
    }

    internal static IReadOnlyList<CardModel> Select(Player owner, EnchantmentModel enchantment, int count)
    {
        var candidates = EligibleCards(owner, enchantment).ToList();
        var selected = new List<CardModel>();
        while (selected.Count < count && candidates.Count > 0)
        {
            var index = owner.PlayerRng.Rewards.NextInt(candidates.Count);
            selected.Add(candidates[index]);
            candidates.RemoveAt(index);
        }
        return selected;
    }

    private static IEnumerable<CardModel> EligibleCards(Player owner, EnchantmentModel enchantment) =>
        owner.Deck.Cards
            .Where(card => card.Owner == owner && card.Enchantment == null && enchantment.CanEnchant(card))
            .Distinct();
}
