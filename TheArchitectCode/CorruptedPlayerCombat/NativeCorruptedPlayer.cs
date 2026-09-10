using System.Runtime.CompilerServices;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using TheArchitect.TheArchitectCode.Monsters;
using TheArchitect.TheArchitectCode.UI;

namespace TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

public sealed class NativeCorruptedPlayer
{
    private static readonly ConditionalWeakTable<Player, NativeCorruptedPlayer> Players = new();
    private static readonly ConditionalWeakTable<Creature, NativeCorruptedPlayer> Creatures = new();
    public static bool TryGet(Player player, out NativeCorruptedPlayer actor) => Players.TryGetValue(player, out actor!);
    public static bool TryGet(Creature creature, out NativeCorruptedPlayer actor) => Creatures.TryGetValue(creature, out actor!);

    public Player Player { get; }
    public Creature Body { get; }
    public PlayerCombatState State => Player.PlayerCombatState!;
    public int CompletedTurns { get; private set; }
    public bool Executing { get; private set; }
    public bool Cleaned { get; private set; }
    internal bool HandPrepared { get; private set; }
    public IReadOnlyList<CardModel> Cards { get; }
    public event Action? Changed;
    internal event Action? TurnStarting;
    internal event Action? TurnFinished;
    internal NativeCombatView View { get; }
    private readonly ICombatState _combat;
    private readonly Creature? _counterpart;
    private readonly NativeChoiceContext _context;
    private readonly Dictionary<CardModel, string> _cardIds = new();
    private readonly Dictionary<CardModel, string> _reasons = new();
    private readonly Dictionary<CardModel, string> _unsupported = new();
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private bool _endTurnRequested;
    internal event Action<CardModel>? CardPlayed;

    public NativeCorruptedPlayer(Creature body, CharacterModel character, IReadOnlyList<JsonElement> deck, string seed,
        Creature? counterpart = null)
    {
        if (body.Monster is not CorruptedPlayer || body.CombatState is not CombatState combat ||
            combat.Players.Count == 0 || body.CombatId == null)
            throw new InvalidOperationException("Native Corrupted Player requires a live enemy with a combat identity.");
        if (counterpart != null && !combat.PlayerCreatures.Contains(counterpart))
            throw new InvalidOperationException("A Corrupted Player counterpart must belong to the human party.");
        Body = body;
        _combat = combat;
        _counterpart = counterpart;
        // Avoid CreateForNewRun's discovery/save notifications and starter relic acquisition.
        var constructor = AccessTools.GetDeclaredConstructors(typeof(Player)).Single(c => c.GetParameters().Length == 15);
        var privateId = ulong.MaxValue - 17;
        var reservedIds = combat.RunState.Players.Select(player => player.NetId)
            .Concat(In(combat).Select(actor => actor.Player.NetId)).ToHashSet();
        while (reservedIds.Contains(privateId))
            privateId = checked(privateId - 1);
        Player = (Player)constructor.Invoke([character, privateId, (int)body.CurrentHp,
            (int)body.MaxHp, character.MaxEnergy, 0, 0, character.BaseOrbSlotCount,
            new RelicGrabBag(), UnlockState.all, null, null, null, null, null]);
        var run = RunState.CreateForNewRun([Player], [ModelDb.Act<Glory>().ToMutable()], [],
            GameMode.Standard, 0, seed);
        // Bind once: precompiled native/BaseLib callers can have inlined Player.Creature.
        AccessTools.Field(typeof(Player), "<Creature>k__BackingField").SetValue(Player, Body);
        View = new NativeCombatView(this, combat);
        Players.Add(Player, this);
        Creatures.Add(Body, this);
        _context = new NativeChoiceContext(Player);
        foreach (var raw in deck)
        {
            var saved = raw.Deserialize(JsonSerializationUtility.GetTypeInfo<SerializableCard>())
                ?? throw new JsonException("The Corrupted Player snapshot contains a null card.");
            if (saved.Id == null || ModelDb.GetByIdOrNull<CardModel>(saved.Id) == null)
                throw new NotSupportedException($"The Corrupted Player's saved card {saved.Id} is not installed.");
            var card = run.LoadCard(NativeCardSupport.ForNativeLoad(raw, saved), Player);
            if ((NativeCardSupport.SavedReason(raw) ?? NativeCardSupport.Reason(card)) is { } reason)
            {
                _unsupported.Add(card, reason);
                MainFile.Logger.Warn($"Corrupted Player {card.Id}: {reason}; preserved but not executed.");
            }
            Player.Deck.AddInternal(card, silent: true);
        }
        Player.ResetCombatState();
        Player.PopulateCombatState(run.Rng.Shuffle, combat);
        Cards = State.DrawPile.Cards.ToArray();
        for (var i = 0; i < Cards.Count; i++)
            _cardIds.Add(Cards[i], $"snapshot:{i}");
        AssertIdentity();
        Trace("bound");
    }

    public void AssertIdentity()
    {
        // Use the same explicit bridge as native call sites: inlined identity getters
        // can bypass their Harmony detours in our own callers too.
        if (Environment.CurrentManagedThreadId != _threadId || Player.Creature != Body ||
            NativeCombatCallSites.CreatureOwner(Body) != Player || NativeCombatCallSites.IsPartyPlayer(Body) ||
            !Body.IsMonster || Body.Side != CombatSide.Enemy ||
            _combat.Players.Contains(Player) || _combat.RunState.Players.Contains(Player) ||
            _combat.Players.Any(player => !_combat.RunState.Players.Contains(player)))
            throw new InvalidOperationException("Native Corrupted Player violated actor/thread/party isolation: " +
                $"thread={Environment.CurrentManagedThreadId}/{_threadId}, playerBody={Player.Creature == Body}, " +
                $"bodyOwner={NativeCombatCallSites.CreatureOwner(Body) == Player}, " +
                $"isPlayer={NativeCombatCallSites.IsPartyPlayer(Body)}, isMonster={Body.IsMonster}, " +
                $"side={Body.Side}, inCombatParty={_combat.Players.Contains(Player)}, " +
                $"inRunParty={_combat.RunState.Players.Contains(Player)}, " +
                $"combatPlayers={_combat.Players.Count}, runPlayers={_combat.RunState.Players.Count}.");
    }

    private bool CanAct => !Cleaned && Body.IsAlive && !CombatManager.Instance.IsOverOrEnding &&
        _combat.PlayerCreatures.Any(creature => creature.IsAlive);

    // Called after the engine clears this body's block, before global AfterSideTurnStart.
    internal async Task StartTurn()
    {
        AssertIdentity();
        if (!CanAct)
            return;
        if (State.Phase != PlayerTurnPhase.None)
            throw new InvalidOperationException("Native Corrupted Player turn was started twice.");
        // Extra enemy turns have no intervening human turn to prepare their hand.
        await PrepareTurn();
        State.Phase = PlayerTurnPhase.Start;
        TurnStarting?.Invoke();
        await Hook.AfterPlayerTurnStart(_combat, _context, Player);
        Trace("turn-start");
        Changed?.Invoke();
    }

    internal async Task PrepareTurn()
    {
        AssertIdentity();
        if (!CanAct || HandPrepared)
            return;
        if (State.Phase != PlayerTurnPhase.None)
            throw new InvalidOperationException("Native Corrupted Player hand can only be prepared between turns.");
        _reasons.Clear();
        _endTurnRequested = false;
        if (CompletedTurns > 0)
            State.IncrementTurnNumber();
        if (Hook.ShouldPlayerResetEnergy(_combat, Player))
            State.ResetEnergy();
        else
            State.AddMaxEnergyToCurrent();
        await Hook.AfterEnergyReset(_combat, Player);
        await Hook.BeforeHandDraw(_combat, Player, _context);
        var draw = Hook.ModifyHandDraw(_combat, Player, 5, out var modifiers);
        await Hook.AfterModifyingHandDraw(_combat, modifiers);
        if (State.TurnNumber == 1)
        {
            var bottom = State.DrawPile.Cards.Where(card => card.Enchantment?.ShouldStartAtBottomOfDrawPile == true).ToArray();
            foreach (var card in bottom)
                State.DrawPile.MoveToBottomInternal(card);
            var innate = State.DrawPile.Cards.Where(card => card.Keywords.Contains(CardKeyword.Innate)).Except(bottom).ToArray();
            foreach (var card in innate)
                State.DrawPile.MoveToTopInternal(card);
            draw = Math.Min(CardPile.MaxCardsInHand, Math.Max(draw, innate.Length));
        }
        await CardPileCmd.Draw(_context, draw, Player, fromHandDraw: true);
        HandPrepared = true;
        Trace("hand-prepared");
        Changed?.Invoke();
    }

    public async Task ExecuteTurn()
    {
        AssertIdentity();
        if (Executing || _combat.CurrentSide != CombatSide.Enemy || State.Phase != PlayerTurnPhase.Start)
            throw new InvalidOperationException($"Native cards require enemy turn setup: executing={Executing}, " +
                $"side={_combat.CurrentSide}, phase={State.Phase}, cleaned={Cleaned}, turns={CompletedTurns}.");
        Executing = true;
        try
        {
            await State.OrbQueue.AfterTurnStart(_context);
            State.Phase = PlayerTurnPhase.AutoPrePlay;
            await CombatManager.Instance.CheckForEmptyHand(null, _context, Player);
            await Hook.AfterAutoPrePlayPhaseEntered(_context, _combat, Player);
            State.Phase = PlayerTurnPhase.Play;
            var plays = 0;
            while (CanAct && !_endTurnRequested)
            {
                CardModel? next = null;
                Creature? target = null;
                foreach (var card in State.Hand.Cards)
                {
                    if (UnsupportedReason(card) is { } unsupported)
                    {
                        _reasons[card] = unsupported;
                        continue;
                    }
                    if (!card.CanPlay(out var reason, out _))
                    {
                        _reasons[card] = reason.ToString();
                        continue;
                    }
                    var candidate = SelectTarget(card);
                    if (!card.IsValidTarget(candidate))
                    {
                        _reasons[card] = "No valid target";
                        continue;
                    }
                    next = card;
                    target = candidate;
                    break;
                }
                if (next == null)
                    break;
                if (++plays > 100)
                {
                    MainFile.Logger.Warn("Native Corrupted Player turn stopped at the 100-play safety limit.");
                    _reasons[next] = "100-play safety limit";
                    break;
                }
                _reasons.Remove(next);
                var (energy, stars) = await next.SpendResources();
                await next.OnPlayWrapper(_context, target, isAutoPlay: false, new ResourceInfo
                {
                    EnergySpent = energy, EnergyValue = energy, StarsSpent = stars, StarValue = stars
                }, skipCardPileVisuals: false);
                AssertIdentity();
                Trace($"played:{next.Id.Entry}");
                CardPlayed?.Invoke(next);
                Changed?.Invoke();
            }
            foreach (var (card, reason) in _reasons.Where(entry => entry.Key.Pile == State.Hand))
                MainFile.Logger.Info($"NATIVE unplayed {card.Id}: {reason}");
            if (CanAct)
            {
                State.Phase = PlayerTurnPhase.AutoPostPlay;
                await Hook.AfterAutoPostPlayPhaseEntered(_context, _combat, Player);
                State.Phase = PlayerTurnPhase.End;
            }
        }
        finally
        {
            Executing = false;
            if (!CanAct)
                Cleanup();
        }
    }

    // The real enemy loop owns side hooks. Insert native hand/orb cleanup between
    // BeforeSideTurnEnd and AfterSideTurnEnd rather than dispatching those hooks twice.
    internal async Task FinishHand()
    {
        if (!CanAct || State.Phase != PlayerTurnPhase.End)
            return;
        Executing = true;
        try
        {
            await State.OrbQueue.BeforeTurnEnd(_context);
            var supported = State.Hand.Cards.Where(c => UnsupportedReason(c) == null).ToArray();
            var effects = supported.Where(c => c.HasTurnEndInHandEffect).ToArray();
            foreach (var card in supported.Except(effects).ToArray())
                if (card.Keywords.Contains(CardKeyword.Ethereal) && Hook.ShouldEtherealTrigger(_combat, card))
                    await CardCmd.Exhaust(_context, card, causedByEthereal: true);
            foreach (var card in effects)
            {
                if (!CanAct)
                    break;
                await CardPileCmd.Add(card, PileType.Play);
                await card.OnTurnEndInHandWrapper(_context);
                if (card.Keywords.Contains(CardKeyword.Ethereal))
                    await CardCmd.Exhaust(_context, card, causedByEthereal: true);
                else
                    await CardPileCmd.Add(card, PileType.Discard);
            }
            if (!CanAct)
                return;
            await Hook.BeforeFlush(_combat, Player);
            var flush = Hook.ShouldFlush(_combat, Player);
            var retained = State.Hand.Cards.Where(card => !flush || card.ShouldRetainThisTurn).ToArray();
            var discarded = State.Hand.Cards.Except(retained).ToArray();
            await CardPileCmd.Add(discarded, PileType.Discard);
            await Hook.AfterFlush(_combat, Player, _context, discarded, retained);
            State.EndOfTurnCleanup();
        }
        finally
        {
            Executing = false;
            if (!CanAct)
                Cleanup();
        }
    }

    internal void FinishTurn()
    {
        if (Cleaned)
            return;
        State.Phase = PlayerTurnPhase.None;
        HandPrepared = false;
        CompletedTurns++;
        Trace("turn-end");
        TurnFinished?.Invoke();
        Changed?.Invoke();
    }

    public void Cleanup()
    {
        if (Cleaned)
            return;
        if (Body.Monster is CorruptedPlayer monster)
            monster.StopTelegraph();
        // A lethal native card may defer state cleanup, but its orb UI must stop immediately.
        CorruptedPlayerOrbs.Hide(Body);
        if (Executing)
            return;
        Cleaned = true;
        HandPrepared = false;
        State.Phase = PlayerTurnPhase.None;
        State.AfterCombatEnd();
        Changed = null;
        TurnStarting = null;
        TurnFinished = null;
        CardPlayed = null;
        MainFile.Logger.Info("NATIVE actor cleaned up");
    }

    internal static IEnumerable<NativeCorruptedPlayer> In(ICombatState combat) =>
        combat.Enemies.Select(c => TryGet(c, out var actor) ? actor : null).OfType<NativeCorruptedPlayer>().ToArray();

    internal static void CleanupAll()
    {
        foreach (var entry in Players)
            entry.Value.Cleanup();
    }

    internal void RequestEndTurn() => _endTurnRequested = true;

    internal string? UnsupportedReason(CardModel card) =>
        _unsupported.GetValueOrDefault(card.DeckVersion ?? card) ?? NativeCardSupport.Reason(card);

    internal static bool IncludeHook(AbstractModel model)
    {
        var card = model switch
        {
            CardModel c => c,
            EnchantmentModel enchantment => enchantment.Card,
            AfflictionModel affliction => affliction.Card,
            BaseLib.Abstracts.CardModifier modifier => modifier.Owner,
            _ => null
        };
        return card == null || !card.IsMutable || card.Owner is not { } owner ||
            !TryGet(owner, out var actor) || actor.UnsupportedReason(card) == null;
    }

    internal async Task TakeExtraTurns()
    {
        for (var extra = 0; CanAct && Hook.ShouldTakeExtraTurn(_combat, Player); extra++)
        {
            if (extra == 20)
                throw new InvalidOperationException("Native Corrupted Player exceeded the 20-extra-turn safety limit.");
            await Hook.AfterTakingExtraTurn(_combat, Player);
            var participants = new[] { Body }.Concat(State.Pets).Where(c => c.IsAlive).ToArray();
            foreach (var creature in participants)
                creature.BeforeTurnStart(CombatSide.Enemy);
            await Hook.BeforeSideTurnStart(_combat, CombatSide.Enemy, participants);
            foreach (var creature in participants)
                await creature.AfterTurnStart(CombatSide.Enemy);
            foreach (var creature in participants)
                await NativeTurnPhases.AfterBlockCleared(_combat, creature);
            await Hook.AfterSideTurnStart(_combat, CombatSide.Enemy, participants);
            if (!CanAct)
                break;
            await ExecuteTurn();
            await NativeTurnPhases.BeforeSideTurnEnd(_combat, CombatSide.Enemy, participants);
            await NativeTurnPhases.FinishExtraTurn(_combat, participants);
        }
    }

    internal Creature? SelectTarget(CardModel card) => card.TargetType switch
    {
        TargetType.AnyEnemy => _counterpart is { IsHittable: true } && _combat.PlayerCreatures.Contains(_counterpart)
            ? _counterpart
            : _combat.PlayerCreatures.FirstOrDefault(c => c.IsHittable),
        TargetType.AnyAlly => _combat.GetTeammatesOf(Body).FirstOrDefault(c => c != Body && c.IsAlive),
        _ => null
    };

    public void Show(CorruptedPlayerTelegraph telegraph)
    {
        var pile = State.Hand;
        var cards = pile.Cards;
        telegraph.ShowPlan(cards.Select(card =>
        {
            if (!_cardIds.TryGetValue(card, out var id))
                _cardIds.Add(card, id = $"generated:{_cardIds.Count}");
            var unsupported = UnsupportedReason(card);
            return new CorruptedPlayerTelegraphCard(card, id, card.Id.Entry, null, unsupported != null, [],
                new Dictionary<string, decimal>(), NativeCurrentState: true,
                Status: unsupported ?? _reasons.GetValueOrDefault(card),
                PreviewTarget: unsupported == null ? SelectTarget(card) : null);
        }).ToArray(), false);
        telegraph.SetNativePlayer(Player);
    }

    private void Trace(string stage) => MainFile.Logger.Info(
        $"NATIVE {stage} turn={State.TurnNumber} energy={State.Energy} hp={Body.CurrentHp} block={Body.Block} " +
        $"powers=[{string.Join(",", Body.Powers.Select(p => $"{p.Id.Entry}:{p.Amount}"))}] " +
        $"humanHp={_combat.Players[0].Creature.CurrentHp} " +
        $"hand=[{string.Join(",", State.Hand.Cards.Select(c => $"{c.Id.Entry}+{c.CurrentUpgradeLevel}"))}] " +
        $"draw={State.DrawPile.Cards.Count} discard={State.DiscardPile.Cards.Count} exhaust={State.ExhaustPile.Cards.Count} " +
        $"orbs={State.OrbQueue.Orbs.Count} shuffle={Player.RunState.Rng.Shuffle.ToSerializable().counter}");
}

[HarmonyPatch(typeof(Creature), nameof(Creature.Player), MethodType.Getter)]
internal static class NativeCorruptedPlayerOwnerPatch
{
    private static void Postfix(Creature __instance, ref Player? __result)
    {
        if (NativeCorruptedPlayer.TryGet(__instance, out var actor))
            __result = actor.Player;
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.IsPlayer), MethodType.Getter)]
internal static class NativeCorruptedPlayerParticipantPatch
{
    private static void Postfix(Creature __instance, ref bool __result)
    {
        if (NativeCorruptedPlayer.TryGet(__instance, out _))
            __result = false;
    }
}

[HarmonyPatch(typeof(CombatState), nameof(CombatState.IterateHookListeners))]
internal static class NativeCorruptedPlayerMonsterHooksPatch
{
    private static void Postfix(CombatState __instance, ref IEnumerable<AbstractModel> __result) =>
        __result = __result.Where(NativeCorruptedPlayer.IncludeHook)
            .Concat(NativeCorruptedPlayer.In(__instance).Select(actor => actor.Body.Monster!));
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageGiven))]
internal static class NativeCorruptedPlayerDamageMetricPatch
{
    private static void Prefix(Creature? dealer, DamageResult results)
    {
        if (NativeCorruptedPlayer.TryGet(results.Receiver, out _) &&
            dealer != null && NativeCombatCallSites.IsPartyPlayer(dealer) &&
            NativeCombatCallSites.CreatureOwner(dealer) is { } player)
            player.ExtraFields.DamageDealt += results.UnblockedDamage;
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatEnd))]
internal static class NativeCorruptedPlayerCombatCleanupPatch
{
    private static void Postfix(ref Task __result) => __result = Cleanup(__result);
    private static async Task Cleanup(Task original)
    {
        await original;
        NativeCorruptedPlayer.CleanupAll();
    }

    [HarmonyPatch(typeof(CombatManager), nameof(CombatManager.Reset))]
    internal static class NativeCorruptedPlayerResetPatch
    {
        private static void Prefix() => NativeCorruptedPlayer.CleanupAll();
    }

    [HarmonyPatch(typeof(PlayerCmd), nameof(PlayerCmd.EndTurn))]
    internal static class NativeCorruptedPlayerForcedEndPatch
    {
        private static bool Prefix(Player player)
        {
            if (!NativeCorruptedPlayer.TryGet(player, out var actor))
                return true;
            actor.RequestEndTurn();
            return false;
        }
    }
}
