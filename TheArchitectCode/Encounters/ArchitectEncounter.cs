using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using TheArchitect.TheArchitectCode.Acts;
using TheArchitect.TheArchitectCode.Lifecycle;

namespace TheArchitect.TheArchitectCode.Encounters;

public sealed class ArchitectEncounter : CustomEncounterModel
{
    public ArchitectEncounter() : base(RoomType.Boss, autoAdd: false) { }

    public override bool IsValidForAct(ActModel act) => act is ArchitectAct;
    public override bool ShouldGiveRewards => false;
    public override bool HasScene => false;
    public override string CustomBgm => ArchitectMusic.TrackPath;
    public override string BossNodePath => "res://TheArchitect/images/map/architect_boss";
    public override IReadOnlyList<string> Slots => [];
    public override string? CustomRunHistoryIconPath => BossNodePath + ".png";
    public override string? CustomRunHistoryIconOutlinePath => BossNodePath + "_outline.png";
    public override IEnumerable<MonsterModel> AllPossibleMonsters => [ArchitectModels.Boss];

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters() =>
        [(ArchitectModels.Boss.ToMutable(), null)];
}
