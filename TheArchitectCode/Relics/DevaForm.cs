using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Rooms;

namespace TheArchitect.TheArchitectCode.Relics;

[Pool(typeof(SharedRelicPool))]
public sealed class DevaForm : UnwrittenRelic
{
    private int? _firstEnergyTurn;
    private int _lastEnergyTurn;

    protected override IEnumerable<DynamicVar> CanonicalVars => [new EnergyVar(1)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.ForEnergy(this)];

    public override Task BeforeCombatStart()
    {
        _firstEnergyTurn = null;
        _lastEnergyTurn = 0;
        return Task.CompletedTask;
    }

    public override Task AfterCombatEnd(CombatRoom room) => BeforeCombatStart();

    public override void ModifyShuffleOrder(Player player, List<CardModel> cards, bool isInitialShuffle)
    {
        if (player != Owner || isInitialShuffle || _firstEnergyTurn != null || Owner.PlayerCombatState == null)
            return;

        _firstEnergyTurn = Owner.PlayerCombatState.TurnNumber + 1;
        Flash();
    }

    internal bool GrantsEnergy(Player player)
    {
        var turn = Owner.PlayerCombatState?.TurnNumber ?? 0;
        return player == Owner && _firstEnergyTurn is int first && turn >= first && turn > _lastEnergyTurn;
    }

    public override async Task AfterEnergyReset(Player player)
    {
        if (!GrantsEnergy(player))
            return;

        _lastEnergyTurn = Owner.PlayerCombatState!.TurnNumber;
        Flash();
        await PlayerCmd.GainEnergy(DynamicVars.Energy.BaseValue, Owner);
    }
}
