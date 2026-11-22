using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models.Blitz;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using TheArchitect.TheArchitectCode.Acts;

namespace TheArchitect.TheArchitectCode.Lifecycle;

// Blitz always builds its fixed line and requires a BlitzActMap, so it receives the Architect layout as one.
[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyGeneratedMap))]
internal static class BlitzArchitectMapPatch
{
    private static void Prefix(IRunState runState, ref ActMap map)
    {
        if (runState is { GameMode: GameMode.Blitz, Act: ArchitectAct act } && map is BlitzActMap)
            map = new BlitzActMap(SerializableActMap.FromActMap(act.CreateLayout(runState)), runState);
    }
}

// Blitz treats the Act 3 boss as final until the Architect Act is appended after its rewards.
[HarmonyPatch(typeof(BlitzRunModel), nameof(BlitzRunModel.TryReplaceDefaultRoomRewards))]
internal static class BlitzAct3BossRewardsPatch
{
    private static readonly Func<Player, CardRarity, CardCreationOptions> CardRewardOptions =
        AccessTools.MethodDelegate<Func<Player, CardRarity, CardCreationOptions>>(
            AccessTools.Method(typeof(BlitzRunModel), "GetCardRewardOptions") ??
            throw new MissingMethodException("The Blitz card reward options changed."));

    private static void Postfix(Player player, List<Reward> rewards, AbstractRoom room, ref bool __result)
    {
        if (__result || room.RoomType != RoomType.Boss ||
            player.RunState is not { CurrentActIndex: 2 } run || run.Acts.Any(act => act is ArchitectAct))
            return;
        rewards.Add(new CardReward(CardRewardOptions(player, CardRarity.Rare), 3, player));
        rewards.Add(new PotionReward(player));
        __result = true;
    }
}
