using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

internal static class CorruptedPartyTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        foreach (var count in new[] { 1, 2, 3, 4 })
        {
            test($"corrupted party {count}: HP depends on ascension, not party size", () =>
            {
                foreach (var ascension in new[] { 0, 7, 8, 10 })
                {
                    var hp = Enumerable.Range(0, count)
                        .Select(_ => CorruptedPlayerHealth.CalculateMaxHp(81, ascension)).ToArray();
                    check(hp.All(value => value == (ascension >= 8 ? 333 : 284)),
                        "Each member must retain the same rounded 3.5x/4.1x snapshot HP.");
                }
            });
        }
    }
}
