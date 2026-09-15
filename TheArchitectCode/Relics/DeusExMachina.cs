using BaseLib.Extensions;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.RelicPools;

namespace TheArchitect.TheArchitectCode.Relics;

[Pool(typeof(SharedRelicPool))]
public sealed class DeusExMachina : UnwrittenRelic
{
    public DeusExMachina() =>
        this.AddCustomAncientSpawnCondition(ancient => ancient.Owner is { } player && CanReceive(player));

    internal static bool CanReceive(Player player) => RandomDeckEnchantments.HasEligibleCards<Sown>(player, 3);

    public override bool HasUponPickupEffect => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(3)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => HoverTipFactory.FromEnchantment<Sown>();

    public override Task AfterObtained()
    {
        var cards = RandomDeckEnchantments.Apply<Sown>(Owner, DynamicVars.Cards.IntValue);
        if (cards.Count > 0)
            CardCmd.Preview(cards);
        return Task.CompletedTask;
    }
}
