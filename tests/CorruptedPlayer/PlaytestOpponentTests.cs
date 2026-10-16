using System.Text.Json;
using TheArchitect.TheArchitectCode.Persistence;
using TheArchitect.TheArchitectCode.UI;

internal static class PlaytestOpponentTests
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (var count in Enumerable.Range(1, 4))
            test($"solo save retains, previews, and resets {count} independent playtest opponents", () =>
            {
                var cards = new[] { JsonSerializer.SerializeToElement(new { id = "CARD.BASH" }) };
                var snapshot = new CorruptedPlayerSnapshot("CHARACTER.IRONCLAD", 80, cards,
                    CorruptedPlayerSnapshot.Hash("CHARACTER.IRONCLAD", 80, cards));
                var state = new ArchitectRun { Entered = true, PlaytestOpponents = Enumerable.Repeat(snapshot, count).ToArray() };
                state.ValidatePlaytestOpponents();
                var json = JsonSerializer.Serialize(state);
                var restored = JsonSerializer.Deserialize<ArchitectRun>(json)!;
                restored.ValidatePlaytestOpponents();
                Check(restored.EncounterSnapshots.Count == count && restored.EncounterCounterpartNetId(0) == null);
                Check(ContinueRunPreviewSelection.Read(json, [1]).Snapshots.Length == count);
                var cleared = CorruptedDeckResetSelection.Create(json, [1]);
                Check(ContinueRunPreviewSelection.Read(cleared.Json, [1]).Snapshots.Length == 0);
                Reject(() => ContinueRunPreviewSelection.Read(json, [1, 2]));
                state.StartingHostNetId = 1;
                Reject(state.ValidatePlaytestOpponents);
            });
        test("malformed local opponent saves fail explicitly", () =>
        {
            foreach (var opponents in new[] { Array.Empty<CorruptedPlayerSnapshot>(), new CorruptedPlayerSnapshot[5] })
                Reject(new ArchitectRun { Entered = true, PlaytestOpponents = opponents }.ValidatePlaytestOpponents);
        });
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Playtest opponent assertion failed.");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (JsonException) { return; }
        throw new InvalidOperationException("Invalid playtest state was accepted.");
    }
}
