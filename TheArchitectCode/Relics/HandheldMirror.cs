using BaseLib.Extensions;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
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
        // Freeze and draw all three before obtaining anything: acquisition hooks may produce rewards.
        var population = MirrorDuplication.CapturePopulation(Owner);
        if (!Owner.Creature.IsAlive || population.Length < MirrorDuplication.CopyCount)
            throw new InvalidOperationException("Handheld Mirror requires three distinct, non-melted owned relic types other than itself.");

        var selected = MirrorDuplication.SelectThree(population, Owner.PlayerRng.Rewards.NextInt);
        foreach (var id in selected)
            await RelicCmd.Obtain(ModelDb.GetById<RelicModel>(id).ToMutable(), Owner);
    }
}
