using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

namespace TheArchitect.TheArchitectCode.Powers;

[HarmonyPatch(typeof(ArtifactPower), nameof(ArtifactPower.GetScaledAmountForMultiplayer))]
internal static class RingOfTheViperArtifactScaling
{
    private static bool Prefix(Creature target, decimal amount, ref decimal __result)
    {
        if (!target.Powers.Any(power => power.Id == CorruptedPlayerBuffs.RingOfTheViper))
            return true;
        // Each Silent already has its own buff; native scaling adds another stack per party member.
        __result = amount;
        return false;
    }
}
