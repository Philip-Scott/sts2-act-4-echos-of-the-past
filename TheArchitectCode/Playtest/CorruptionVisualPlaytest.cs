using System.IO;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;
using TheArchitect.TheArchitectCode.Monsters;
using TheArchitect.TheArchitectCode.Persistence;
using TheArchitect.TheArchitectCode.UI;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class CorruptionVisualPlaytest
{
    private sealed record NativeMaterial(CanvasItem Node, Material? Material, bool UseParentMaterial);
    private sealed record NativeBody(Node2D Body, Node? Owner, NativeMaterial[] Materials);
    private static readonly ConditionalWeakTable<NCreatureVisuals, NativeBody[]> NativeBodies = new();
    internal static bool RecordingNativeVisuals { get; private set; }

    internal static async Task Run(NGame game)
    {
        Require(NativeDemoSafety.Enabled, "visual probes require the disposable Steam-off launcher");
        game.GetWindow().Title = "The Architect - ISOLATED BOUND ECHO VISUALS (automatic)";
        CharacterModel[] characters = [ModelDb.Character<Ironclad>(), ModelDb.Character<Silent>(),
            ModelDb.Character<Regent>(), ModelDb.Character<Necrobinder>(), ModelDb.Character<Defect>()];
        RecordingNativeVisuals = true;
        try
        {
            foreach (var character in characters)
                await ExerciseCharacter(game, character);
        }
        finally
        {
            RecordingNativeVisuals = false;
        }
        MainFile.Logger.Info("CORRUPTION VISUALS PASSED: five production characters; animation, alpha, scale, UI/orb/pet isolation, death and room cleanup. Screenshots require visual review.");
        game.GetTree().Quit();
    }

    private static async Task ExerciseCharacter(NGame game, CharacterModel character)
    {
        var label = character.Id.Entry.ToLowerInvariant();
        MainFile.Logger.Info($"CORRUPTION CHARACTER BEGIN: {label}");
        var run = await game.StartNewSingleplayerRun(character, true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()],
            [], "ARCHITECT-BOUND-ECHO-" + character.Id.Entry, GameMode.Standard);
        var human = run.Players.Single();
        var destination = ProjectSettings.GlobalizePath(
            SaveManager.Instance.GetProfileScopedPath("TheArchitect/corrupted_player_snapshot.json"));
        Require(RunManager.Instance.ShouldSave &&
            Path.GetFullPath(destination).StartsWith(NativeDemoSafety.RuntimePath + "/", StringComparison.Ordinal),
            $"{label}: snapshot and run saves are disposable");
        var snapshot = CorruptedPlayerSnapshot.Capture(human);
        Require(snapshot.ResolveCharacter() == character &&
            snapshot.Deck.Length == character.StartingDeck.Count(), $"{label}: fixture is the actual new-run starting deck");
        Require(CorruptedPlayerStore.Commit("bound-echo-" + label, "VisualProbe", snapshot) != null,
            $"{label}: generated snapshot committed without captured user data");
        await Task.Delay(1500);
        var entry = new DevConsole(shouldAllowDebugCommands: true).ProcessCommand("architect");
        if (!entry.success || entry.task == null)
            throw new InvalidOperationException($"Corruption encounter entry failed: {entry.msg}");
        await entry.task;
        Require(ArchitectRun.Get(run).EntrySnapshot?.Snapshot?.ContentHash == snapshot.ContentHash,
            $"{label}: production Architect entry loaded generated snapshot");
        await WaitFor(() => NMapScreen.Instance?.IsOpen == true, label + " map");
        await RunManager.Instance.EnterMapCoord(run.Map.StartingMapPoint.coord);
        await RunManager.Instance.EnterMapCoord(run.Map.StartingMapPoint.Children.Single().coord);
        await RunManager.Instance.EnterMapCoord(run.Map.BossMapPoint.coord);
        await NativeDemoPlaytest.PlayerTurn(human, 1);
        var actor = RequireNativeActor(human.Creature.CombatState!.Enemies
            .Single(c => c.Monster is CorruptedPlayer).Monster as CorruptedPlayer);
        var node = actor.Body.GetCreatureNode()!;
        var humanNode = human.Creature.GetCreatureNode()!;
        await Task.Delay(2000);
        AssertIsolation(node, humanNode, label);
        actor.AssertIdentity();
        if (character is Ironclad)
            await AssertCompositorModulation(game, Find<CanvasGroup>(node.Visuals, "BoundEchoBody"));

        var normalMaterial = humanNode.Body.Material;
        var normalModulate = humanNode.Body.Modulate;
        var normalScale = humanNode.Visuals.Scale;
        await Capture(label, "idle");
        PlayerCmd.EndTurn(human, false);
        await NativeDemoPlaytest.PlayerTurn(human, 2);
        Require(actor.CompletedTurns == 1, $"{label}: a real native Corrupted Player turn completed with Bound Echo active");
        await Task.Delay(2000);
        await Capture(label, "native-turn");
        foreach (var (trigger, animation) in new[] { ("Attack", "attack"), ("Cast", "cast"), ("Hit", "hurt") })
        {
            node.SetAnimationTrigger(trigger);
            humanNode.SetAnimationTrigger(trigger);
            await game.AwaitProcessFrame();
            using (var track = node.SpineAnimation.GetCurrentTrack())
            {
                Require(track != null && track.GetAnimationName().Contains(animation, StringComparison.OrdinalIgnoreCase),
                    $"{label}: native {trigger} animation is active");
            }
            await Task.Delay(80);
            await Capture(label, trigger.ToLowerInvariant());
            await Task.Delay((int)(Math.Min(node.GetCurrentAnimationTimeRemaining(), 5f) * 1000) + 100);
            node.SetAnimationTrigger("Idle");
            humanNode.SetAnimationTrigger("Idle");
        }

        var context = new ThrowingPlayerChoiceContext();
        if (character is Defect)
        {
            await OrbCmd.Channel<LightningOrb>(context, actor.Player);
            Require(node.OrbManager!.GetNode<Control>("%Orbs").GetChildren().OfType<NOrb>()
                .Any(orb => orb.Model is LightningOrb), $"{label}: a real native lightning orb is rendered");
            await Capture(label, "orbs");
        }
        if (character is Necrobinder)
        {
            await OstyCmd.Summon(context, actor.Player, 5, null);
            await Task.Delay(900);
            var pet = actor.Player.Osty?.GetCreatureNode()
                ?? throw new InvalidOperationException("Visual probe failed to summon the native enemy pet.");
            Require(pet.GetParent() == node.GetParent() && !node.Visuals.IsAncestorOf(pet),
                $"{label}: real enemy Osty remains outside the Corrupted Player's body group");
            AssertCorruptedPet(pet);
            Require(human.Osty?.GetCreatureNode() is { } humanPet &&
                !Descendants(humanPet).Any(n => n.Name.ToString().StartsWith("BoundEcho", StringComparison.Ordinal)),
                $"{label}: the human's Osty has no corruption effect");
            Require(actor.Player.Osty!.Side == actor.Body.Side, $"{label}: summon remains an enemy-owned pet");
            await Capture(label, "pet");
            await OstyCmd.Summon(context, actor.Player, 15, null);
            await Task.Delay(900);
            AssertCorruptedPet(pet);
            await Capture(label, "pet-grown");
            await CreatureCmd.Damage(context, actor.Player.Osty!, actor.Player.Osty!.CurrentHp,
                ValueProp.Unblockable | ValueProp.Unpowered, human.Creature);
            await Task.Delay(500);
            Require(!GodotObject.IsInstanceValid(pet) || !Find<Node2D>(pet.Visuals, "BoundEchoFront").IsVisibleInTree(),
                $"{label}: defeated Osty's bindings disappear");
            await Capture(label, "pet-defeated");
            await OstyCmd.Summon(context, actor.Player, 8, null);
            await Task.Delay(900);
            var revived = actor.Player.Osty!.GetCreatureNode()!;
            AssertCorruptedPet(revived);
            Require(Find<Node2D>(revived.Visuals, "BoundEchoFront").IsVisibleInTree(),
                $"{label}: revived enemy Osty restores its corruption");
            await Capture(label, "pet-revived");
        }
        AssertIsolation(node, humanNode, label);

        // Native revive fades NCreatureVisuals, not its original Spine materials.
        var originalAlpha = node.Visuals.Modulate;
        var ui = new[] { node.GetNode<CanvasItem>("%HealthBar"), node.IntentContainer,
            Find<CanvasItem>(node, "CorruptedPlayerTelegraph"), node.OrbManager! };
        var uiColors = ui.Select(item => item.Modulate).ToArray();
        foreach (var alpha in new[] { 0.5f, 0f, 1f })
        {
            var tween = node.CreateTween();
            tween.TweenProperty(node.Visuals, "modulate:a", alpha, 0.25);
            await node.ToSignal(tween, Tween.SignalName.Finished);
            await Capture(label, "alpha-" + (int)(alpha * 100));
            Require(Mathf.IsEqualApprox(node.Visuals.Modulate.A, alpha) &&
                Mathf.IsEqualApprox(InheritedAlpha(Find<CanvasGroup>(node.Visuals, "BoundEchoBody")) *
                    Find<CanvasGroup>(node.Visuals, "BoundEchoBody").SelfModulate.A, alpha) &&
                Mathf.IsEqualApprox(InheritedAlpha(Find<Node2D>(node.Visuals, "BoundEchoBack")), alpha) &&
                Mathf.IsEqualApprox(InheritedAlpha(Find<Node2D>(node.Visuals, "BoundEchoFront")), alpha),
                $"{label}: body and independent bindings inherit native alpha={alpha}");
            Require(ui.Select(item => item.Modulate).SequenceEqual(uiColors),
                $"{label}: native alpha fade leaves health, intents, telegraph and orb UI unchanged");
        }
        node.Visuals.Modulate = originalAlpha;
        foreach (var scale in new[] { 0.75f, 1.25f, 1f })
        {
            node.ScaleTo(scale, 0.25);
            await Task.Delay(350);
            Require(node.Visuals.Scale.IsEqualApprox(Vector2.One * scale * node.Visuals.DefaultScale),
                $"{label}: native ScaleTo({scale}) completes with the corruption effect attached");
            AssertIsolation(node, humanNode, label);
            await Capture(label, "scale-" + (int)(scale * 100));
        }
        Require(humanNode.Body.Material == normalMaterial && humanNode.Body.Modulate == normalModulate &&
            humanNode.Visuals.Scale.IsEqualApprox(normalScale),
            $"{label}: normal human materials, alpha and scale remain unchanged");

        if (character is Ironclad)
        {
            await SaveManager.Instance.SaveRun(null);
            var saved = SaveManager.Instance.LoadRunSave().SaveData
                ?? throw new InvalidOperationException("Visual probe room save missing.");
            var previous = actor;
            var oldEffect = Find<Node>(node.Visuals, "BoundEcho");
            await game.ReturnToMainMenu();
            await WaitFor(() => !GodotObject.IsInstanceValid(oldEffect), "live room effect freed");
            Require(previous.Cleaned, $"{label}: live room transition cleans native actor and effect nodes");
            await Capture(label, "room-exit");
            run = RunState.FromSerializable(saved);
            await RunManager.Instance.SetUpSavedSingleplayer(run, saved);
            await game.LoadRun(run, saved.PreFinishedRoom);
            human = run.Players.Single();
            await NativeDemoPlaytest.PlayerTurn(human, 1);
            actor = RequireNativeActor(human.Creature.CombatState!.Enemies
                .Single(c => c.Monster is CorruptedPlayer).Monster as CorruptedPlayer);
            node = actor.Body.GetCreatureNode()!;
            humanNode = human.Creature.GetCreatureNode()!;
            AssertIsolation(node, humanNode, label + " reload");
            Require(actor != previous, $"{label}: reload constructed a fresh production Corrupted Player");
            await Capture(label, "reload");
        }

        var effect = Find<Node>(node.Visuals, "BoundEcho");
        var front = Find<Node2D>(node.Visuals, "BoundEchoFront");
        var back = Find<Node2D>(node.Visuals, "BoundEchoBack");
        var bodyGroup = Find<CanvasGroup>(node.Visuals, "BoundEchoBody");
        var orbs = node.OrbManager!;
        var death = CreatureCmd.Kill(actor.Body, true);
        await Task.Delay(100);
        await Capture(label, "death");
        await death;
        Require(actor.Cleaned && actor.Body.GetCreatureNode() == null,
            $"{label}: native death removes and cleans the Corrupted Player during boss handoff");
        Require(!GodotObject.IsInstanceValid(orbs) || !orbs.Visible,
            $"{label}: native orb UI is hidden or freed after death");
        await WaitFor(() => (!GodotObject.IsInstanceValid(front) || !front.IsVisibleInTree()) &&
            (!GodotObject.IsInstanceValid(back) || !back.IsVisibleInTree()),
            label + " bindings disappear after death");
        await Capture(label, "handoff");
        Require(human.Creature.CombatState!.Enemies.Any(c => c.Monster is ArchitectBoss),
            $"{label}: death used the production Architect boss handoff");
        await game.ReturnToMainMenu();
        await WaitFor(() => new Node[] { effect, front, back, bodyGroup }
            .All(n => !GodotObject.IsInstanceValid(n)), label + " room nodes freed");
        Require(!Descendants(game).Any(n => n.Name.ToString().StartsWith("BoundEcho", StringComparison.Ordinal)),
            $"{label}: no effect nodes survive room teardown");
        MainFile.Logger.Info($"CORRUPTION CHARACTER PASSED: {label}");
    }

    private static NativeCorruptedPlayer RequireNativeActor(CorruptedPlayer? monster) =>
        monster?.Native ?? throw new InvalidOperationException("Production Corrupted Player native actor missing.");

    private static async Task AssertCompositorModulation(NGame game, CanvasGroup bodyGroup)
    {
        var layer = new CanvasLayer { Layer = 100 };
        var probe = new Node2D { Position = new Vector2(40, 250), ZIndex = 4000 };
        var background = new ColorRect { Size = new Vector2(120, 40), Color = new Color(0.1f, 0.15f, 0.2f) };
        var faded = new Node2D();
        var material = (ShaderMaterial)bodyGroup.Material.Duplicate();
        material.SetShaderParameter("strength", 0f);
        var group = new CanvasGroup { Position = new Vector2(60, 0), Material = material };
        var original = new ColorRect { Size = new Vector2(40, 40), Color = new Color(0.8f, 0.4f, 0.2f) };
        var composed = new ColorRect { Size = original.Size, Color = original.Color };
        probe.AddChild(background);
        probe.AddChild(faded);
        faded.AddChild(original);
        faded.AddChild(group);
        group.AddChild(composed);
        layer.AddChild(probe);
        game.AddChild(layer);
        try
        {
            foreach (var tint in new[] { Colors.White, new Color(1, 1, 1, 0.5f), new Color(0.7f, 0.5f, 0.9f, 0.5f), Colors.Transparent })
            {
                faded.Modulate = tint;
                group.Visible = CorruptedPlayerCorruption.ApplyInheritedModulation(group);
                await game.AwaitProcessFrame();
                await game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = game.GetViewport().GetTexture().GetImage();
                var viewport = game.GetViewport();
                var pixelScale = new Vector2(image.GetWidth(), image.GetHeight()) / viewport.GetVisibleRect().Size;
                var a = (original.GetGlobalTransformWithCanvas() * new Vector2(20, 20)) * pixelScale;
                var b = (composed.GetGlobalTransformWithCanvas() * new Vector2(20, 20)) * pixelScale;
                var native = image.GetPixel((int)a.X, (int)a.Y);
                var actual = image.GetPixel((int)b.X, (int)b.Y);
                Require(Mathf.Abs(native.R - actual.R) < 0.015f &&
                    Mathf.Abs(native.G - actual.G) < 0.015f && Mathf.Abs(native.B - actual.B) < 0.015f,
                    $"compositor preserves inherited modulation exactly once: tint={tint}, native={native}, composed={actual}");
            }
        }
        finally
        {
            layer.QueueFree();
        }
    }

    private static void AssertCorruptedPet(NCreature pet)
    {
        var group = Find<CanvasGroup>(pet.Visuals, "BoundEchoBody");
        var front = Find<Node2D>(pet.Visuals, "BoundEchoFront");
        var back = Find<Node2D>(pet.Visuals, "BoundEchoBack");
        Require(pet.HasSpineAnimation && pet.Body.GetParent() == group && group.Material is ShaderMaterial &&
            front.GetParent() == group.GetParent() && back.GetParent() == group.GetParent() &&
            !group.IsAncestorOf(pet.Hitbox) && !group.IsAncestorOf(pet.GetNode("%HealthBar")) &&
            Descendants(pet).Count(n => n.Name == "BoundEcho") == 1,
            "enemy Osty retains native animation and its own body-only Bound Echo, without corrupting its health UI");
    }

    internal static void RememberNativeVisuals(NCreatureVisuals visuals)
    {
        var bodies = new[] { visuals.GetNode<Node2D>("%Visuals"),
            visuals.GetNodeOrNull<Node2D>("%PhobiaModeVisuals") }.OfType<Node2D>();
        NativeBodies.Add(visuals, bodies.Select(body => new NativeBody(body, body.Owner,
            new Node[] { body }.Concat(Descendants(body)).OfType<CanvasItem>()
                .Select(item => new NativeMaterial(item, item.Material, item.UseParentMaterial)).ToArray())).ToArray());
    }

    internal static void AssertNativeMaterialRetention(NCreatureVisuals visuals)
    {
        Require(NativeBodies.TryGetValue(visuals, out var bodies),
            "production visuals were observed before corruption attachment");
        foreach (var native in bodies!)
        {
            Require(native.Body.GetParent() is CanvasGroup && native.Body.Owner == native.Owner &&
                native.Materials.All(entry => entry.Node.Material == entry.Material &&
                    entry.Node.UseParentMaterial == entry.UseParentMaterial),
                $"production attachment retained {native.Body.Name}, its scene owner and all {native.Materials.Length} native body materials");
        }
    }

    private static void AssertIsolation(NCreature node, NCreature human, string label)
    {
        var controller = Find<Node>(node.Visuals, "BoundEcho");
        var group = Find<CanvasGroup>(node.Visuals, "BoundEchoBody");
        var back = Find<Node2D>(node.Visuals, "BoundEchoBack");
        var front = Find<Node2D>(node.Visuals, "BoundEchoFront");
        var original = node.Visuals.GetNode<Node2D>("%Visuals");
        Require(controller.GetParent() == node.Visuals && node.Body == original && original.GetParent() == group &&
            group.GetChildCount() == 1 && group.Material is ShaderMaterial,
            $"{label}: one body-only shader group wraps original %Visuals under production controller");
        Require(back.GetParent() == group.GetParent() && front.GetParent() == group.GetParent() &&
            back.GetIndex() < group.GetIndex() && front.GetIndex() > group.GetIndex(),
            $"{label}: independent binding layers bracket, and are not sampled inside, the body group");
        Require(!group.IsAncestorOf(node.Hitbox) && !group.IsAncestorOf(node.GetNode("%HealthBar")) &&
            !group.IsAncestorOf(node.IntentContainer) && node.OrbManager != null &&
            !group.IsAncestorOf(node.OrbManager) && !group.IsAncestorOf(Find<Node>(node, "CorruptedPlayerTelegraph")) &&
            !group.IsAncestorOf(node.Visuals.GetNode("%Bounds")),
            $"{label}: hitbox, bounds, health, intents, orbs and card telegraph stay outside body group");
        Require(node.HasSpineAnimation && original.Scale.X < 0 &&
            !Descendants(human).Any(n => n.Name.ToString().StartsWith("BoundEcho", StringComparison.Ordinal)),
            $"{label}: Corrupted Player retains left-facing native Spine; matching normal human has no effect");
        var bounds = node.Visuals.GetNode<Control>("%Bounds");
        MainFile.Logger.Info($"CORRUPTION GEOMETRY {label}: creature={node.GetPath()} position={node.Position} " +
            $"visuals.position={node.Visuals.Position} visuals.scale={node.Visuals.Scale} " +
            $"visuals.global={node.Visuals.GetGlobalTransform()} " +
            $"body.path={original.GetPath()} body.local={original.Transform} body.global={original.GlobalTransform} " +
            $"group.global={group.GlobalTransform} bounds.path={bounds.GetPath()} bounds.position={bounds.Position} " +
            $"bounds.size={bounds.Size} bounds.scale={bounds.Scale} bounds.global={bounds.GetGlobalTransform()} " +
            $"hitbox.position={node.Hitbox.Position} hitbox.size={node.Hitbox.Size}");
        MainFile.Logger.Info($"CORRUPTION TREE {label}:\n" + string.Join("\n", Descendants(node.Visuals).Select(child =>
            $"{node.Visuals.GetPathTo(child)} [{child.GetType().Name}] owner={child.Owner?.Name} " +
            (child is CanvasItem item
                ? $"visible={item.Visible} modulate={item.Modulate} material={item.Material?.GetType().Name ?? "none"}"
                : ""))));
    }

    [HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.CreateVisuals))]
    internal static class CorruptionNativeMaterialCapturePatch
    {
        private static void Postfix(NCreatureVisuals __result)
        {
            if (CorruptionVisualPlaytest.RecordingNativeVisuals)
                CorruptionVisualPlaytest.RememberNativeVisuals(__result);
        }
    }

    [HarmonyPatch(typeof(CorruptedPlayer), nameof(CorruptedPlayer.CreateCustomVisuals))]
    internal static class CorruptionNativeMaterialRetentionPatch
    {
        private static void Postfix(NCreatureVisuals __result)
        {
            if (CorruptionVisualPlaytest.RecordingNativeVisuals)
                CorruptionVisualPlaytest.AssertNativeMaterialRetention(__result);
        }
    }

    private static T Find<T>(Node root, string name) where T : Node =>
        Descendants(root).SingleOrDefault(n => n.Name == name) as T
        ?? throw new InvalidOperationException($"Corruption visual contract missing {name} ({typeof(T).Name}).");

    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static float InheritedAlpha(CanvasItem node)
    {
        var alpha = 1f;
        for (Node? current = node; current != null; current = current.GetParent())
            if (current is CanvasItem item)
                alpha *= item.Modulate.A;
        return alpha;
    }

    private static Task Capture(string character, string stage) =>
        NativeDemoPlaytest.Capture($"corruption-{character}-{stage}");

    private static async Task WaitFor(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Corruption visual probe timed out: " + description);
            await NGame.Instance!.AwaitProcessFrame();
        }
    }

    private static void Require(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException("CORRUPTION ASSERTION FAILED: " + description);
        MainFile.Logger.Info("CORRUPTION ASSERT: " + description);
    }
}
