using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.RelicPools;

namespace TheArchitect.TheArchitectCode.Relics;

[Pool(typeof(SharedRelicPool))]
public sealed class BorrowedTomorrow : UnwrittenRelic
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new EnergyVar(1), new DynamicVar("Turns", 3), new PowerVar<VulnerablePower>(2)];

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        [HoverTipFactory.ForEnergy(this), HoverTipFactory.FromPower<VulnerablePower>()];

    public override async Task BeforeCombatStart()
    {
        Flash();
        await PowerCmd.Apply<VulnerablePower>(new ThrowingPlayerChoiceContext(), Owner.Creature,
            DynamicVars.Vulnerable.BaseValue, Owner.Creature, null);
    }

    public override async Task AfterEnergyReset(Player player)
    {
        var turn = Owner.PlayerCombatState?.TurnNumber ?? 0;
        if (player != Owner || turn < 1 || turn > DynamicVars["Turns"].IntValue)
            return;

        Flash();
        await PlayerCmd.GainEnergy(DynamicVars.Energy.BaseValue, Owner);
    }
}
