using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.RelicPools;

namespace TheArchitect.TheArchitectCode.Relics;

[Pool(typeof(SharedRelicPool))]
public sealed class LooseThread : UnwrittenRelic
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new CardsVar(1), new DynamicVar("Turns", 3)];

    public override decimal ModifyHandDraw(Player player, decimal count)
    {
        var turn = Owner.PlayerCombatState?.TurnNumber ?? 0;
        return player == Owner && turn >= 1 && turn <= DynamicVars["Turns"].IntValue
            ? count + DynamicVars.Cards.BaseValue
            : count;
    }

    public override Task AfterModifyingHandDraw()
    {
        Flash();
        return Task.CompletedTask;
    }
}
