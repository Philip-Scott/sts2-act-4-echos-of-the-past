using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Rooms;

namespace TheArchitect.TheArchitectCode.Relics;

[Pool(typeof(SharedRelicPool))]
public sealed class NurembergEgg : UnwrittenRelic
{
    private int _cardsPlayed;
    private bool _triggered;
    private bool _counterHidden;
    private CardModel? _exhaustCard;

    public override bool ShowCounter => !_counterHidden;
    public override int DisplayAmount => _cardsPlayed;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new CardsVar(12), new DynamicVar("Replay", 2)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromKeyword(CardKeyword.Exhaust)];

    public override Task BeforeCombatStart() => ResetCombat(rearmCounter: true);

    public override Task AfterCombatEnd(CombatRoom room) => ResetCombat(rearmCounter: false);

    private Task ResetCombat(bool rearmCounter)
    {
        _counterHidden = !rearmCounter && (_counterHidden || _triggered);
        _cardsPlayed = 0;
        _triggered = false;
        _exhaustCard = null;
        InvokeDisplayAmountChanged();
        return Task.CompletedTask;
    }

    private bool IsTwelfth(CardModel card) =>
        Owner.PlayerCombatState != null && card.Owner == Owner &&
        !_triggered && _cardsPlayed == DynamicVars.Cards.IntValue - 1;

    public override int ModifyCardPlayCount(CardModel card, Creature? target, int playCount) =>
        IsTwelfth(card) ? playCount + DynamicVars["Replay"].IntValue : playCount;

    public override Task AfterModifyingCardPlayCount(CardModel card)
    {
        if (IsTwelfth(card))
            Flash();
        return Task.CompletedTask;
    }

    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (!_counterHidden && _triggered && cardPlay.Player == Owner &&
            cardPlay.Card == _exhaustCard && cardPlay.IsLastInSeries)
        {
            _counterHidden = true;
            InvokeDisplayAmountChanged();
        }
        return Task.CompletedTask;
    }

    public override Task BeforeCardPlayed(CardPlay cardPlay)
    {
        if (cardPlay.Player == Owner && Owner.PlayerCombatState != null && cardPlay.IsFirstInSeries)
        {
            if (IsTwelfth(cardPlay.Card))
            {
                _triggered = true;
                _exhaustCard = cardPlay.Card;
            }
            _cardsPlayed = Math.Min(_cardsPlayed + 1, DynamicVars.Cards.IntValue);
            InvokeDisplayAmountChanged();
        }
        return Task.CompletedTask;
    }

    // Native play resolves its final pile before asking for the replay count.
    public override CardLocation ModifyCardPlayResultLocation(CardModel card, bool isAutoPlay,
        ResourceInfo resources, CardLocation result) =>
        IsTwelfth(card)
            ? new CardLocation(Owner, PileType.Exhaust, CardPilePosition.Bottom)
            : result;

    public override bool TryModifyKeywordsInCombat(CardModel card, ISet<CardKeyword> keywords) =>
        Owner.PlayerCombatState != null && card.Owner == Owner && card == _exhaustCard &&
        keywords.Add(CardKeyword.Exhaust);
}
