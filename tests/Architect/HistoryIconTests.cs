using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using TheArchitect.TheArchitectCode.Lifecycle;

internal static class HistoryIconTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("history icons: serialized model IDs resolve without ModelDb lookups", () =>
        {
            Check(ArchitectHistoryIcons.Resolve(new ModelId("EVENT", "THEARCHITECT-THE_UNWRITTEN"), false),
                "res://TheArchitect/images/map/the_watcher.png");
            Check(ArchitectHistoryIcons.Resolve(new ModelId("EVENT", "THEARCHITECT-THE_UNWRITTEN"), true),
                "res://TheArchitect/images/map/the_watcher_outline.png");
            Check(ArchitectHistoryIcons.Resolve(new ModelId("ENCOUNTER", "THEARCHITECT-ARCHITECT_ENCOUNTER"), false),
                "res://TheArchitect/images/map/architect_boss.png");
            Check(ArchitectHistoryIcons.Resolve(new ModelId("ENCOUNTER", "THEARCHITECT-ARCHITECT_ENCOUNTER"), true),
                "res://TheArchitect/images/map/architect_boss_outline.png");
        });

        test("history icons: unrelated IDs and categories retain native behavior", () =>
        {
            Check(ArchitectHistoryIcons.Resolve(null, false), null);
            Check(ArchitectHistoryIcons.Resolve(new ModelId("EVENT", "NEOW"), true), null);
            Check(ArchitectHistoryIcons.Resolve(new ModelId("ENCOUNTER", "THEARCHITECT-THE_UNWRITTEN"), false), null);
            Check(ArchitectHistoryIcons.Resolve(new ModelId("EVENT", "THEARCHITECT-ARCHITECT_ENCOUNTER"), true), null);
        });

        test("history icons: registered native helpers return character and boss assets", () =>
        {
            var harmony = new Harmony("TheArchitect.Tests.HistoryIcons");
            try
            {
                harmony.CreateClassProcessor(typeof(ArchitectHistoryIconPatch)).Patch();
                harmony.CreateClassProcessor(typeof(ArchitectHistoryIconOutlinePatch)).Patch();
                var ancient = new ModelId("EVENT", "THEARCHITECT-THE_UNWRITTEN");
                var boss = new ModelId("ENCOUNTER", "THEARCHITECT-ARCHITECT_ENCOUNTER");
                Check(ImageHelper.GetRoomIconPath(MapPointType.Ancient, RoomType.Event, ancient),
                    "res://TheArchitect/images/map/the_watcher.png");
                Check(ImageHelper.GetRoomIconOutlinePath(MapPointType.Ancient, RoomType.Event, ancient),
                    "res://TheArchitect/images/map/the_watcher_outline.png");
                Check(ImageHelper.GetRoomIconPath(MapPointType.Boss, RoomType.Boss, boss),
                    "res://TheArchitect/images/map/architect_boss.png");
                Check(ImageHelper.GetRoomIconOutlinePath(MapPointType.Boss, RoomType.Boss, boss),
                    "res://TheArchitect/images/map/architect_boss_outline.png");
                Check(ImageHelper.GetRoomIconPath(MapPointType.Ancient, RoomType.Event,
                    new ModelId("EVENT", "NEOW")), "res://images/ui/run_history/neow.png");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        });
    }

    private static void Check(string? actual, string? expected)
    {
        if (actual != expected)
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}
