using BaseLib.Abstracts;
using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using TheArchitect.TheArchitectCode.Acts;
using TheArchitect.TheArchitectCode.Lifecycle;

namespace TheArchitect.TheArchitectCode.Ancients;

public sealed class TheUnwritten : CustomAncientModel
{
    public override bool IsValidForAct(ActModel act) => act is ArchitectAct;
    public override Color ButtonColor => new("46616b");
    public override Color DialogueColor => new("344e5c");
    public override string CustomMapIconPath => "res://TheArchitect/images/map/the_unwritten.png";
    public override string CustomMapIconOutlinePath => "res://TheArchitect/images/map/the_unwritten_outline.png";
    public override string CustomRunHistoryIconPath => CustomMapIconPath;
    public override string CustomRunHistoryIconOutlinePath => CustomMapIconOutlinePath;
    public override IEnumerable<string> GetAssetPaths(IRunState runState) =>
        ["res://scenes/events/ancient_event_layout.tscn",
            ArchitectRoomBackgrounds.RestBackgroundPath, CustomMapIconPath, CustomMapIconOutlinePath];

    protected override OptionPools MakeOptionPools => new(
        MakePool(Relic("LOOSE_THREAD"), Relic("CROOKED_NEEDLE"), Relic("ORANGE_PEARL"), Relic("DIAMOND_HAND")),
        MakePool(Relic("UNSPENT_POSSIBILITY"), Relic("LAST_MEAL")),
        MakePool(Relic("BORROWED_TOMORROW"), Relic("HANDHELD_MIRROR")));

    // Resolve canonical prefixed IDs, including after the game's normal startup caches generic lookups.
    private static RelicModel Relic(string entry) =>
        ModelDb.GetById<RelicModel>(new ModelId("RELIC", $"THEARCHITECT-{entry}"));
}
