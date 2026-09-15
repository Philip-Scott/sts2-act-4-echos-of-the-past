using BaseLib.Extensions;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.RelicPools;
using TheArchitect.TheArchitectCode.Enchantments;

namespace TheArchitect.TheArchitectCode.Relics;

[Pool(typeof(SharedRelicPool))]
public sealed class VioletLotus : UnwrittenRelic
{
    public VioletLotus() =>
        this.AddCustomAncientSpawnCondition(ancient => ancient.Owner is { } player && CanReceive(player));

    internal static bool CanReceive(Player player) =>
        RandomDeckEnchantments.HasEligibleCards<Wrath>(player, 4) &&
        RandomDeckEnchantments.HasEligibleCards<Calm>(player, 4);

    public override bool HasUponPickupEffect => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(2)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        [.. HoverTipFactory.FromEnchantment<Wrath>(), .. HoverTipFactory.FromEnchantment<Calm>()];

    public override Task AfterObtained()
    {
        var wrath = RandomDeckEnchantments.Apply<Wrath>(Owner, DynamicVars.Cards.IntValue);
        var calm = RandomDeckEnchantments.Apply<Calm>(Owner, DynamicVars.Cards.IntValue);
        if (wrath.Count + calm.Count > 0)
            CardCmd.Preview([.. wrath, .. calm]);
        return Task.CompletedTask;
    }
}
