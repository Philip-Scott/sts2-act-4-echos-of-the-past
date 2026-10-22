using System.Text.Json;
using System.Text.RegularExpressions;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;
using TheArchitect.TheArchitectCode.Powers;

internal static class CorruptedPlayerBuffTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        foreach (var count in new[] { 1, 2, 3, 4 })
        {
            test($"Ring of the Viper: {count} Silents each gain exactly 1 Artifact per application", () =>
            {
                var combat = new CombatState();
                for (var index = 0; index < count; index++)
                {
                    var player = (Player)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Player));
                    AccessTools.Field(typeof(Player), "<Creature>k__BackingField")
                        .SetValue(player, new Creature(player, 100, 100));
                    combat.AddPlayer(player);
                }
                var artifact = ModelDb.Power<ArtifactPower>();
                var normalEnemy = new Creature(new TenHpMonster().ToMutable(), CombatSide.Enemy, null);
                check(artifact.GetScaledAmountForMultiplayer(combat, normalEnemy, 1, normalEnemy, null) == count,
                    "Reproduce native Artifact scaling before applying the patch.");
                var harmony = new Harmony("TheArchitect.Tests.RingOfTheViper");
                try
                {
                    harmony.CreateClassProcessor(typeof(RingOfTheViperArtifactScaling)).Patch();
                    for (var index = 0; index < count; index++)
                    {
                        var owner = new Creature(new TenHpMonster().ToMutable(), CombatSide.Enemy, null);
                        // The headless harness uses the registered buff ID without loading the native actor/UI.
                        var buff = ModelDb.Power<StrengthPower>().ToMutable();
                        AccessTools.Field(typeof(AbstractModel), "<Id>k__BackingField")
                            .SetValue(buff, CorruptedPlayerBuffs.RingOfTheViper);
                        buff.ApplyInternal(owner, 1);
                        for (var turn = 0; turn < 3; turn++)
                        {
                            var gained = artifact.GetScaledAmountForMultiplayer(combat, owner, 1, owner, null);
                            check(gained == 1, $"Silent {index}, turn {turn}: gained {gained} Artifact.");
                        }
                        check(artifact.GetScaledAmountForMultiplayer(combat, owner, 2, owner, null) == 2,
                            "Preserve the requested amount rather than hard-coding one stack.");
                        buff.RemoveInternal();
                        check(artifact.GetScaledAmountForMultiplayer(combat, owner, 1, owner, null) == count,
                            "The exemption must end when Ring of the Viper is removed.");
                    }
                    check(artifact.GetScaledAmountForMultiplayer(combat, normalEnemy, 1, normalEnemy, null) == count,
                        "Normal enemies must retain native multiplayer Artifact scaling.");
                }
                finally
                {
                    harmony.UnpatchAll(harmony.Id);
                }
            });
        }

        test("Corruption buffs: each base character receives its own buff below Ascension 9", () =>
        {
            (CharacterModel Character, ModelId Buff)[] expected =
            [
                (ModelDb.Character<Ironclad>(), CorruptedPlayerBuffs.SingeingIchor),
                (ModelDb.Character<Silent>(), CorruptedPlayerBuffs.RingOfTheViper),
                (ModelDb.Character<Regent>(), CorruptedPlayerBuffs.TarnishedCrown),
                (ModelDb.Character<Necrobinder>(), CorruptedPlayerBuffs.FracturedUrn),
                (ModelDb.Character<Defect>(), CorruptedPlayerBuffs.OvervoltedMatrix)
            ];
            foreach (var (character, buff) in expected)
            {
                var buffs = CorruptedPlayerBuffs.For(character, 8);
                check(buffs.SequenceEqual([buff]), $"{character.GetType().Name}: {string.Join(",", buffs)}");
            }
        });

        test("Corruption buffs: Ascension 9+ adds Echo Shard after the character buff", () =>
        {
            check(CorruptedPlayerBuffs.For(ModelDb.Character<Ironclad>(), 9)
                .SequenceEqual([CorruptedPlayerBuffs.SingeingIchor, CorruptedPlayerBuffs.EchoShard]), "A9 Ironclad");
            check(CorruptedPlayerBuffs.For(ModelDb.Character<Defect>(), 10)
                .SequenceEqual([CorruptedPlayerBuffs.OvervoltedMatrix, CorruptedPlayerBuffs.EchoShard]), "A10 Defect");
            check(!CorruptedPlayerBuffs.For(ModelDb.Character<Silent>(), 0).Contains(CorruptedPlayerBuffs.EchoShard), "A0 Silent");
        });

        test("Corruption buffs: characters without a buff still receive Echo Shard at Ascension 9", () =>
        {
            check(CorruptedPlayerBuffs.For(Unmapped(), 8).Count == 0, "No character buff below A9");
            check(CorruptedPlayerBuffs.For(Unmapped(), 9).SequenceEqual([CorruptedPlayerBuffs.EchoShard]),
                "Only Echo Shard at A9");
        });

        test("Corruption buffs: IDs match their BaseLib power classes and have localized text and icons", () =>
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "TheArchitect.csproj")))
                root = root.Parent;
            check(root != null, "Cannot locate the project sources.");
            var source = File.ReadAllText(Path.Combine(root!.FullName, "TheArchitectCode/Powers/CorruptionBuffPowers.cs"));
            var matrix = source.Split("public sealed class OvervoltedMatrixPower", StringSplitOptions.None)[1]
                .Split("public sealed class EchoShardPower", StringSplitOptions.None)[0];
            check(matrix.Contains("Task AfterSideTurnEnd(") && !matrix.Contains("Task AfterSideTurnStart("),
                "Overvolted Matrix must gain Focus after end-of-turn Orbs, not at turn start.");
            var classIds = Regex.Matches(source, @"public sealed class (\w+Power)\b")
                .Select(match => "THEARCHITECT-" + Regex.Replace(match.Groups[1].Value, "(?<!^)([A-Z])", "_$1").ToUpperInvariant())
                .ToHashSet();
            ModelId[] buffs =
            [
                CorruptedPlayerBuffs.SingeingIchor, CorruptedPlayerBuffs.RingOfTheViper,
                CorruptedPlayerBuffs.TarnishedCrown, CorruptedPlayerBuffs.FracturedUrn,
                CorruptedPlayerBuffs.OvervoltedMatrix, CorruptedPlayerBuffs.EchoShard
            ];
            check(classIds.SetEquals(buffs.Select(id => id.Entry)), string.Join(",", classIds));
            using var json = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(root.FullName, "TheArchitect/localization/eng/powers.json")));
            foreach (var buff in buffs)
                foreach (var key in new[] { "title", "description", "smartDescription" })
                    check(json.RootElement.TryGetProperty($"{buff.Entry}.{key}", out _), $"{buff.Entry}.{key}");
            foreach (var key in new[] { "description", "smartDescription" })
                check(json.RootElement.GetProperty($"{CorruptedPlayerBuffs.OvervoltedMatrix.Entry}.{key}")
                    .GetString()!.Contains("At the end of its turn, gain"),
                    $"Overvolted Matrix {key} must describe end-of-turn Focus gain.");
            foreach (var buff in buffs)
            {
                var file = buff.Entry["THEARCHITECT-".Length..].ToLowerInvariant() + ".png";
                foreach (var dir in new[] { "images/powers", "images/powers/big" })
                    check(File.Exists(Path.Combine(root.FullName, "TheArchitect", dir, file)), $"{dir}/{file}");
            }
        });
    }

    // Deprived is not registered in the test ModelDb; skip its constructor to get a character with no buff.
    private static CharacterModel Unmapped() =>
        (CharacterModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Deprived));
}
