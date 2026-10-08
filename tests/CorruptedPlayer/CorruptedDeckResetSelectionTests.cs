using System.Text.Json;
using System.Text.Json.Nodes;
using TheArchitect.TheArchitectCode.Persistence;
using TheArchitect.TheArchitectCode.UI;

internal static class CorruptedDeckResetSelectionTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("solo reset removes malformed decks without resolving models or changing other run fields", () =>
        {
            const string json = """{"Id":"run","Entered":true,"EntrySnapshot":{"Snapshot":"broken"},"FutureField":{"keep":42}}""";
            var reset = CorruptedDeckResetSelection.Create(json, [10]);
            var root = JsonNode.Parse(reset.Json)!;
            Check(reset.Party == null && root["EntrySnapshot"] == null && root["Id"]!.GetValue<string>() == "run");
            Check(root["FutureField"]!["keep"]!.GetValue<int>() == 42);
            Check(ContinueRunPreviewSelection.Read(reset.Json, [10]).StatusKey == "FROZEN_FIRST_VISIT");
            Check(CorruptedDeckResetSelection.Create(reset.Json, [10]).Json == reset.Json);
        });
        test("resetting an unselected solo run does not prematurely enter Act 4", () =>
        {
            var reset = CorruptedDeckResetSelection.Create(null, [10]);
            Check(ContinueRunPreviewSelection.Read(reset.Json, [10]).StatusKey == "NOT_SELECTED");
        });
        foreach (int count in new[] { 2, 3, 4 })
        foreach (bool entered in new[] { true, false })
        {
            test($"reset preserves exact {count}-player identities and freezes First Visit with entered={entered}", () =>
            {
                var state = State(count, entered);
                var root = JsonSerializer.SerializeToNode(state)!;
                var key = entered ? "EntryParty" : "PendingParty";
                root[key]!["Lineage"] = "unreadable deck data";
                var reset = CorruptedDeckResetSelection.Create(root.ToJsonString(), state.OriginalNetworkIds!,
                    10, state.Origin!.HostProfileUuid);
                var cleared = JsonSerializer.Deserialize<ArchitectRun>(reset.Json)!;
                Check(reset.Party!.GroupKey == state.Origin.GroupKey && cleared.Id == state.Id);
                Check(cleared.StartingHostNetId == 10 && cleared.Entered == entered);
                Check(cleared.Origin!.Participants.SequenceEqual(state.Origin.Participants));
                Check((cleared.EntryParty ?? cleared.PendingParty) is { Lineage: null });
                Check(cleared.Origin.Lineage == null && cleared.EntrySnapshot == null);
                var selected = ContinueRunPreviewSelection.Read(reset.Json, state.OriginalNetworkIds!);
                Check(selected.Snapshots.Length == 0 && selected.StatusKey == "FROZEN_FIRST_VISIT");
            });
        }
        test("reset recovers missing frozen party from validated original membership", () =>
        {
            var state = State(2, true);
            state.EntryParty = null;
            var reset = CorruptedDeckResetSelection.Create(JsonSerializer.Serialize(state), [10, 20], 10,
                state.Origin!.HostProfileUuid);
            Check(ContinueRunPreviewSelection.Read(reset.Json, [10, 20]).StatusKey == "FROZEN_FIRST_VISIT");
        });
        test("reset cannot target another host profile or party and rejects lost identities", () =>
        {
            var state = State(2, true);
            var json = JsonSerializer.Serialize(state);
            Reject(() => CorruptedDeckResetSelection.Create(json, [10, 20], 20, state.Origin!.HostProfileUuid));
            Reject(() => CorruptedDeckResetSelection.Create(json, [10, 20], 10, Guid.NewGuid()));
            Reject(() => CorruptedDeckResetSelection.Create(json, [10, 30], 10, state.Origin!.HostProfileUuid));
            Reject(() => CorruptedDeckResetSelection.Create(json, [10]));
            Reject(() => CorruptedDeckResetSelection.Create(null, [10, 20], 10, Guid.NewGuid()));
            Reject(() => CorruptedDeckResetSelection.Create("{", [10]));
        });
    }

    private static ArchitectRun State(int count, bool entered)
    {
        var participants = Enumerable.Range(1, count).Select(index =>
            new SuccessorParticipant((ulong)index * 10, new Guid(index, 0, 0, new byte[8]))).ToArray();
        var origin = new FrozenCorruptedParty
        {
            RunId = "reset-run", HostNetId = 10, HostProfileUuid = participants[0].ProfileUuid,
            Participants = participants, GroupKey = SuccessorGroup.Key(participants.Select(player => player.ProfileUuid))
        };
        var state = new ArchitectRun();
        state.BindOrigin(origin);
        if (entered)
            state.EnterParty(origin);
        else
            state.PendingParty = origin;
        return state;
    }

    private static void Check(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("Corrupted deck reset assertion failed.");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return; }
        throw new InvalidOperationException("Unsafe reset was not rejected.");
    }
}
