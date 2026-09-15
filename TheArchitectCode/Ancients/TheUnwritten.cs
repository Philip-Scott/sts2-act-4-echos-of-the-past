using BaseLib.Abstracts;
using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using TheArchitect.TheArchitectCode.Acts;

namespace TheArchitect.TheArchitectCode.Ancients;

// Keep the original model type and serialized ID for existing runs and history.
public sealed class TheUnwritten : CustomAncientModel
{
    internal const string BackgroundTexturePath = "res://TheArchitect/images/ancients/the_watcher.png";

    public override bool IsValidForAct(ActModel act) => act is ArchitectAct;
    public override Color ButtonColor => new("755687");
    public override Color DialogueColor => new("493455");
    public override string CustomMapIconPath => "res://TheArchitect/images/map/the_watcher.png";
    public override string CustomMapIconOutlinePath => "res://TheArchitect/images/map/the_watcher_outline.png";
    public override string CustomRunHistoryIconPath => CustomMapIconPath;
    public override string CustomRunHistoryIconOutlinePath => CustomMapIconOutlinePath;
    public override IEnumerable<string> GetAssetPaths(IRunState runState) =>
        ["res://scenes/events/ancient_event_layout.tscn",
            BackgroundTexturePath,
            CustomMapIconPath, CustomMapIconOutlinePath];

    protected override OptionPools MakeOptionPools => new(
        MakePool(Relic("THE_LAST_WISH"), Relic("GOLDEN_EYE"), Relic("ORANGE_PEARL"), Relic("LAST_MEAL")),
        MakePool(Relic("LOOSE_THREAD"), Relic("DIAMOND_HAND"), Relic("DEUS_EX_MACHINA"), Relic("VIOLET_LOTUS")),
        MakePool(Relic("NUREMBERG_EGG"), Relic("RITUAL_DAGGER"), Relic("DEVA_FORM"), Relic("HANDHELD_MIRROR")));

    // Resolve canonical prefixed IDs, including after the game's normal startup caches generic lookups.
    private static RelicModel Relic(string entry) =>
        ModelDb.GetById<RelicModel>(new ModelId("RELIC", $"THEARCHITECT-{entry}"));
}
