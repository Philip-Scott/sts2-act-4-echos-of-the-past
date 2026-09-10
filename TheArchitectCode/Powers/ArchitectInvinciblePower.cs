using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace TheArchitect.TheArchitectCode.Powers;

public sealed class ArchitectInvinciblePower : CustomPowerModel
{
    private int _hpLostThisTurn;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override bool ShouldScaleInMultiplayer => true;
    public override int DisplayAmount => Math.Max(0, Amount - _hpLostThisTurn);
    public override string CustomPackedIconPath => ModelDb.Power<IntangiblePower>().PackedIconPath;
    public override string CustomBigIconPath => ModelDb.Power<IntangiblePower>().ResolvedBigIconPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Remaining", 0)];
    public override LocString Description
    {
        get
        {
            var description = base.Description;
            description.Add("Remaining", DisplayAmount);
            return description;
        }
    }

    public override decimal ModifyHpLostAfterOstyLate(Creature target, decimal amount, ValueProp props,
        Creature? dealer, CardModel? cardSource) => target == Owner ? Math.Min(amount, DisplayAmount) : amount;

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        Owner.CurrentHpChanged += TrackHpLoss;
        Removed += DetachHpListener;
        RefreshRemaining();
        return Task.CompletedTask;
    }

    private void DetachHpListener()
    {
        Owner.CurrentHpChanged -= TrackHpLoss;
        Removed -= DetachHpListener;
        Owner.HpDisplay = HpDisplay.Normal;
    }

    public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side,
        IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (side == Owner.Side && participants.Contains(Owner))
        {
            _hpLostThisTurn = 0;
            RefreshRemaining();
        }
        return Task.CompletedTask;
    }

    private void TrackHpLoss(int previousHp, int currentHp)
    {
        // Damage hooks run after the whole target batch; nested damage must see the spent budget immediately.
        if (currentHp < previousHp)
        {
            _hpLostThisTurn += previousHp - currentHp;
            RefreshRemaining();
        }
    }

    private void RefreshRemaining()
    {
        DynamicVars["Remaining"].BaseValue = DisplayAmount;
        Owner.HpDisplay = DisplayAmount == 0 ? HpDisplay.InfiniteWithNumbers : HpDisplay.Normal;
        InvokeDisplayAmountChanged();
    }
}
