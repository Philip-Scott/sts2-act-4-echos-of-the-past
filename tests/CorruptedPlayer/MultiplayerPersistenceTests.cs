using System.Text.Json;
using TheArchitect.TheArchitectCode.Multiplayer;
using TheArchitect.TheArchitectCode.Persistence;

internal static class MultiplayerPersistenceTests
{
    internal static void Run(Action<string, Action> test)
    {
        static void Check(bool value)
        {
            if (!value) throw new InvalidOperationException("Multiplayer persistence assertion failed.");
        }
        static void Reject(Action action)
        {
            try { action(); }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException)
            {
                return;
            }
            throw new InvalidOperationException("Expected invalid party data to be rejected.");
        }
        static CorruptedPlayerSnapshot Snapshot(int hp, string card = """{"id":"CARD.STRIKE_IRONCLAD","props":{"custom":"untouched"}}""")
        {
            var deck = new[] { JsonSerializer.Deserialize<JsonElement>(card) };
            return new("CHARACTER.IRONCLAD", hp, deck, CorruptedPlayerSnapshot.Hash("CHARACTER.IRONCLAD", hp, deck));
        }
        var host = Guid.Parse("10000000-0000-0000-0000-000000000000");
        var client = Guid.Parse("20000000-0000-0000-0000-000000000000");
        var other = Guid.Parse("30000000-0000-0000-0000-000000000000");
        var participants = new[] { new SuccessorParticipant(10, host), new SuccessorParticipant(20, client) };
        var party = new FrozenCorruptedParty
        {
            RunId = "run-1", HostNetId = 10, HostProfileUuid = host,
            GroupKey = SuccessorGroup.Key([host, client]), Participants = participants
        };
        var members = new[] { new CorruptedPartyMember(host, Snapshot(80)), new CorruptedPartyMember(client, Snapshot(99)) };
        var lineage = new CorruptedPartyEnvelope
        {
            HostProfileUuid = host, GroupKey = party.GroupKey, Revision = 1,
            TerminalRunId = "previous-run", Outcome = "ArchitectLoss", Members = members
        };
        var frozen = party with { Lineage = lineage };

        test("successor groups use exact order-independent persistent membership", () =>
        {
            Check(SuccessorGroup.Key([host, client]) == SuccessorGroup.Key([client, host]));
            Check(SuccessorGroup.Key([host, client]) != SuccessorGroup.Key([host, other]));
            Reject(() => SuccessorGroup.Key([host, host]));
            Reject(() => SuccessorGroup.Key([host, Guid.Empty]));
            Reject(() => SuccessorGroup.Key([host]));
        });
        test("frozen parties validate complete original network and profile membership", () =>
        {
            frozen.Validate([20, 10], 10);
            Reject(() => frozen.Validate([10], 10));
            Reject(() => frozen.Validate([10, 20], 20));
            Reject(() => (frozen with { Participants = [participants[0], participants[0]] }).Validate([10, 20], 10));
            Reject(() => (frozen with { GroupKey = SuccessorGroup.Key([host, other]) }).Validate([10, 20], 10));
            Reject(() => (frozen with { HostProfileUuid = client }).Validate([10, 20], 10));
            Reject(() => (frozen with { Lineage = lineage with { Members = [members[0]] } }).Validate([10, 20], 10));
            Reject(() => (frozen with { Lineage = lineage with { Members = [members[0], members[0]] } }).Validate([10, 20], 10));
        });
        test("frozen save round trips preserve full parties and unknown card fields", () =>
        {
            var restored = JsonSerializer.Deserialize<FrozenCorruptedParty>(JsonSerializer.Serialize(frozen))!;
            restored.Validate([10, 20], 10);
            Check(restored.Digest == frozen.Digest);
            Check(restored.Lineage!.Members.Length == 2);
            Check(restored.Lineage.Members[1].Snapshot.MaxHp == 99);
            Check(restored.Lineage.Members[0].Snapshot.Deck[0].GetProperty("props").GetProperty("custom").GetString() == "untouched");
            Check((frozen with { Lineage = lineage with { Revision = 2 } }).Digest != frozen.Digest);
        });
        test("whole-party validation rejects corruption without reducing membership", () =>
        {
            var corrupt = members[1] with { Snapshot = members[1].Snapshot with { MaxHp = 100 } };
            Reject(() => (frozen with { Lineage = lineage with { Members = [members[0], corrupt] } }).Validate([10, 20], 10));
            Reject(() => (frozen with { Lineage = lineage with { SchemaVersion = 99 } }).Validate([10, 20], 10));
            Reject(() => (frozen with { Lineage = lineage with { HostProfileUuid = client } }).Validate([10, 20], 10));
        });
        test("native save extension state freezes the whole encounter and never reselects on reload", () =>
        {
            var state = new ArchitectRun();
            state.BindOrigin(party);
            state.EnterParty(frozen);
            Check(state.Entered && state.EncounterRevision == 1 && state.EncounterSnapshots.Count == 2);
            var restored = JsonSerializer.Deserialize<ArchitectRun>(JsonSerializer.Serialize(state))!;
            Check(restored.EntryParty!.Digest == frozen.Digest && restored.EncounterSnapshots.Count == 2);
            restored.EnterParty(frozen);
            Reject(() => restored.EnterParty(frozen with { Lineage = lineage with { Revision = 2 } }));
            Reject(() => restored.EnterParty(party));
            Reject(() => restored.BindNetworkOrigin([10, 20], 20));
            Reject(() => restored.BindNetworkOrigin([10, 30], 10));
            var firstVisit = new ArchitectRun();
            firstVisit.BindOrigin(party);
            firstVisit.EnterParty(party);
            Reject(() => firstVisit.EnterParty(frozen));
            Check(firstVisit.EncounterSnapshots.Count == 0 && firstVisit.EncounterRevision == 0);
        });
        test("single-player v1 entry snapshot and playtest setters remain compatible", () =>
        {
            var envelope = new CorruptedPlayerEnvelope { ProfileUuid = host, Revision = 7, Snapshot = Snapshot(80) };
            var json = JsonSerializer.Serialize(new { Id = "legacy", Entered = true, EntrySnapshot = envelope });
            var state = JsonSerializer.Deserialize<ArchitectRun>(json)!;
            Check(state.EntryParty == null && state.EncounterSnapshots.Count == 1 && state.EncounterRevision == 7);
            Check(state.EncounterCounterpartNetId(0) == null);
            state.EntrySnapshot = envelope with { Snapshot = Snapshot(90), Revision = 8 };
            Check(state.EncounterSnapshots[0].MaxHp == 90 && state.EncounterRevision == 8);
        });
        test("encounter counterparts follow profile identity across reordered members and reloads", () =>
        {
            var reordered = frozen with
            {
                Participants = participants.Reverse().ToArray(),
                Lineage = lineage with { Members = members.Reverse().ToArray() }
            };
            reordered.Validate([20, 10], 10);
            var state = new ArchitectRun { Entered = true, EntryParty = reordered };
            Check(state.EncounterSnapshots[0].MaxHp == 99 && state.EncounterCounterpartNetId(0) == 20);
            Check(state.EncounterSnapshots[1].MaxHp == 80 && state.EncounterCounterpartNetId(1) == 10);
            var restored = JsonSerializer.Deserialize<ArchitectRun>(JsonSerializer.Serialize(state))!;
            Check(restored.EncounterCounterpartNetId(0) == 20 && restored.EncounterCounterpartNetId(1) == 10);
            restored.EntryParty = reordered with { Participants = participants };
            Check(restored.EncounterCounterpartNetId(0) == 20 && restored.EncounterCounterpartNetId(1) == 10);
            restored.EntryParty = reordered with { Participants = [participants[0]] };
            Reject(() => restored.EncounterCounterpartNetId(0));
        });
        test("host entry waits for all exact party acknowledgements", () =>
        {
            var consensus = new PartyEntryConsensus(frozen, [10, 20], 10);
            Check(!consensus.Ready);
            consensus.Acknowledge(10, frozen.Digest);
            Check(!consensus.Ready);
            consensus.Acknowledge(10, frozen.Digest);
            Check(!consensus.Ready);
            Reject(() => consensus.Acknowledge(30, frozen.Digest));
            Reject(() => consensus.Acknowledge(20, party.Digest));
            Check(!consensus.Ready);
            consensus.Acknowledge(20, frozen.Digest);
            Check(consensus.Ready);
            consensus.Reject("missing client character");
            Check(!consensus.Ready);
            Reject(() => consensus.Acknowledge(20, frozen.Digest));
        });
        test("First Visit is an explicit synchronized whole-group decision", () =>
        {
            party.Validate([10, 20], 10);
            var consensus = new PartyEntryConsensus(party, [10, 20], 10);
            consensus.Acknowledge(10, party.Digest);
            Check(!consensus.Ready);
            consensus.Acknowledge(20, party.Digest);
            Check(consensus.Ready && consensus.Party.Lineage == null);
        });
        test("chunked frozen payload handles ordering duplicates and rejects mixing", () =>
        {
            var chunks = new PartyPayloadBuffer();
            Check(chunks.Add(frozen.Digest, 1, 2, "world") == null);
            Check(chunks.Add(frozen.Digest, 1, 2, "world") == null);
            Check(chunks.Add(frozen.Digest, 0, 2, "hello ") == "hello world");
            Reject(() => chunks.Add(party.Digest, 0, 2, "hello "));
            Reject(() => chunks.Add(frozen.Digest, 1, 2, "other"));
            Reject(() => new PartyPayloadBuffer().Add(frozen.Digest, 0, 257, ""));
            Reject(() => new PartyPayloadBuffer().Add(frozen.Digest, 2, 2, ""));
            Reject(() => new PartyPayloadBuffer().Add(frozen.Digest, 0, 1, new string('x', 16001)));
        });

        var directory = Path.Combine("artifacts", "multiplayer-persistence-tests", Guid.NewGuid().ToString("N"));
        try
        {
            test("profiles without snapshots persist identity and reuse legacy envelope identity", () =>
            {
                var path = Path.Combine(directory, "new-profile", "identity.json");
                var id = ProfileIdentityStore.GetOrCreate(path, () => null);
                Check(id != Guid.Empty && ProfileIdentityStore.GetOrCreate(path, () => other) == id);
                Check(ProfileIdentityStore.GetOrCreate(Path.Combine(directory, "legacy-profile", "identity.json"), () => host) == host);
                File.WriteAllText(path, """{"SchemaVersion":1,"ProfileUuid":"00000000-0000-0000-0000-000000000000"}""");
                Reject(() => ProfileIdentityStore.GetOrCreate(path, () => other));
            });
            test("host atomically records every terminal member and deduplicates outcome callbacks", () =>
            {
                Check(CorruptedPartyStore.Load(directory, host, [host, client]) == null);
                Check(CorruptedPartyStore.Commit(directory, host, party, "ArchitectLoss", members) == 1);
                Check(CorruptedPartyStore.Commit(directory, host, party, "ArchitectLoss", members) == 1);
                var saved = CorruptedPartyStore.Load(directory, host, [client, host])!;
                Check(saved.Members.Length == 2 && saved.TerminalRunId == party.RunId && saved.Outcome == "ArchitectLoss");
                var nextRun = party with { RunId = "run-2" };
                Check(CorruptedPartyStore.Commit(directory, host, nextRun, "ArchitectWin", members) == 2);
                Check(CorruptedPartyStore.Load(directory, host, [host, client])!.Outcome == "ArchitectWin");
            });
            test("different hosts and changed membership never reuse another lineage", () =>
            {
                Check(CorruptedPartyStore.Load(directory, client, [host, client]) == null);
                Check(CorruptedPartyStore.Load(directory, host, [host, other]) == null);
                Reject(() => CorruptedPartyStore.Commit(directory, client, party, "ArchitectWin", members));
                Check(CorruptedPartyStore.Load(directory, client, [host, client]) == null);
            });
            test("partial terminal parties and capture corruption cannot replace the prior complete party", () =>
            {
                var nextRun = party with { RunId = "run-3" };
                Reject(() => CorruptedPartyStore.Commit(directory, host, nextRun, "ArchitectWin", [members[0]]));
                Reject(() => CorruptedPartyStore.Commit(directory, host, nextRun, "ArchitectWin",
                    [members[0], members[1] with { Snapshot = Snapshot(0) }]));
                Check(CorruptedPartyStore.Load(directory, host, [host, client])!.Revision == 2);
            });
            test("corrupt primary recovers only a validated full backup and preserves unsupported schema", () =>
            {
                var path = Path.Combine(directory, "multiplayer", host.ToString("N"), party.GroupKey + ".json");
                File.WriteAllText(path, "{broken");
                Check(CorruptedPartyStore.Load(directory, host, [host, client])!.Revision == 1);
                File.WriteAllText(path, JsonSerializer.Serialize(lineage with { SchemaVersion = 99 }));
                Reject(() => CorruptedPartyStore.Load(directory, host, [host, client]));
                Reject(() => CorruptedPartyStore.Commit(directory, host, party with { RunId = "run-4" }, "ArchitectWin", members));
                Check(File.ReadAllText(path).Contains("\"SchemaVersion\":99"));
                File.WriteAllText(path, "{broken");
                File.WriteAllText(path + ".backup", "{also broken");
                Reject(() => CorruptedPartyStore.Load(directory, host, [host, client]));
            });
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
