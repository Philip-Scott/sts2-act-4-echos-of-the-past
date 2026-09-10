using System.Text.Json;
using TheArchitect.TheArchitectCode.Multiplayer;
using TheArchitect.TheArchitectCode.Persistence;

internal static class MultiplayerSynchronizationTests
{
    internal static void Run(Action<string, Action> test)
    {
        static void Check(bool condition, string message = "Multiplayer synchronization assertion failed.")
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        foreach (int count in new[] { 2, 3, 4 })
        {
            test($"{count}-player native protocol freezes entire host party before any peer enters", () =>
            {
                var network = new Network(count);
                network.Start();
                Check(network.Peers.Values.All(peer => peer.State.Origin != null && !peer.State.Entered));
                network.HeldAcknowledgements.Add(network.PlayerIds[^1]);
                var tasks = network.Peers.Values.Select(peer => peer.Sync.EnsureEntry()).ToArray();
                network.Pump();
                Check(network.Peers.Values.All(peer => !peer.Sync.IsReady && !peer.State.Entered));
                Check(network.LineageReads == 1 && network.ClientLineageReads == 0);
                network.ReleaseAcknowledgements();
                Check(tasks.All(task => task.IsCompletedSuccessfully));
                Check(network.Peers.Values.All(peer => peer.State.EncounterSnapshots.Count == count));
                Check(network.Peers.Values.Select(peer => peer.State.EntryParty!.Digest).Distinct().Count() == 1);
                Check(network.Peers.Values.All(peer => peer.State.EncounterRevision == 1));
            });
        }
        test("client content rejection blocks every peer without local First Visit fallback", () =>
        {
            var network = new Network(3) { RejectContentFor = 20 };
            network.Start();
            var tasks = network.Peers.Values.Select(peer => peer.Sync.EnsureEntry()).ToArray();
            network.Pump();
            Check(tasks.All(task => task.IsFaulted));
            Check(network.Peers.Values.All(peer => !peer.Sync.IsReady && !peer.State.Entered && peer.Errors.Count > 0));
            Check(network.ClientLineageReads == 0 && network.LineageReads == 1);
        });
        test("host lineage corruption is broadcast and never replaced with local client snapshots", () =>
        {
            var network = new Network(2) { RejectLineageRead = true };
            network.Start();
            var tasks = network.Peers.Values.Select(peer => peer.Sync.EnsureEntry()).ToArray();
            network.Pump();
            Check(tasks.All(task => task.IsFaulted));
            Check(network.Peers.Values.All(peer => !peer.State.Entered && peer.Errors.Count > 0));
            Check(network.ClientLineageReads == 0);
        });
        test("host absence of a lineage synchronizes First Visit even when clients have different data", () =>
        {
            var network = new Network(3) { Lineage = null };
            network.Start();
            var tasks = network.Peers.Values.Select(peer => peer.Sync.EnsureEntry()).ToArray();
            network.Pump();
            Check(tasks.All(task => task.IsCompletedSuccessfully));
            Check(network.Peers.Values.All(peer => peer.State.Entered && peer.State.EncounterSnapshots.Count == 0));
            Check(network.ClientLineageReads == 0);
        });
        test("native reload transfers the saved party without consulting newer host lineage", () =>
        {
            var original = new Network(2);
            original.Start();
            _ = original.Peers[10].Sync.EnsureEntry();
            _ = original.Peers[20].Sync.EnsureEntry();
            original.Pump();
            var restored = new Network(2) { RejectLineageRead = true };
            foreach (var id in original.PlayerIds)
                restored.Replace(id, JsonSerializer.Deserialize<ArchitectRun>(JsonSerializer.Serialize(original.Peers[id].State))!);
            restored.Start();
            var tasks = restored.Peers.Values.Select(peer => peer.Sync.EnsureEntry()).ToArray();
            restored.Pump();
            Check(tasks.All(task => task.IsCompletedSuccessfully));
            Check(restored.LineageReads == 0 && restored.ClientLineageReads == 0);
            Check(restored.Peers.Values.All(peer => peer.State.EntryParty!.Digest == original.Peers[10].State.EntryParty!.Digest));
        });
        test("rejoining one client receives the committed party without reselecting or blocking active peers", () =>
        {
            var network = new Network(3);
            network.Start();
            foreach (var peer in network.Peers.Values) _ = peer.Sync.EnsureEntry();
            network.Pump();
            var frozenDigest = network.Peers[10].State.EntryParty!.Digest;
            var save = JsonSerializer.Deserialize<ArchitectRun>(JsonSerializer.Serialize(network.Peers[20].State))!;
            network.Replace(20, save);
            network.Peers[20].Sync.Start();
            var ready = network.Peers[20].Sync.EnsureEntry();
            network.Pump();
            Check(ready.IsCompletedSuccessfully && network.Peers.Values.All(peer => peer.Sync.IsReady));
            Check(network.Peers[20].State.EntryParty!.Digest == frozenDigest && network.LineageReads == 1);
        });
        test("saving during prepare preserves the selected party without rerolling after reload", () =>
        {
            var original = new Network(2);
            original.Start();
            original.HeldAcknowledgements.Add(20);
            foreach (var peer in original.Peers.Values) _ = peer.Sync.EnsureEntry();
            original.Pump();
            var digest = original.Peers[10].State.PendingParty!.Digest;
            Check(original.Peers.Values.All(peer => !peer.State.Entered && peer.State.PendingParty != null));
            var restored = new Network(2) { RejectLineageRead = true };
            foreach (var id in original.PlayerIds)
                restored.Replace(id, JsonSerializer.Deserialize<ArchitectRun>(JsonSerializer.Serialize(original.Peers[id].State))!);
            restored.Start();
            var tasks = restored.Peers.Values.Select(peer => peer.Sync.EnsureEntry()).ToArray();
            restored.Pump();
            Check(tasks.All(task => task.IsCompletedSuccessfully));
            Check(restored.LineageReads == 0 && restored.Peers.Values.All(peer => peer.State.EntryParty!.Digest == digest));
        });
        test("original host and participant profile identities cannot change after native saves", () =>
        {
            var network = new Network(2);
            network.Start();
            var save = JsonSerializer.Deserialize<ArchitectRun>(JsonSerializer.Serialize(network.Peers[20].State))!;
            network.Replace(20, save, profile: Guid.NewGuid());
            network.Peers[20].Sync.Start();
            network.Pump();
            Check(network.Peers.Values.All(peer => !peer.Sync.IsReady && peer.Errors.Count > 0));
            Check(network.LineageReads == 0);
            var migrated = new Network(2);
            migrated.Replace(20, save, host: 20);
            migrated.Peers[20].Sync.Start();
            Check(migrated.Peers[20].Errors.Count == 1 && !migrated.Peers[20].Sync.IsReady);
        });
        test("a client cannot author a frozen party or acknowledge different content", () =>
        {
            var network = new Network(2);
            network.Start();
            network.Peers[10].Sync.Receive(new ArchitectPartyPacket
            {
                Kind = ArchitectPartyMessageKind.Prepare, Seed = 123, Payload = "{}"
            }, 20);
            network.Pump();
            Check(network.Peers.Values.All(peer => peer.Errors.Count > 0 && !peer.State.Entered));
            var mismatched = new Network(2);
            mismatched.Start();
            mismatched.HeldAcknowledgements.Add(20);
            _ = mismatched.Peers[10].Sync.EnsureEntry();
            _ = mismatched.Peers[20].Sync.EnsureEntry();
            mismatched.Pump();
            mismatched.Peers[10].Sync.Receive(new ArchitectPartyPacket
            {
                Kind = ArchitectPartyMessageKind.Acknowledge, Seed = 123, Digest = new string('0', 64)
            }, 20);
            mismatched.Pump();
            Check(mismatched.Peers.Values.All(peer => peer.Errors.Count > 0 && !peer.State.Entered));
        });
        test("large whole-party payloads use bounded reliable chunks and keep exact custom card data", () =>
        {
            var network = new Network(4);
            var members = network.Lineage!.Members.Select(member =>
            {
                var card = JsonSerializer.SerializeToElement(new { id = "CARD.STRIKE_IRONCLAD", custom = new string('x', 45000) });
                var snapshot = new CorruptedPlayerSnapshot("CHARACTER.IRONCLAD", 80, [card],
                    CorruptedPlayerSnapshot.Hash("CHARACTER.IRONCLAD", 80, [card]));
                return member with { Snapshot = snapshot };
            }).ToArray();
            network.Lineage = network.Lineage with { Members = members };
            network.Start();
            var tasks = network.Peers.Values.Select(peer => peer.Sync.EnsureEntry()).ToArray();
            network.Pump();
            Check(tasks.All(task => task.IsCompletedSuccessfully));
            Check(network.LargestPacket <= PartyPayloadBuffer.ChunkSize && network.PrepareChunks > 4);
            Check(network.Peers.Values.All(peer => peer.State.EncounterSnapshots[3].Deck[0].GetProperty("custom").GetString()!.Length == 45000));
        });
    }

    private sealed record Peer(ArchitectRun State, PartySynchronizer Sync, List<string> Errors);

    private sealed class Network
    {
        internal readonly Dictionary<ulong, Peer> Peers = [];
        internal readonly ulong[] PlayerIds;
        internal readonly HashSet<ulong> HeldAcknowledgements = [];
        private readonly Queue<(ulong Sender, ulong Target, ArchitectPartyPacket Message)> messages = [];
        private readonly List<(ulong Sender, ulong Target, ArchitectPartyPacket Message)> held = [];
        internal CorruptedPartyEnvelope? Lineage;
        internal int LineageReads, ClientLineageReads, LargestPacket, PrepareChunks;
        internal ulong? RejectContentFor;
        internal bool RejectLineageRead;

        private static Guid Profile(ulong id) => Guid.Parse($"{id:x8}-0000-0000-0000-000000000000");

        internal Network(int count)
        {
            PlayerIds = Enumerable.Range(1, count).Select(index => (ulong)index * 10).ToArray();
            var card = JsonSerializer.Deserialize<JsonElement>("""{"id":"CARD.STRIKE_IRONCLAD"}""");
            var members = PlayerIds.Select(id => new CorruptedPartyMember(Profile(id),
                new("CHARACTER.IRONCLAD", (int)(80 + id), [card],
                    CorruptedPlayerSnapshot.Hash("CHARACTER.IRONCLAD", (int)(80 + id), [card])))).ToArray();
            Lineage = new CorruptedPartyEnvelope
            {
                HostProfileUuid = Profile(10), GroupKey = SuccessorGroup.Key(PlayerIds.Select(Profile)),
                Revision = 1, TerminalRunId = "prior", Outcome = "ArchitectWin", Members = members
            };
            foreach (var id in PlayerIds) Replace(id, new ArchitectRun());
        }

        internal void Replace(ulong id, ArchitectRun state, Guid? profile = null, ulong host = 10)
        {
            var errors = new List<string>();
            var environment = new PartySyncEnvironment(state, PlayerIds, id, host, 123, state.Entered,
                () => profile ?? Profile(id),
                (_, _) =>
                {
                    if (id != 10)
                    {
                        ClientLineageReads++;
                        throw new InvalidOperationException("Clients must not consult local successor data.");
                    }
                    LineageReads++;
                    return RejectLineageRead ? throw new JsonException("Corrupt host lineage.") : Lineage;
                },
                party =>
                {
                    if (RejectContentFor == id && party.Lineage != null)
                        throw new JsonException("Required native character/card model is missing.");
                },
                (packet, target) => Send(id, packet, target), errors.Add, () => true);
            Peers[id] = new Peer(state, new PartySynchronizer(environment), errors);
        }

        internal void Start()
        {
            foreach (var peer in Peers.Values) peer.Sync.Start();
            Pump();
        }

        private void Send(ulong sender, ArchitectPartyPacket packet, ulong? target)
        {
            LargestPacket = Math.Max(LargestPacket, packet.Payload.Length);
            if (packet.Kind == ArchitectPartyMessageKind.Prepare) PrepareChunks++;
            foreach (var recipient in target is { } peer ? [peer] :
                         sender == 10 ? PlayerIds.Where(id => id != sender).ToArray() : new ulong[] { 10 })
            {
                var delivery = (sender, recipient, packet);
                if (packet.Kind == ArchitectPartyMessageKind.Acknowledge && HeldAcknowledgements.Contains(sender))
                    held.Add(delivery);
                else
                    messages.Enqueue(delivery);
            }
        }

        internal void ReleaseAcknowledgements()
        {
            HeldAcknowledgements.Clear();
            foreach (var delivery in held) messages.Enqueue(delivery);
            held.Clear();
            Pump();
        }

        internal void Pump()
        {
            int count = 0;
            while (messages.TryDequeue(out var delivery))
            {
                if (++count > 10000) throw new InvalidOperationException("Architect protocol entered a message loop.");
                Peers[delivery.Target].Sync.Receive(delivery.Message, delivery.Sender);
            }
        }
    }
}
