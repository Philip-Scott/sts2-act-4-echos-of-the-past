using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace TheArchitect.TheArchitectCode.Powers;

public sealed class ArchitectBeatOfDeathPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override string CustomPackedIconPath => ModelDb.Power<ThornsPower>().PackedIconPath;
    public override string CustomBigIconPath => ModelDb.Power<ThornsPower>().ResolvedBigIconPath;

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner.IsDead || cardPlay.Player.Creature.IsDead || cardPlay.Player.Creature.Side == Owner.Side)
            return;
        Flash();
        await CreatureCmd.Damage(choiceContext, cardPlay.Player.Creature, Amount, ValueProp.Unpowered, Owner, null, null);
    }
}
