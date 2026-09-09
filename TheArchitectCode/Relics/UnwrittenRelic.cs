using BaseLib.Abstracts;
using BaseLib.Extensions;
using MegaCrit.Sts2.Core.Entities.Relics;

namespace TheArchitect.TheArchitectCode.Relics;

public abstract class UnwrittenRelic : CustomRelicModel
{
    private string IconName => Id.Entry.RemovePrefix().ToLowerInvariant();

    public override RelicRarity Rarity => RelicRarity.Ancient;
    public override string PackedIconPath => $"res://TheArchitect/images/relics/{IconName}.png";
    protected override string PackedIconOutlinePath => $"res://TheArchitect/images/relics/{IconName}_outline.png";
    protected override string BigIconPath => $"res://TheArchitect/images/relics/big/{IconName}.png";
}
