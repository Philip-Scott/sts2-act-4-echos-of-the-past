using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.addons.mega_text;
using TheArchitect.TheArchitectCode.UI;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class NativeEnemyResourcePlaytest
{
    internal static async Task Verify(NGame game, IReadOnlyList<Player> actors,
        IReadOnlyList<CorruptedPlayerTelegraph> panels)
    {
        if (!NativeDemoSafety.Enabled)
            throw new InvalidOperationException("Enemy resource probes require isolated native playtests.");
        var counters = panels.Select(panel => panel.FindChildren("*", recursive: true, owned: false)
            .OfType<NStarCounter>().Single()).ToArray();
        var initial = actors.Select(player => player.PlayerCombatState!.Stars).ToArray();
        try
        {
            for (var index = 0; index < actors.Count; index++)
                actors[index].PlayerCombatState!.GainStars(7 + index);
            await game.ToSignal(game.GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
            for (var index = 0; index < actors.Count; index++)
            {
                var counter = counters[index];
                var expected = initial[index] + 7 + index;
                if (!counter.IsVisibleInTree() ||
                    counter.GetNode<MegaRichTextLabel>("%CountLabel").Text != $"[center]{expected}[/center]")
                    throw new InvalidOperationException($"Enemy {index} must display its own native star count.");
            }
            await NativeDemoPlaytest.Capture($"party-{actors.Count}-stars");
            actors[0].PlayerCombatState!.LoseStars(actors[0].PlayerCombatState!.Stars);
            if (!counters[0].IsVisibleInTree() ||
                counters[0].GetNode<MegaRichTextLabel>("%CountLabel").Text != "[center]0[/center]")
                throw new InvalidOperationException("Native stars stay visible at zero after spending.");
            for (var index = 1; index < actors.Count; index++)
                if (actors[index].PlayerCombatState!.Stars != initial[index] + 7 + index)
                    throw new InvalidOperationException("Spending one actor's stars must not change another actor.");
        }
        finally
        {
            for (var index = 0; index < actors.Count; index++)
            {
                var state = actors[index].PlayerCombatState!;
                state.LoseStars(state.Stars);
                state.GainStars(initial[index]);
            }
        }

    }

    internal static async Task VerifyFullRelicBar(NGame game, Player human,
        IReadOnlyList<CorruptedPlayerTelegraph> panels)
    {
        if (!NativeDemoSafety.Enabled)
            throw new InvalidOperationException("Relic-bar probes require isolated native playtests.");
        var inventory = NRun.Instance!.GlobalUi.RelicInventory;
        var relics = Enumerable.Range(0, 60).Select(_ => ModelDb.Relic<BurningBlood>().ToMutable()).ToArray();
        var focus = game.GetViewport().GuiGetFocusOwner();
        try
        {
            foreach (var relic in relics)
                human.AddRelicInternal(relic, silent: false);
            await game.ToSignal(game.GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
            if (inventory.GetLineCount() < 2)
                throw new InvalidOperationException("The full-relic-bar probe must wrap the native inventory.");
            var height = inventory.GetBottomOfInventory().Y - inventory.GetDefaultPosition().Y;
            var bottom = (inventory.GetGlobalTransform() * new Vector2(0, height)).Y;
            var bounds = panels.Select(panel => panel.GetGlobalTransform() * new Rect2(Vector2.Zero, panel.Size))
                .ToArray();
            for (var index = 0; index < bounds.Length; index++)
            {
                if (bounds[index].Position.Y < bottom + 11f ||
                    !game.GetViewport().GetVisibleRect().Encloses(bounds[index]) ||
                    bounds.Skip(index + 1).Any(other => bounds[index].Intersects(other)))
                    throw new InvalidOperationException("Wrapped relics must not hide or overlap any enemy hand.");
            }
            var face = panels[0].FindChildren("CorruptedPlayerCard", recursive: true, owned: false)
                .OfType<Button>().First();
            face.GrabFocus();
            await game.ToSignal(game.GetTree(), SceneTree.SignalName.ProcessFrame);
            await game.ToSignal(game.GetTree(), SceneTree.SignalName.ProcessFrame);
            var preview = game.HoverTipsContainer.GetChildren().OfType<Control>()
                .Single(node => node.Name == "CorruptedPlayerCardPreview" && node.IsVisibleInTree());
            if (!game.GetViewport().GetVisibleRect().Encloses(preview.GetGlobalRect()))
                throw new InvalidOperationException("Full-size enemy cards must remain inspectable with a full relic bar.");
            await NativeDemoPlaytest.Capture($"party-{panels.Count}-full-relics-preview");
            face.ReleaseFocus();
        }
        finally
        {
            foreach (var relic in relics.Where(human.Relics.Contains))
                human.RemoveRelicInternal(relic, silent: false);
            if (GodotObject.IsInstanceValid(focus))
                focus!.GrabFocus();
        }
        await game.ToSignal(game.GetTree(), SceneTree.SignalName.ProcessFrame);
        await game.ToSignal(game.GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
