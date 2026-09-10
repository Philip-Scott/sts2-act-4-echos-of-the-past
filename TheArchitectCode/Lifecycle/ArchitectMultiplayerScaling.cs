using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Singleton;
using TheArchitect.TheArchitectCode.Encounters;

namespace TheArchitect.TheArchitectCode.Lifecycle;

internal static class ArchitectMultiplayerScaling
{
    // The native scaling table ends at Act 3; Act 4 uses its final-act boss tier.
    internal static decimal GetScaling(EncounterModel? encounter, int actIndex) =>
        MultiplayerScalingModel.GetMultiplayerScaling(encounter,
            encounter is ArchitectEncounter && actIndex == 3 ? 2 : actIndex);
}
