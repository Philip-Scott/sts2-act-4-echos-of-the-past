using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;
using TheArchitect.TheArchitectCode.Enchantments;
using TheArchitect.TheArchitectCode.Monsters;
using TheArchitect.TheArchitectCode.Powers;
using TheArchitect.TheArchitectCode.UI;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class WatcherStanceVisualPlaytest
{
    internal static void EnchantFixture(Player[] players)
    {
        Require(NativeDemoSafety.Enabled, "fixture is disposable");
        foreach (var player in players)
        {
            var skills = player.Deck.Cards.Where(card => card.Type == CardType.Skill).Take(2).ToArray();
            skills[0].EnchantInternal(ModelDb.GetById<EnchantmentModel>(new ModelId("ENCHANTMENT", "THEARCHITECT-CALM")).ToMutable(), 1);
            skills[1].EnchantInternal(ModelDb.GetById<EnchantmentModel>(new ModelId("ENCHANTMENT", "THEARCHITECT-WRATH")).ToMutable(), 1);
        }
    }

    internal static async Task Run(NGame game, Player[] players, NativeCorruptedPlayer[] actors)
    {
        Require(NativeDemoSafety.Enabled && !NativeDemoSafety.SharedVisible, "private-display native probe");
        var creatures = players.Select(player => player.Creature).Concat(actors.Select(actor => actor.Body)).ToArray();
        var nodes = creatures.Select(creature => creature.GetCreatureNode()!).ToArray();
        foreach (var player in players.Concat(actors.Select(actor => actor.Player))
                     .Where(player => player.Character is Necrobinder))
            await OstyCmd.Summon(new ThrowingPlayerChoiceContext(), player, 5, null);
        var pets = players.Concat(actors.Select(actor => actor.Player)).Select(player => player.Osty)
            .Where(pet => pet != null).Select(pet => pet!).ToArray();
        Require(pets.Length == 2, "fixture summons a human and corrupted Osty for isolation checks");
        var native = nodes.Concat(pets.Select(pet => pet.GetCreatureNode()!))
            .SelectMany(node => Descendants(node.Visuals).OfType<CanvasItem>())
            .Select(item => (Item: item, Parent: item.GetParent(), item.Material, item.UseParentMaterial)).ToArray();
        var echoes = actors.Select(actor => actor.Body.GetCreatureNode()!.Visuals
            .GetNode<CorruptedPlayerCorruption>("BoundEcho")).ToArray();
        Require(creatures.All(creature => Effect(creature) == null), "no stance creates no effect");
        await NativeDemoPlaytest.Capture("stance-none");

        await Play<Calm>(players[0]);
        var calm = Effect(players[0].Creature)!;
        Require(calm.Power is CalmStancePower, "local player's enchanted card creates Calm on its own node");
        Require(calm.GetIndex() == 0 && calm.GetNode<Polygon2D>("StanceBack").ZIndex == 0 &&
            calm.GetNode<Polygon2D>("StanceFront").ZIndex == 1,
            "aura draws behind the character, not behind the room background");
        Require(creatures.Skip(1).All(creature => Effect(creature) == null),
            "local stance never decorates remote players or Corrupted Players");
        await AnimationFrames("calm", calm);
        await Play<Calm>(players[0]);
        Require(ReferenceEquals(Effect(players[0].Creature), calm), "same-stance reentry retains one running effect");
        WatcherStanceVfx.Attach(calm.Power);
        Require(ReferenceEquals(Effect(players[0].Creature), calm), "repeated attach is idempotent");

        var builds = calm.GeometryBuildCount;
        var started = calm.AnimationTime;
        for (var frame = 0; frame < 12; frame++)
            await game.AwaitProcessFrame();
        Require(calm.AnimationTime > started && calm.GeometryBuildCount == builds,
            "animation advances without rebuilding quad geometry");
        var humanNode = nodes[0];
        var position = humanNode.Position;
        humanNode.Position += new Vector2(37, -11);
        await game.AwaitProcessFrame();
        Require(calm.GlobalPosition.IsEqualApprox(humanNode.Visuals.GlobalPosition) &&
            calm.GeometryBuildCount == builds, "effect follows owner movement without geometry rebuilds");
        humanNode.Position = position;
        var scale = humanNode.Visuals.Scale;
        humanNode.Visuals.Scale *= 0.8f;
        await game.AwaitProcessFrame();
        Require(calm.GlobalScale.IsEqualApprox(humanNode.Visuals.GlobalScale),
            "effect inherits native character scaling");
        humanNode.Visuals.Scale = scale;

        var oldPower = calm.Power;
        await Play<Wrath>(players[0]);
        Require(!GodotObject.IsInstanceValid(calm) || !calm.IsInsideTree(), "Calm detaches immediately on switching");
        var wrath = Effect(players[0].Creature)!;
        Require(wrath.Power is WrathStancePower, "Wrath replaces rather than layers over Calm");
        await oldPower.AfterCombatEnd(null!);
        Require(ReferenceEquals(Effect(players[0].Creature), wrath), "stale Calm cleanup cannot remove new Wrath");
        await AnimationFrames("wrath", wrath);
        await Play<Wrath>(players[0]);
        Require(ReferenceEquals(Effect(players[0].Creature), wrath), "Wrath reentry does not stack or restart");

        await Play<Calm>(players[1]);
        foreach (var actor in actors)
        {
            Require(actor.Cards.Any(card => card.Enchantment is Calm) &&
                actor.Cards.Any(card => card.Enchantment is Wrath), "snapshot restores both enchanted native cards");
            actor.AssertIdentity();
        }
        await Play<Calm>(actors[0].Player);
        await Play<Wrath>(actors[1].Player);
        Require(Effect(players[1].Creature)?.Power is CalmStancePower &&
            Effect(players[2].Creature) == null &&
            Effect(actors[0].Body)?.Power is CalmStancePower &&
            Effect(actors[1].Body)?.Power is WrathStancePower &&
            Effect(actors[2].Body) == null, "remote and saved-enemy stances use exact creature identities");
        await Task.Delay(600);
        await NativeDemoPlaytest.Capture("stance-party-owners");
        await Play<Calm>(players[2]);
        await Play<Wrath>(actors[2].Player);
        Require(pets.All(pet => Effect(pet) == null), "human and corrupted Osty are never tinted or decorated");
        Require(native.All(state => GodotObject.IsInstanceValid(state.Item) &&
            state.Item.GetParent() == state.Parent && state.Item.Material == state.Material &&
            state.Item.UseParentMaterial == state.UseParentMaterial), "native art, body hierarchy and material assignments stay intact");
        Require(echoes.All(effect => GodotObject.IsInstanceValid(effect) && effect.IsInsideTree()),
            "existing Bound Echo controllers remain attached and animating");
        Require(nodes.All(node => node.Visuals.GetChildren().OfType<WatcherStanceVfx>().Count() == 1),
            "each of six active owners has exactly one stance controller");
        await NativeDemoPlaytest.Capture("stance-all-owners");

        await PowerCmd.Remove(wrath.Power);
        Require(Effect(players[0].Creature) == null && Effect(players[1].Creature) != null,
            "native explicit removal cleans only the removed owner");
        players[1].Creature.RemoveAllPowersInternalExcept();
        Require(Effect(players[1].Creature) == null, "engine power reset removes effect and subscription");
        await NativeDemoPlaytest.Capture("stance-removed");
        var tornDown = Effect(players[2].Creature)!;
        var tornDownPower = tornDown.Power;
        tornDown.QueueFree();
        await game.AwaitProcessFrame();
        await game.AwaitProcessFrame();
        await PowerCmd.Remove(tornDownPower);
        Require(!GodotObject.IsInstanceValid(tornDown), "view teardown unsubscribes before subsequent model removal");
        await Play<Calm>(players[2]);
        var dyingEffect = Effect(actors[0].Body)!;
        await CreatureCmd.Kill(actors[0].Body, true);
        await game.AwaitProcessFrame();
        Require(!GodotObject.IsInstanceValid(dyingEffect) || !dyingEffect.IsInsideTree(),
            "native owner death removes its stance effect");
        Require(Effect(players[2].Creature) != null && Effect(actors[1].Body) != null,
            "one owner's death preserves unrelated living stances");
        await NativeDemoPlaytest.Capture("stance-death");

        await Play<Wrath>(players[0]);
        var surviving = Effect(players[0].Creature)!;
        foreach (var actor in actors.Skip(1))
            await CreatureCmd.Kill(actor.Body, true);
        var combat = players[0].Creature.CombatState!;
        await WaitFor(() => combat.Enemies.Any(creature => creature.Monster is ArchitectBoss));
        await CreatureCmd.Kill(combat.Enemies.Single(creature => creature.Monster is ArchitectBoss), true);
        await CombatManager.Instance.CheckWinCondition();
        await WaitFor(() => !CombatManager.Instance.IsInProgress);
        await WaitFor(() => !GodotObject.IsInstanceValid(surviving) || !surviving.IsInsideTree());
        Require(players.All(player => Effect(player.Creature) == null), "real combat-end lifecycle removes surviving stance VFX");
        await NativeDemoPlaytest.Capture("stance-combat-end");
        await game.ReturnToMainMenu();
        await game.AwaitProcessFrame();
        Require(!Descendants(game).OfType<WatcherStanceVfx>().Any(), "scene teardown leaves no stance nodes");
        MainFile.Logger.Info("STANCE VFX PASSED: animated Calm/Wrath, local/remote/saved owners, idempotence, switch/removal/death/combat/scene cleanup, cached geometry, native materials and Bound Echo.");
    }

    private static async Task Play<T>(Player player) where T : EnchantmentModel
    {
        var card = player.PlayerCombatState!.AllPiles.SelectMany(pile => pile.Cards)
            .First(card => card.Enchantment is T);
        await CardCmd.AutoPlay(new ThrowingPlayerChoiceContext(), card, null);
    }

    private static WatcherStanceVfx? Effect(Creature creature) =>
        creature.GetCreatureNode()?.Visuals.GetNodeOrNull<WatcherStanceVfx>("WatcherStanceVfx");

    private static async Task AnimationFrames(string label, WatcherStanceVfx effect)
    {
        for (var frame = 0; frame < 3; frame++)
        {
            await Task.Delay(450);
            await NativeDemoPlaytest.Capture($"stance-{label}-{frame}");
            MainFile.Logger.Info($"STANCE FRAME {label}-{frame}: t={effect.AnimationTime:F3}");
        }
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        yield return node;
        foreach (var child in node.GetChildren())
            foreach (var descendant in Descendants(child))
                yield return descendant;
    }

    private static async Task WaitFor(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!ready())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Stance VFX probe timed out.");
            await NGame.Instance!.AwaitProcessFrame();
        }
    }

    private static void Require(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException("STANCE VFX ASSERTION FAILED: " + description);
        MainFile.Logger.Info("STANCE VFX ASSERT: " + description);
    }
}
