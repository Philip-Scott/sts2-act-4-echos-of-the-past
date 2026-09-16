using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.RelicPools;

namespace TheArchitect.TheArchitectCode.Relics;

[Pool(typeof(SharedRelicPool))]
public sealed class RitualDagger : UnwrittenRelic
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new PowerVar<RitualPower>(1), new PowerVar<VulnerablePower>(3)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        [HoverTipFactory.FromPower<RitualPower>(), HoverTipFactory.FromPower<VulnerablePower>()];

    public override async Task BeforeCombatStart()
    {
        Flash();
        var context = new ThrowingPlayerChoiceContext();
        await PowerCmd.Apply<RitualPower>(context, Owner.Creature,
            DynamicVars["RitualPower"].BaseValue, Owner.Creature, null);
        await PowerCmd.Apply<VulnerablePower>(context, Owner.Creature,
            DynamicVars.Vulnerable.BaseValue, Owner.Creature, null);
    }
}
