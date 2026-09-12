using System.Text.Json;
using TheArchitect.TheArchitectCode.Persistence;
using TheArchitect.TheArchitectCode.UI;

internal static class ContinueRunPreviewTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("continue solo preview uses the frozen snapshot and preserves unknown card fields", () =>
        {
            var snapshot = Snapshot(80);
            var state = new ArchitectRun
            {
                Entered = true,
                EntrySnapshot = new CorruptedPlayerEnvelope { Revision = 4, Snapshot = snapshot }
            };
            var json = JsonSerializer.Serialize(state);
            var result = ContinueRunPreviewSelection.Read(json, [10]);
            Check(result.StatusKey == null && result.Snapshots.Single().ContentHash == snapshot.ContentHash);
            Check(result.Snapshots[0].Deck[0].GetProperty("custom").GetString() == "preserved");
            Check(JsonSerializer.Serialize(state) == json);
        });
        test("continue solo First Visit remains frozen even without an entry snapshot", () =>
        {
            var result = Read(new ArchitectRun { Entered = true }, [10]);
            Check(result.Snapshots.Length == 0 && result.StatusKey == "FROZEN_FIRST_VISIT");
        });
        test("continue preview never treats an unselected or missing extension as a frozen First Visit", () =>
        {
            Check(ContinueRunPreviewSelection.Read(null, [10]).StatusKey == "NOT_SELECTED");
            Check(Read(new ArchitectRun(), [10]).StatusKey == "NOT_SELECTED");
        });
        foreach (var count in new[] { 2, 3, 4 })
        {
            test($"continue {count}-player preview uses the full saved party regardless of connected lobby size", () =>
            {
                var state = PartyState(count);
                var frozen = state.Origin! with { Lineage = Lineage(state.Origin!) };
                state.EnterParty(frozen);
                var json = JsonSerializer.Serialize(state);
                var result = ContinueRunPreviewSelection.Read(json,
                    state.OriginalNetworkIds!.Reverse().ToArray());
                Check(result.StatusKey == null && result.Snapshots.Length == count);
                Check(result.Snapshots.Select(snapshot => snapshot.ContentHash)
                    .SequenceEqual(frozen.Lineage!.Members.Select(member => member.Snapshot.ContentHash)));
                Check(JsonSerializer.Serialize(state) == json && state.EntryParty!.Digest == frozen.Digest);
            });
        }
        test("continue multiplayer preview preserves a saved pending selection before entry commits", () =>
        {
            var state = PartyState(2);
            state.PendingParty = state.Origin! with { Lineage = Lineage(state.Origin!) };
            Check(Read(state, [10, 20]).Snapshots.Length == 2);
            Check(!state.Entered && state.EntryParty == null && state.PendingParty != null);
        });
        test("continue multiplayer frozen First Visit takes precedence over other saved data", () =>
        {
            var state = PartyState(2);
            state.EnterParty(state.Origin!);
            state.EntrySnapshot = new CorruptedPlayerEnvelope { Snapshot = Snapshot(200) };
            var result = Read(state, [10, 20]);
            Check(result.Snapshots.Length == 0 && result.StatusKey == "FROZEN_FIRST_VISIT");
        });
        test("continue multiplayer origin registration is not a selected First Visit", () =>
        {
            Check(Read(PartyState(2), [10, 20]).StatusKey == "NOT_SELECTED");
        });
        test("continue preview rejects missing or mismatched frozen multiplayer data", () =>
        {
            var state = PartyState(2);
            state.Entered = true;
            Reject(() => Read(state, [10, 20]));
            state.Entered = false;
            state.EnterParty(state.Origin! with { Lineage = Lineage(state.Origin!) });
            Reject(() => Read(state, [10, 30]));
            Reject(() => Read(state, [10]));
            state.Id = "another-run";
            Reject(() => Read(state, [10, 20]));
        });
        test("continue preview rejects invalid snapshots without silently selecting another lineage", () =>
        {
            var state = new ArchitectRun
            {
                Entered = true,
                EntrySnapshot = new CorruptedPlayerEnvelope { Snapshot = Snapshot(80) with { MaxHp = 90 } }
            };
            Reject(() => Read(state, [10]));
            Reject(() => ContinueRunPreviewSelection.Read("null", [10]));
            Reject(() => ContinueRunPreviewSelection.Read("{", [10]));
        });
        test("continue preview retains unavailable characters for actionable rendering instead of replacing the visit", () =>
        {
            var snapshot = Snapshot(80, "CHARACTER.MISSING_MOD");
            var result = Read(new ArchitectRun
            {
                Entered = true, EntrySnapshot = new CorruptedPlayerEnvelope { Snapshot = snapshot }
            }, [10]);
            Check(result.StatusKey == null && result.Snapshots.Single().CharacterId == snapshot.CharacterId);
        });
    }

    private static ContinueRunPreviewSelection Read(ArchitectRun state, ulong[] players) =>
        ContinueRunPreviewSelection.Read(JsonSerializer.Serialize(state), players);

    private static ArchitectRun PartyState(int count)
    {
        var players = Enumerable.Range(1, count)
            .Select(index => new SuccessorParticipant((ulong)index * 10, new Guid(index, 0, 0, new byte[8])))
            .ToArray();
        var state = new ArchitectRun();
        state.BindOrigin(new FrozenCorruptedParty
        {
            RunId = "saved-run", HostNetId = players[0].NetId, HostProfileUuid = players[0].ProfileUuid,
            GroupKey = SuccessorGroup.Key(players.Select(player => player.ProfileUuid)), Participants = players
        });
        return state;
    }

    private static CorruptedPartyEnvelope Lineage(FrozenCorruptedParty party) => new()
    {
        HostProfileUuid = party.HostProfileUuid, GroupKey = party.GroupKey, Revision = 3,
        TerminalRunId = "previous-run", Outcome = "ArchitectLoss",
        Members = party.Participants.Select((player, index) =>
            new CorruptedPartyMember(player.ProfileUuid, Snapshot(80 + index))).ToArray()
    };

    private static CorruptedPlayerSnapshot Snapshot(int hp, string character = "CHARACTER.IRONCLAD")
    {
        var cards = new[] { JsonSerializer.Deserialize<JsonElement>("""{"id":"CARD.STRIKE_IRONCLAD","custom":"preserved"}""") };
        return new(character, hp, cards, CorruptedPlayerSnapshot.Hash(character, hp, cards));
    }

    private static void Check(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("Continue-run preview assertion failed.");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (JsonException) { return; }
        throw new InvalidOperationException("Expected invalid continue-run preview data to be rejected.");
    }
}
