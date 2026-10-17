using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Singleton;
using MegaCrit.Sts2.Core.Runs;
using TheArchitect.TheArchitectCode.Encounters;

namespace TheArchitect.TheArchitectCode.Lifecycle;

internal static class ArchitectMultiplayerScaling
{
    // The native scaling table ends at Act 3; Act 4 uses its final-act boss tier.
    internal static decimal GetScaling(EncounterModel? encounter, int actIndex) =>
        MultiplayerScalingModel.GetMultiplayerScaling(encounter, GetActIndex(encounter, actIndex));

    internal static int GetActIndex(EncounterModel? encounter, int actIndex) =>
        actIndex == 3 && (encounter is ArchitectEncounter or CorruptedPlayerEncounter ||
            encounter is TheArchitectEventEncounter && ArchitectEnding.IsActive(RunManager.Instance.DebugOnlyGetState()))
            ? 2 : actIndex;

    // Native callers such as enemy block scaling (3+ players) pass the raw act index.
    [HarmonyPatch(typeof(MultiplayerScalingModel), nameof(MultiplayerScalingModel.GetMultiplayerScaling))]
    internal static class ActFourPatch
    {
        private static void Prefix(EncounterModel? encounter, ref int actIndex) =>
            actIndex = GetActIndex(encounter, actIndex);
    }
}
