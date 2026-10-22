using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace TheArchitect.TheArchitectCode.UI;

/// <summary>
/// NCard nodes come from the game's NodePool and may later be reused by the local player's hand,
/// so every card created for a mod display is borrowed and returned without leaving changes behind.
/// </summary>
internal static class PooledCards
{
    // Mouse and focus belong to the control that hosts a card. The recursive behaviors leave the
    // card's own controls untouched, so the pool's next user gets them unchanged.
    internal static void Borrow(NCard node)
    {
        node.MouseBehaviorRecursive = Control.MouseBehaviorRecursiveEnum.Disabled;
        node.FocusBehaviorRecursive = Control.FocusBehaviorRecursiveEnum.Disabled;
    }

    // Before a native VFX takes ownership, since it returns the card to the pool itself.
    internal static void Restore(NCard node)
    {
        node.MouseBehaviorRecursive = Control.MouseBehaviorRecursiveEnum.Inherited;
        node.FocusBehaviorRecursive = Control.FocusBehaviorRecursiveEnum.Inherited;
    }

    // The pool takes cards back at the end of the frame, and teardown can reach one node twice:
    // a sliding card is both the animator's flight and the hand's miniature.
    private static readonly HashSet<NCard> Returning = [];

    // QueueFree would destroy the node while the pool still counts it as in use.
    internal static void Return(NCard? node)
    {
        if (node == null || !GodotObject.IsInstanceValid(node) || node.IsQueuedForDeletion() || !Returning.Add(node))
            return;
        if (Returning.Count == 1)
            Callable.From(Returning.Clear).CallDeferred();
        Restore(node);
        node.QueueFreeSafely();
    }
}
