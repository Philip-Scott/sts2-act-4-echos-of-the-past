using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Saves;

namespace AnimatedPlayerHands;

/// <summary>
/// Native combat only animates the local player's hand. This replays the same motions on a remote
/// player's compact hand: draws travel from its draw pile into their slots like NHandCardHolder,
/// the rest of the hand slides, and cards leaving the hand use the native NCardFlyVfx aimed at the
/// hand's own pile badges or burn in place with NCardExhaustQuickVfx's flames.
/// Cards that are played leave silently: native combat already shows remote plays above the player.
/// </summary>
internal sealed class RemoteHandAnimator : IDisposable
{
    private static readonly AccessTools.FieldRef<NCardExhaustQuickVfx, NParticlesContainer> ExhaustBurst =
        AccessTools.FieldRefAccess<NCardExhaustQuickVfx, NParticlesContainer>("_particlesContainer");
    private static readonly AccessTools.FieldRef<NCardExhaustQuickVfx, float> ExhaustAnticipation =
        AccessTools.FieldRefAccess<NCardExhaustQuickVfx, float>("_anticipationDuration");
    private static readonly AccessTools.FieldRef<NCardFlyVfx, Vector2> FlyEnd =
        AccessTools.FieldRefAccess<NCardFlyVfx, Vector2>("_endPos");
    private static readonly AccessTools.FieldRef<NCardFlyVfx, float> FlyArcDir =
        AccessTools.FieldRefAccess<NCardFlyVfx, float>("_arcDir");

    private sealed class Flight(NCard node)
    {
        public NCard Node { get; } = node;
        public Control? Face;
        public bool Pending;
        public double StartAt;
        public PileType? Source;
    }

    private readonly PlayerCombatState _state;
    private readonly string _trailPath;
    private readonly Vector2 _faceCenter;
    private readonly float _miniatureScale;
    private readonly Func<PileType, Vector2?> _pileCenter;
    private readonly Func<CardModel, NCard?> _takeFromHand;
    private readonly Action _handChanged;
    private readonly Dictionary<CardModel, Flight> _flights = [];
    private readonly Dictionary<CardModel, PileType?> _arrivals = [];
    private readonly Dictionary<CardModel, NCard> _departures = [];
    private readonly List<(CardPile Pile, Action<CardModel> Added, Action<CardModel> Removed)> _subscriptions = [];
    private (CardModel Card, PileType Pile)? _lastRemoved;
    private Control? _layer;
    private double _clock;
    private double _nextArrival;

    internal RemoteHandAnimator(Player player, Vector2 faceCenter, float miniatureScale,
        Func<PileType, Vector2?> pileCenter, Func<CardModel, NCard?> takeFromHand, Action handChanged)
    {
        _state = player.PlayerCombatState!;
        _trailPath = player.Character.TrailPath;
        _faceCenter = faceCenter;
        _miniatureScale = miniatureScale;
        _pileCenter = pileCenter;
        _takeFromHand = takeFromHand;
        _handChanged = handChanged;
        Subscribe(_state.Hand, OnAddedToHand, OnRemovedFromHand);
        foreach (var pile in new[] { _state.DrawPile, _state.DiscardPile, _state.ExhaustPile })
            Subscribe(pile, card => OnAddedToPile(card, pile.Type), card => _lastRemoved = (card, pile.Type));
    }

    public bool Uses(Player player) => player.PlayerCombatState == _state;

    internal static bool Animates => SaveManager.Instance?.PrefsSave is not { } prefs ||
        HandMotion.Animates(prefs.FastMode);

    internal int InFlight => _flights.Count;

    // Cards that have emerged from one of the hand's pile badges.
    internal int PileArrivals { get; private set; }

    private static double Stagger => SaveManager.Instance?.PrefsSave is { } prefs
        ? HandMotion.DrawStagger(prefs.FastMode) : 0.25;

    private void Subscribe(CardPile pile, Action<CardModel> added, Action<CardModel> removed)
    {
        // These run inside the game's card commands; a display failure must not interrupt them.
        added = Faults.Guard($"{pile.Type} pile event", added);
        removed = Faults.Guard($"{pile.Type} pile event", removed);
        pile.CardAdded += added;
        pile.CardRemoved += removed;
        _subscriptions.Add((pile, added, removed));
    }

    private void OnRemovedFromHand(CardModel card)
    {
        _arrivals.Remove(card);
        if (_takeFromHand(card) is { } node)
        {
            _flights.Remove(card);
            if (Animates && ToLayer(node))
                _departures[card] = node;
            else
                CardNodes.Return(node);
        }
        _handChanged();
    }

    private void OnAddedToHand(CardModel card)
    {
        if (_departures.Remove(card, out var node))
            _flights[card] = new Flight(node);
        else
            _arrivals[card] = _lastRemoved?.Card == card ? _lastRemoved.Value.Pile : null;
        _lastRemoved = null;
        _handChanged();
    }

    private void OnAddedToPile(CardModel card, PileType pile)
    {
        if (!_departures.Remove(card, out var node))
            return;
        if (pile == PileType.Exhaust)
            Burn(node);
        else
            FlyToPile(node, pile);
    }

    /// <summary>Keeps a hand card's node on screen while the hand strip is rebuilt.</summary>
    public bool Release(CardModel card, NCard node)
    {
        if (_flights.TryGetValue(card, out var flight))
        {
            flight.Face = null;
            return true;
        }
        if (!Animates || !ToLayer(node))
            return false;
        _flights[card] = new Flight(node);
        return true;
    }

    /// <summary>Returns a released node, which then travels from its current position to the new slot.</summary>
    public NCard? Claim(CardModel card, Control face)
    {
        if (!_flights.TryGetValue(card, out var flight) || !GodotObject.IsInstanceValid(flight.Node))
        {
            _flights.Remove(card);
            return null;
        }
        flight.Face = face;
        return flight.Node;
    }

    /// <summary>Starts a newly displayed card's draw animation if it just entered the hand.</summary>
    public void Arrive(CardModel card, NCard node, Control face)
    {
        if (!_arrivals.Remove(card, out var source) || !Animates || !ToLayer(node))
            return;
        node.Visible = false;
        _flights[card] = new Flight(node)
        {
            Face = face,
            Pending = true,
            Source = source,
            StartAt = HandMotion.ScheduleArrival(_clock, ref _nextArrival, Stagger)
        };
    }

    public void DropUnclaimed()
    {
        foreach (var (card, flight) in _flights.Where(pair => pair.Value.Face == null).ToArray())
        {
            _flights.Remove(card);
            Free(flight.Node);
        }
        _arrivals.Clear();
    }

    public void Process(double delta, bool visible)
    {
        _clock += delta;
        if (GodotObject.IsInstanceValid(_layer))
            _layer!.Visible = visible;
        // Departures resolve synchronously when the card lands in discard, draw or exhaust.
        // Anything left (played or removed from combat) already has native visuals or none.
        foreach (var node in _departures.Values)
            Free(node);
        _departures.Clear();
        foreach (var (card, flight) in _flights.ToArray())
        {
            if (flight.Face == null)
                continue;
            if (!GodotObject.IsInstanceValid(flight.Face) || !GodotObject.IsInstanceValid(flight.Node))
            {
                _flights.Remove(card);
                Free(flight.Node);
                continue;
            }
            var faceTransform = flight.Face.GetGlobalTransform();
            var target = faceTransform * _faceCenter;
            var layerScale = _layer!.GetGlobalTransform().Scale;
            var targetScale = faceTransform.Scale * _miniatureScale / layerScale;
            if (flight.Pending)
            {
                if (_clock < flight.StartAt)
                    continue;
                flight.Pending = false;
                flight.Node.Visible = true;
                // The draw pile icon sits just under the hand's health bar, so drawn cards emerge at icon size and grow
                // on the way up; generated cards grow in place, as natively.
                var origin = flight.Source is { } source ? _pileCenter(source) : null;
                if (origin.HasValue)
                    PileArrivals++;
                flight.Node.GlobalPosition = origin ?? target;
                flight.Node.Scale = origin.HasValue ? targetScale * HandMotion.PileEmergeScale : Vector2.Zero;
                flight.Node.Rotation = origin.HasValue ? HandMotion.PileEmergeTilt : 0f;
            }
            var step = (float)delta;
            flight.Node.GlobalPosition = HandMotion.StepPosition(flight.Node.GlobalPosition, target, step);
            flight.Node.Scale = HandMotion.StepScale(flight.Node.Scale, targetScale, step);
            flight.Node.Rotation = Mathf.LerpAngle(flight.Node.Rotation, 0f, Math.Min(1f, step * 10f));
            if (flight.Node.GlobalPosition != target || flight.Node.Scale != targetScale)
                continue;
            _flights.Remove(card);
            flight.Node.Reparent(flight.Face, keepGlobalTransform: false);
            flight.Node.Position = _faceCenter;
            flight.Node.Scale = Vector2.One * _miniatureScale;
            flight.Node.Rotation = 0f;
        }
    }

    private void FlyToPile(NCard node, PileType pile)
    {
        // The VFX returns the card to the pool when it lands.
        CardNodes.Restore(node);
        if (_pileCenter(pile) is not { } end || NCardFlyVfx.Create(node, pile, false, _trailPath) is not { } vfx)
        {
            Free(node);
            return;
        }
        node.Visible = true;
        FlyEnd(vfx) = end;
        _layer!.AddChild(vfx);
        // _Ready picks a full-screen arc; the first frame has not run yet, so scale it to the HUD.
        FlyArcDir(vfx) = HandMotion.FlyArc(node.GlobalPosition, end, Rng.Chaotic.NextFloat(0f, 80f));
    }

    private static void Burn(NCard node)
    {
        // The burn outlives this hand if its owner dies mid-animation, so it joins the combat UI directly.
        // Move before creating the VFX: it lives inside the card, and reparenting would end it and free the card.
        if (NCombatRoom.Instance?.Ui is not { } ui)
        {
            Free(node);
            return;
        }
        node.Visible = true;
        MoveKeepingTransform(node, ui);
        CardNodes.Restore(node);
        if (NCardExhaustQuickVfx.Create(node) is not { } vfx)
        {
            Free(node);
            return;
        }
        // The closing smoke burst is sized for a full hand card and would swallow the HUD; keep
        // the native flames and let the miniature char to exhaust gray before the VFX frees it.
        ExhaustBurst(vfx).Visible = false;
        var duration = ExhaustAnticipation(vfx);
        var tween = node.CreateTween().SetParallel();
        tween.TweenProperty(node, "modulate", StsColors.exhaustGray, duration);
        tween.TweenProperty(node, "scale", node.Scale * 0.85f, duration);
        // The VFX frees the card early when combat ends; a paused tween must not resume on a recycled card.
        node.Connect(Node.SignalName.TreeExiting, Callable.From(tween.Kill), (uint)GodotObject.ConnectFlags.OneShot);
        NDebugAudioManager.Instance?.Play("card_exhaust.mp3");
        TaskHelper.RunSafely(vfx.PlayAnimation());
    }

    private bool ToLayer(NCard node)
    {
        if (!GodotObject.IsInstanceValid(_layer))
        {
            if (NCombatRoom.Instance?.Ui is not { } ui)
                return false;
            _layer = new Control { Name = "AnimatedPlayerHandCards", MouseFilter = Control.MouseFilterEnum.Ignore };
            ui.AddChild(_layer);
        }
        if (node.GetParent() == _layer)
            return true;
        if (node.GetParent() == null)
            _layer!.AddChild(node);
        else
            MoveKeepingTransform(node, _layer!);
        return true;
    }

    // Control.Reparent keeps only the global position; the HUD is scaled, so carry scale and rotation too.
    private static void MoveKeepingTransform(Control node, Control parent)
    {
        var global = node.GetGlobalTransform();
        node.Reparent(parent, keepGlobalTransform: false);
        var local = parent.GetGlobalTransform().AffineInverse() * global;
        node.Rotation = local.Rotation;
        node.Scale = local.Scale;
        node.Position = local.Origin - node.PivotOffset + local.BasisXform(node.PivotOffset);
    }

    private static void Free(NCard node) => CardNodes.Return(node);

    public void Dispose()
    {
        foreach (var (pile, added, removed) in _subscriptions)
        {
            pile.CardAdded -= added;
            pile.CardRemoved -= removed;
        }
        _subscriptions.Clear();
        foreach (var flight in _flights.Values)
            Free(flight.Node);
        foreach (var node in _departures.Values)
            Free(node);
        _flights.Clear();
        _departures.Clear();
        _arrivals.Clear();
        if (GodotObject.IsInstanceValid(_layer))
        {
            foreach (var node in _layer!.GetChildren().OfType<NCard>())
                Free(node);
            // Disposal can happen while the combat UI frees its children, so never detach synchronously.
            _layer.Hide();
            _layer.QueueFree();
        }
        _layer = null;
    }
}
