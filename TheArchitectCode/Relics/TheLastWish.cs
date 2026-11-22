using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Runs;

namespace TheArchitect.TheArchitectCode.Relics;

[Pool(typeof(SharedRelicPool))]
public sealed class TheLastWish : UnwrittenRelic
{
    public override bool HasUponPickupEffect => true;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new GoldVar(99), new PowerVar<PlatingPower>(4), new PowerVar<StrengthPower>(1)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        [HoverTipFactory.FromPower<PlatingPower>(), HoverTipFactory.FromPower<StrengthPower>()];

    // Blitz has little to spend Gold on, so the pickup grants a relic there instead.
    public override async Task AfterObtained()
    {
        if (Owner.RunState.GameMode == GameMode.Blitz)
            await RelicCmd.Obtain(RelicFactory.PullNextRelicForRewardFromFront(Owner).ToMutable(), Owner);
        else
            await PlayerCmd.GainGold(DynamicVars.Gold.BaseValue, Owner);
    }

    internal static void AddModeTo(RelicModel relic, LocString text)
    {
        if (relic is TheLastWish)
            text.Add("IsBlitz", RunManager.Instance.DebugOnlyGetState()?.GameMode == GameMode.Blitz);
    }

    public override async Task BeforeCombatStart()
    {
        Flash();
        var context = new ThrowingPlayerChoiceContext();
        await PowerCmd.Apply<PlatingPower>(context, Owner.Creature,
            DynamicVars["PlatingPower"].BaseValue, Owner.Creature, null);
        await PowerCmd.Apply<StrengthPower>(context, Owner.Creature,
            DynamicVars.Strength.BaseValue, Owner.Creature, null);
    }
}

[HarmonyPatch(typeof(RelicModel), nameof(RelicModel.DynamicDescription), MethodType.Getter)]
internal static class TheLastWishDescriptionPatch
{
    private static void Postfix(RelicModel __instance, LocString __result) => TheLastWish.AddModeTo(__instance, __result);
}

[HarmonyPatch(typeof(RelicModel), nameof(RelicModel.DynamicEventDescription), MethodType.Getter)]
internal static class TheLastWishEventDescriptionPatch
{
    private static void Postfix(RelicModel __instance, LocString __result) => TheLastWish.AddModeTo(__instance, __result);
}
