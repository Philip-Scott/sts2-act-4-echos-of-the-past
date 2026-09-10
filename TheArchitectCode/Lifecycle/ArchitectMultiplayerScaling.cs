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
        actIndex == 3 && (encounter is ArchitectEncounter ||
            encounter is TheArchitectEventEncounter && ArchitectEnding.IsActive(RunManager.Instance.DebugOnlyGetState()))
            ? 2 : actIndex;
}
