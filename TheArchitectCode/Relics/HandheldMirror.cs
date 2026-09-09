using BaseLib.Extensions;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.RelicPools;

namespace TheArchitect.TheArchitectCode.Relics;

[Pool(typeof(SharedRelicPool))]
public sealed class HandheldMirror : UnwrittenRelic
{
    public HandheldMirror() =>
        this.AddCustomAncientSpawnCondition(ancient => CanOffer(ancient.Owner ??
            throw new InvalidOperationException("Handheld Mirror eligibility requires the Ancient's owner.")));

    public override bool HasUponPickupEffect => true;

    public static bool CanOffer(Player? owner) =>
        owner is not null && owner.Creature.IsAlive &&
        MirrorDuplication.CapturePopulation(owner).Length >= MirrorDuplication.CopyCount;

    public override async Task AfterObtained()
    {
        if (!CanOffer(Owner))
            throw new InvalidOperationException("Handheld Mirror requires three distinct, non-melted owned relic types that are not blocklisted.");

        // Prepare all three before acquisition hooks can change the inventory or source state.
        var copies = MirrorDuplication.PrepareCopies(Owner.Relics, Owner.PlayerRng.Rewards.NextInt);
        foreach (var copy in copies)
            await RelicCmd.Obtain(copy, Owner);
    }
}
