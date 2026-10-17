using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

namespace TheArchitect.TheArchitectCode.Powers;

// Relic-like Corrupted Player buffs. Effects resolve on the private native player
// bound to this power's owner, never on a human party member.
public abstract class CorruptionBuffPower : TheArchitectPower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    protected bool TryGetActor(out NativeCorruptedPlayer actor) =>
        NativeCorruptedPlayer.TryGet(Owner, out actor) && !actor.Cleaned && Owner.IsAlive;

    protected bool IsActor(Player player) => TryGetActor(out var actor) && actor.Player == player;

    protected bool IsOwnTurn(IEnumerable<Creature> participants) => Owner.IsAlive && participants.Contains(Owner);

    protected static Task Owned(NativeCorruptedPlayer actor, Func<PlayerChoiceContext, Task> action) =>
        actor.RunOwnedChoice(action);
}

public sealed class SingeingIchorPower : CorruptionBuffPower
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<StrengthPower>(1), new HealVar(7)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<StrengthPower>()];

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (!IsOwnTurn(participants))
            return;
        Flash();
        await PowerCmd.Apply<StrengthPower>(choiceContext, Owner, DynamicVars.Strength.BaseValue, Owner, null);
        await CreatureCmd.Heal(Owner, DynamicVars.Heal.BaseValue);
    }
}

public sealed class RingOfTheViperPower : CorruptionBuffPower
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(1), new PowerVar<ArtifactPower>(1)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<ArtifactPower>()];

    // The hand is prepared during the human turn so the telegraph shows the real upcoming hand.
    public override decimal ModifyHandDraw(Player player, decimal count) =>
        IsActor(player) ? count + DynamicVars.Cards.BaseValue : count;

    public override Task AfterModifyingHandDraw()
    {
        Flash();
        return Task.CompletedTask;
    }

    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (!IsOwnTurn(participants))
            return;
        Flash();
        await PowerCmd.Apply<ArtifactPower>(new ThrowingPlayerChoiceContext(), Owner,
            DynamicVars["ArtifactPower"].BaseValue, Owner, null);
    }
}

public sealed class TarnishedCrownPower : CorruptionBuffPower
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new StarsVar("StartOfCombat", 3), new StarsVar("EndOfTurn", 2)];

    public override async Task BeforeCombatStart()
    {
        if (!TryGetActor(out var actor))
            return;
        Flash();
        await PlayerCmd.GainStars(DynamicVars["StartOfCombat"].BaseValue, actor.Player);
    }

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (!IsOwnTurn(participants) || !TryGetActor(out var actor))
            return;
        Flash();
        await PlayerCmd.GainStars(DynamicVars["EndOfTurn"].BaseValue, actor.Player);
    }
}

public sealed class FracturedUrnPower : CorruptionBuffPower
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new SummonVar("StartOfCombat", 17), new SummonVar("StartOfTurn", 5)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.Static(StaticHoverTip.SummonStatic)];

    public override Task BeforeCombatStart() => Summon(DynamicVars["StartOfCombat"].BaseValue);

    public override Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants,
        ICombatState combatState) =>
        IsOwnTurn(participants) ? Summon(DynamicVars["StartOfTurn"].BaseValue) : Task.CompletedTask;

    private async Task Summon(decimal amount)
    {
        if (!TryGetActor(out var actor))
            return;
        Flash();
        await Owned(actor, context => OstyCmd.Summon(context, actor.Player, amount, this));
    }
}

public sealed class OvervoltedMatrixPower : CorruptionBuffPower
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<FocusPower>(1)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.Static(StaticHoverTip.Channeling), HoverTipFactory.FromOrb<LightningOrb>(),
        HoverTipFactory.FromOrb<FrostOrb>(), HoverTipFactory.FromOrb<DarkOrb>(), HoverTipFactory.FromPower<FocusPower>()
    ];

    public override async Task BeforeCombatStart()
    {
        if (!TryGetActor(out var actor))
            return;
        Flash();
        await Owned(actor, async context =>
        {
            await OrbCmd.Channel<LightningOrb>(context, actor.Player);
            await OrbCmd.Channel<FrostOrb>(context, actor.Player);
            await OrbCmd.Channel<DarkOrb>(context, actor.Player);
        });
    }

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (!IsOwnTurn(participants))
            return;
        Flash();
        await PowerCmd.Apply<FocusPower>(choiceContext, Owner,
            DynamicVars["FocusPower"].BaseValue, Owner, null);
    }
}

public sealed class EchoShardPower : CorruptionBuffPower
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new EnergyVar(1)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.ForEnergy(this)];

    // Applied at the native energy reset, so the human sees the upcoming energy during their turn.
    public override decimal ModifyMaxEnergy(Player player, decimal amount) =>
        IsActor(player) ? amount + DynamicVars.Energy.BaseValue : amount;
}
