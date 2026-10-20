using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace TheArchitect.TheArchitectCode.Powers;

public sealed class PlayerStunnedPower : CustomPowerModel
{
    internal static Task ApplyTo(Creature creature)
    {
        var power = ModelDb.GetById<PlayerStunnedPower>(
            new ModelId("POWER", "THEARCHITECT-PLAYER_STUNNED_POWER")).ToMutable();
        return PowerCmd.Apply(new ThrowingPlayerChoiceContext(), power, creature, 1, null, null);
    }

    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override string CustomPackedIconPath => ModelDb.Power<NoDrawPower>().PackedIconPath;
    public override string CustomBigIconPath => ModelDb.Power<NoDrawPower>().ResolvedBigIconPath;

    public override bool ShouldDraw(Player player, bool fromHandDraw) => player.Creature != Owner;

    public override Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
        IEnumerable<Creature> participants) =>
        participants.Contains(Owner) ? PowerCmd.Remove(this) : Task.CompletedTask;
}
