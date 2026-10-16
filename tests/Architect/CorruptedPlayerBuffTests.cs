using System.Text.Json;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

internal static class CorruptedPlayerBuffTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
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
