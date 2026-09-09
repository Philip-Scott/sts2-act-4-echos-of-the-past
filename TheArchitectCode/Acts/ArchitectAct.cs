using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using TheArchitect.TheArchitectCode.Encounters;

namespace TheArchitect.TheArchitectCode.Acts;

public sealed class ArchitectAct : CustomActModel, ILocalizationProvider
{
    internal const string BackgroundTitle = "the_architect_event_encounter";
    internal const string ArchitectBackgroundScenePath =
        "res://scenes/backgrounds/the_architect_event_encounter/the_architect_event_encounter_background.tscn";

    public ArchitectAct() : base(4, autoAdd: false) { }

    public List<(string, string)> Localization => new ActLoc("The Architect");
    protected override int BaseNumberOfRooms => 1;
    protected override int NumberOfWeakEncounters => 0;
    public override IEnumerable<AncientEventModel> AllAncients => [ArchitectModels.Ancient];
    public override IEnumerable<EventModel> AllEvents => [];
    public override IEnumerable<EncounterModel> GenerateAllEncounters() => [ArchitectModels.Encounter];
    protected override string CustomBackgroundScenePath => ArchitectBackgroundScenePath;
    protected override BackgroundAssets CustomGenerateBackgroundAssets(Rng rng) => new(BackgroundTitle, rng);
    // Map decorations are Texture2D resources, not room background scenes.
    protected override string CustomMapTopBgPath => ModelDb.Act<Glory>().MapTopBgPath;
    protected override string CustomMapMidBgPath => ModelDb.Act<Glory>().MapMidBgPath;
    protected override string CustomMapBotBgPath => ModelDb.Act<Glory>().MapBotBgPath;
    // Keep the native campfire/lighting contract; ArchitectRoomBackgrounds replaces only its scenery.
    protected override string CustomRestSiteBackgroundPath => ModelDb.Act<Glory>().RestSiteBackgroundPath;
    public override Color MapBgColor => ModelDb.Act<Glory>().MapBgColor;
    public override Color MapTraveledColor => new("292929");
    public override Color MapUntraveledColor => new("555555");
    protected override ActMap CustomCreateMap(RunState runState, bool replaceTreasureWithElites) =>
        new ArchitectMap(Ancient != null);

    public void InitializeRooms()
    {
        AssertMutable();
        _rooms = new RoomSet { Ancient = ArchitectModels.Ancient, Boss = ArchitectModels.Encounter };
    }
}
