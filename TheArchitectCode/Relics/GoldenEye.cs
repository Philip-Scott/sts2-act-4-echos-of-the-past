using BaseLib.Utils;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Rooms;

namespace TheArchitect.TheArchitectCode.Relics;

[Pool(typeof(SharedRelicPool))]
public sealed class GoldenEye : UnwrittenRelic
{
    private bool _scried;

    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Scry", 8)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        [new HoverTip(new LocString("static_hover_tips", "THEARCHITECT-SCRY.title"),
            new LocString("static_hover_tips", "THEARCHITECT-SCRY.description"))];

    public override Task BeforeCombatStart()
    {
        _scried = false;
        return Task.CompletedTask;
    }

    public override Task AfterCombatEnd(CombatRoom room) => BeforeCombatStart();

    public override async Task BeforeHandDraw(Player player, PlayerChoiceContext choiceContext, ICombatState combatState)
    {
        if (player != Owner || _scried || Owner.PlayerCombatState?.TurnNumber != 1)
            return;

        _scried = true;
        var cards = ScryCards();
        if (cards.Length == 0)
            return;

        Flash();
        var prefs = new CardSelectorPrefs(new LocString("relics", "THEARCHITECT-GOLDEN_EYE.scryPrompt"), 0, cards.Length)
        {
            RequireManualConfirmation = true,
            Comparison = (left, right) => Array.IndexOf(cards, left).CompareTo(Array.IndexOf(cards, right))
        };
        var selected = await CardSelectCmd.FromSimpleGrid(choiceContext, cards, Owner, prefs);
        // Move directly: Scry is not a discard-from-hand effect and must not trigger those hooks.
        await CardPileCmd.Add(selected, Owner.PlayerCombatState!.DiscardPile);
    }

    internal CardModel[] ScryCards() =>
        Owner.PlayerCombatState?.DrawPile.Cards.Take(DynamicVars["Scry"].IntValue).ToArray() ?? [];
}
