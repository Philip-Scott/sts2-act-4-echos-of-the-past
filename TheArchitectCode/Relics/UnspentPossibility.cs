using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.RelicPools;

namespace TheArchitect.TheArchitectCode.Relics;

[Pool(typeof(SharedRelicPool))]
public sealed class UnspentPossibility : UnwrittenRelic
{
    public override bool HasUponPickupEffect => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new GoldVar(150)];

    public override Task AfterObtained() => PlayerCmd.GainGold(DynamicVars.Gold.BaseValue, Owner);
}
