using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.RelicPools;

namespace TheArchitect.TheArchitectCode.Relics;

[Pool(typeof(SharedRelicPool))]
public sealed class TheLastWish : UnwrittenRelic
{
    public override bool HasUponPickupEffect => true;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new GoldVar(99), new PowerVar<PlatingPower>(4), new PowerVar<StrengthPower>(1)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        [HoverTipFactory.FromPower<PlatingPower>(), HoverTipFactory.FromPower<StrengthPower>()];

    public override Task AfterObtained() => PlayerCmd.GainGold(DynamicVars.Gold.BaseValue, Owner);

    public override async Task BeforeCombatStart()
    {
        Flash();
        var context = new ThrowingPlayerChoiceContext();
        await PowerCmd.Apply<PlatingPower>(context, Owner.Creature,
            DynamicVars["PlatingPower"].BaseValue, Owner.Creature, null);
        await PowerCmd.Apply<StrengthPower>(context, Owner.Creature,
            DynamicVars.Strength.BaseValue, Owner.Creature, null);
    }
}
