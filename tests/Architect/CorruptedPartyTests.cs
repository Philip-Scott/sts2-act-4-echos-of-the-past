using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

internal static class CorruptedPartyTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        foreach (var count in new[] { 1, 2, 3, 4 })
        {
            test($"corrupted party {count}: only the last confirmed death starts the Architect", () =>
            {
                var phase = new CorruptedPartyPhase(count);
                for (var index = count - 1; index >= 0; index--)
                {
                    check(phase.Defeat(index) == (index == 0), "Handoff must wait for every distinct member.");
                    check(!phase.Defeat(index), "Repeated or nested death hooks must not repeat the handoff.");
                }
            });

            test($"corrupted party {count}: HP depends on ascension, not party size", () =>
            {
                foreach (var ascension in new[] { 0, 7, 8, 10 })
                {
                    var hp = Enumerable.Range(0, count)
                        .Select(_ => CorruptedPlayerHealth.CalculateMaxHp(81, ascension)).ToArray();
                    check(hp.All(value => value == (ascension >= 8 ? 203 : 162)),
                        "Each member must retain the same rounded 2x/2.5x snapshot HP.");
                }
            });
        }

        test("corrupted party: a pending or prevented death cannot complete the phase", () =>
        {
            var phase = new CorruptedPartyPhase(3);
            check(!phase.Defeat(1), "First death must not transition.");
            check(!phase.Defeat(0), "An unconfirmed third death must still block the Architect.");
            check(!phase.Defeat(1), "Duplicate confirmation must not count as the missing member.");
            check(phase.Defeat(2), "Final confirmation must trigger exactly one handoff.");
            check(!phase.Defeat(2), "Handoff must stay latched.");
        });
    }
}
