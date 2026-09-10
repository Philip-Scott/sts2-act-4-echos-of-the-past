using System.IO;
using System.Text.Json;
using TheArchitect.TheArchitectCode.Multiplayer;
using TheArchitect.TheArchitectCode.Persistence;

internal static class LobbyPreviewTests
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (int count in new[] { 2, 3, 4 })
        {
            test($"{count}-player lobby preview reads only the exact host-owned group without a run", () =>
            {
                var network = new Network(count);
                network.Start();
                Check(network.Peers.Values.All(peer => peer.Sync.Party?.Lineage?.Members.Length == count));
                Check(network.Peers.Values.All(peer => !peer.Sync.IsLoading && peer.Sync.Error == null));
                Check(network.Peers.Values.Select(peer => peer.Sync.Party!.Digest).Distinct().Count() == 1);
                Check(network.HostReads == 1 && network.ClientReads == 0);
                Check(network.LastHost == Profile(10));
                Check(network.LastProfiles.Order().SequenceEqual(network.Members.Select(Profile).Order()));
                Check(network.Peers.Values.All(peer => peer.Changes > 1));
            });
        }
        test("lobby First Visit is an explicit host result and never client-local fallback", () =>
        {
            var network = new Network(3) { FirstVisit = true };
            network.Start();
            Check(network.Peers.Values.All(peer => peer.Sync.Party is { Lineage: null } &&
                peer.Sync.Status.Contains("First Visit") && peer.Sync.Error == null && !peer.Sync.IsLoading));
            Check(network.HostReads == 1 && network.ClientReads == 0);
        });
        test("host-only lobby waits without constructing a successor group or reading a profile", () =>
        {
            var network = new Network(1);
            network.Start();
            Check(network.Peers[10].Sync.Party == null && network.Peers[10].Sync.Error == null);
            Check(!network.Peers[10].Sync.IsLoading && network.Peers[10].Sync.Status.Contains("Waiting"));
            Check(network.HostReads == 0 && network.ProfileReads == 0 && network.Sent.Count == 0);
            network.Peers[10].Sync.Tick(100);
            Check(network.Peers[10].Sync.Error == null);
        });
        test("membership changes immediately clear every old preview before a new group is read", () =>
        {
            var network = new Network(3);
            network.Start();
            var previous = network.Peers[10].Sync.Party!.GroupKey;
            network.Members = [10, 20];
            foreach (var id in network.Members)
            {
                network.Peers[id].Sync.Refresh(network.Members);
                Check(network.Peers[id].Sync.Party == null && network.Peers[id].Sync.IsLoading);
            }
            network.Pump();
            Check(network.Members.All(id => network.Peers[id].Sync.Party?.Participants.Length == 2));
            Check(network.Peers[10].Sync.Party!.GroupKey != previous);
        });
        test("client-ready request recovers a host announcement sent before client handlers", () =>
        {
            var network = new Network(2);
            network.Peers[10].Sync.Refresh(network.Members);
            network.Pump();
            Check(network.HostReads == 0);
            network.Peers[20].Sync.Refresh(network.Members);
            network.Pump();
            Check(network.Peers.Values.All(peer => peer.Sync.Party != null));
        });
        test("host-ready announcement recovers a client request sent before host handlers", () =>
        {
            var network = new Network(2);
            network.Peers[20].Sync.Refresh(network.Members);
            network.Pump();
            network.Peers[10].Sync.Refresh(network.Members);
            network.Pump();
            Check(network.Peers.Values.All(peer => peer.Sync.Party != null));
        });
        test("client retries an unanswered refresh without changing its request identity", () =>
        {
            var network = new Network(2);
            network.Peers[10].Sync.Refresh(network.Members);
            network.Pending.Clear();
            network.Peers[20].Sync.Refresh(network.Members);
            var first = network.Pending.Single().Packet.RequestId;
            network.Pending.Clear();
            network.Peers[20].Sync.Tick(3);
            Check(network.Pending.Single().Packet.RequestId == first);
            network.Pump();
            Check(network.Peers.Values.All(peer => peer.Sync.Party != null));
        });
        test("same-membership refresh starts a new preview and ignores retired requests and snapshots", () =>
        {
            var network = new Network(2);
            network.Start();
            var oldPackets = network.Sent.ToArray();
            var oldParty = network.Peers[20].Sync.Party!;
            network.Revision = 2;
            network.Peers[20].Sync.Refresh(network.Members);
            Check(network.Peers[20].Sync.Party == null);
            network.Pump();
            var fresh = network.Peers[20].Sync.Party!;
            Check(fresh.RunId != oldParty.RunId && fresh.Lineage!.Revision == 2);
            foreach (var packet in oldPackets) network.Deliver(packet);
            network.Pump();
            Check(network.Peers.Values.All(peer => peer.Sync.Party?.Digest == fresh.Digest && peer.Sync.Error == null));
            Check(network.HostReads == 2);
        });
        test("leave and rejoin never reuse a previous participant's profile or transfer", () =>
        {
            var network = new Network(2);
            network.Start();
            var oldPackets = network.Sent.ToArray();
            var oldGroup = network.Peers[10].Sync.Party!.GroupKey;
            network.Members = [10];
            network.Peers[10].Sync.Refresh(network.Members);
            Check(network.Peers[10].Sync.Party == null && !network.Peers[10].Sync.IsLoading);
            network.Members = [10, 20];
            network.Profiles[20] = Guid.NewGuid();
            network.Replace(20);
            network.Start();
            var fresh = network.Peers[10].Sync.Party!;
            Check(fresh.GroupKey != oldGroup);
            foreach (var packet in oldPackets) network.Deliver(packet);
            network.Pump();
            Check(network.Peers.Values.All(peer => peer.Sync.Party?.Digest == fresh.Digest && peer.Sync.Error == null));
        });
        test("a never-observed older refresh request cannot roll back a newer completed request", () =>
        {
            var network = new Network(2);
            network.Start();
            network.Peers[20].Sync.Refresh(network.Members);
            var delayedRequest = network.Pending.Dequeue();
            network.Peers[20].Sync.Refresh(network.Members);
            network.Pump();
            var fresh = network.Peers[10].Sync.Party!.Digest;
            var reads = network.HostReads;
            network.Deliver(delayedRequest);
            network.Pump();
            Check(network.Peers.Values.All(peer => peer.Sync.Party?.Digest == fresh && peer.Sync.Error == null));
            Check(network.HostReads == reads);
        });
        test("host-only refresh cannot be rolled back by delayed old identity challenges", () =>
        {
            var network = new Network(2);
            network.Start();
            var old = network.Sent.Last(item => item.Target == 20 &&
                item.Packet.Kind == LobbyPreviewMessageKind.IdentityRequest && item.Packet.RequestId != "");
            network.Peers[10].Sync.Refresh(network.Members);
            network.Pump();
            var fresh = network.Peers[20].Sync.Party!;
            network.Deliver(old);
            network.Pump();
            Check(network.Peers[20].Sync.Party?.Digest == fresh.Digest && network.HostReads == 2);
        });
        test("nonmembers and nonhost peers cannot replace or poison a client's lobby preview", () =>
        {
            var network = new Network(3);
            network.Start();
            var snapshot = network.Sent.Last(item => item.Target == 20 && item.Packet.Kind == LobbyPreviewMessageKind.Snapshot);
            var before = network.Peers[20].Sync.Party!.Digest;
            network.Deliver(snapshot with { Sender = 30, Packet = snapshot.Packet with { Payload = "forged" } });
            network.Deliver(snapshot with { Sender = 99, Packet = snapshot.Packet with { Payload = "forged" } });
            network.Deliver(snapshot with { Packet = snapshot.Packet with { MemberNetIds = [10, 20] } });
            Check(network.Peers[20].Sync.Party?.Digest == before && network.Peers[20].Sync.Error == null);
        });
        test("a client cannot author host preview data even with current handshake tokens", () =>
        {
            var network = new Network(2);
            network.Start();
            var identity = network.Sent.Last(item => item.Sender == 20 && item.Packet.Kind == LobbyPreviewMessageKind.Identity);
            network.Deliver(identity with { Packet = identity.Packet with { Kind = LobbyPreviewMessageKind.Snapshot } });
            network.Pump();
            Check(network.Peers.Values.All(peer => peer.Sync.Party == null && peer.Sync.Error != null));
        });
        test("client validates its exact persistent profile and the snapshot request epoch", () =>
        {
            foreach (bool changeProfile in new[] { true, false })
            {
                var network = new Network(2);
                network.Start(holdSnapshots: true);
                var template = network.Held.Single();
                var party = network.Peers[10].Sync.Party! with { Lineage = null };
                if (changeProfile)
                {
                    var players = party.Participants.Select(player => player.NetId == 20
                        ? player with { ProfileUuid = Guid.NewGuid() } : player).ToArray();
                    party = party with { Participants = players, GroupKey = SuccessorGroup.Key(players.Select(player => player.ProfileUuid)) };
                }
                else
                    party = party with { RunId = "a-different-preview" };
                network.Deliver(template with { Packet = template.Packet with
                {
                    Payload = JsonSerializer.Serialize(party), Digest = party.Digest
                } });
                Check(network.Peers[20].Sync.Party == null && network.Peers[20].Sync.Error != null);
                Check(network.ClientReads == 0);
            }
        });
        test("duplicate persistent profiles fail the exact-group handshake before any lineage read", () =>
        {
            var network = new Network(2);
            network.Profiles[20] = Profile(10);
            network.Start();
            Check(network.HostReads == 0 && network.Peers.Values.All(peer => peer.Sync.Error != null && peer.Sync.Party == null));
        });
        test("host lineage read failures surface on all previews without consulting client data", () =>
        {
            var network = new Network(2) { ReadError = new JsonException("Corrupt host lineage.") };
            network.Start();
            Check(network.Peers.Values.All(peer => peer.Sync.Party == null && peer.Sync.Error!.Contains("Corrupt host lineage")));
            Check(network.HostReads == 1 && network.ClientReads == 0);
            network.ReadError = null;
            network.Peers[20].Sync.Refresh(network.Members);
            Check(network.Peers[20].Sync.Error == null && network.Peers[20].Sync.IsLoading);
            network.Pump();
            Check(network.Peers.Values.All(peer => peer.Sync.Party != null && peer.Sync.Error == null));
        });
        test("host rejects a lineage owned by another profile rather than displaying it", () =>
        {
            var network = new Network(2) { WrongLineageOwner = true };
            network.Start();
            Check(network.Peers.Values.All(peer => peer.Sync.Error != null && peer.Sync.Party == null));
            Check(network.ClientReads == 0);
        });
        test("large lobby deck transfers remain hidden until all bounded chunks validate", () =>
        {
            var network = new Network(4) { CardDataSize = 45000 };
            network.Start(holdSnapshots: true);
            Check(network.Held.Count > 4 && network.Sent.All(item => item.Packet.Payload.Length <= PartyPayloadBuffer.ChunkSize));
            Check(network.Peers.Where(item => item.Key != 10).All(item => item.Value.Sync.Party == null && item.Value.Sync.IsLoading));
            foreach (var delivery in network.Held.AsEnumerable().Reverse())
            {
                network.Deliver(delivery);
                network.Deliver(delivery);
            }
            Check(network.Peers.Values.All(peer => peer.Sync.Party?.Lineage?.Members.Length == 4 && peer.Sync.Error == null));
            Check(network.Peers[20].Sync.Party!.Lineage!.Members[3].Snapshot.Deck[0].GetProperty("custom").GetString()!.Length == 45000);
            Check(network.Peers.Values.Select(peer => peer.Sync.Party!.Digest).Distinct().Count() == 1);
        });
        test("conflicting chunk bodies digests and counts reject the entire preview", () =>
        {
            foreach (int conflict in new[] { 0, 1, 2 })
            {
                var network = new Network(2) { CardDataSize = 20000 };
                network.Start(holdSnapshots: true);
                var first = network.Held.First();
                network.Deliver(first);
                var changed = conflict switch
                {
                    0 => first.Packet with { Payload = first.Packet.Payload[..^1] + "!" },
                    1 => first.Packet with { Digest = new string('0', 64) },
                    _ => first.Packet with { Count = first.Packet.Count + 1 }
                };
                network.Deliver(first with { Packet = changed });
                foreach (var delivery in network.Held) network.Deliver(delivery);
                Check(network.Peers[20].Sync.Party == null && network.Peers[20].Sync.Error != null);
            }
        });
        test("refresh during a partial transfer discards its chunks and ignores delayed old fragments", () =>
        {
            var network = new Network(2) { CardDataSize = 20000 };
            network.Start(holdSnapshots: true);
            var oldPackets = network.Held.ToArray();
            network.Deliver(oldPackets[0]);
            network.Revision = 2;
            network.Peers[20].Sync.Refresh(network.Members);
            network.Held.Clear();
            network.Pump(holdSnapshots: true);
            foreach (var delivery in oldPackets) network.Deliver(delivery);
            Check(network.Peers[20].Sync.Party == null && network.Peers[20].Sync.Error == null);
            foreach (var delivery in network.Held) network.Deliver(delivery);
            Check(network.Peers[20].Sync.Party?.Lineage?.Revision == 2 && network.Peers[20].Sync.Error == null);
        });
        test("invalid chunk bounds and hashes never publish a lobby preview", () =>
        {
            foreach (int invalid in new[] { 0, 1, 2, 3 })
            {
                var network = new Network(2);
                network.Start(holdSnapshots: true);
                var delivery = network.Held.Single();
                var packet = invalid switch
                {
                    0 => delivery.Packet with { Index = -1 },
                    1 => delivery.Packet with { Count = PartyPayloadBuffer.MaximumChunks + 1 },
                    2 => delivery.Packet with { Payload = new string('x', PartyPayloadBuffer.ChunkSize + 1) },
                    _ => delivery.Packet with { Digest = new string('0', 64) }
                };
                network.Deliver(delivery with { Packet = packet });
                Check(network.Peers[20].Sync.Error != null && network.Peers[20].Sync.Party == null);
            }
        });
        test("incomplete transfers time out without exposing a partial or First Visit preview", () =>
        {
            var network = new Network(2) { CardDataSize = 20000 };
            network.Start(holdSnapshots: true);
            network.Deliver(network.Held.First());
            network.Peers[20].Sync.Tick(LobbyPreviewSynchronizer.TimeoutSeconds);
            Check(network.Peers[20].Sync.Party == null && network.Peers[20].Sync.Error!.Contains("Timed out"));
            foreach (var delivery in network.Held) network.Deliver(delivery);
            Check(network.Peers[20].Sync.Party == null);
            Check(network.Peers[10].Sync.Party != null);
            network.Peers[20].Sync.Refresh(network.Members);
            network.Pump();
            Check(network.Peers[20].Sync.Party != null && network.Peers[20].Sync.Error == null);
        });
        test("missing identity handshake times out and remains a preview-only error", () =>
        {
            var network = new Network(2);
            network.Peers[10].Sync.Refresh(network.Members);
            network.Peers[10].Sync.Tick(LobbyPreviewSynchronizer.TimeoutSeconds);
            Check(network.Peers[10].Sync.Error!.Contains("Timed out") && network.Peers[10].Sync.Party == null);
            Check(network.HostReads == 0);
        });
        test("transport failures including failed failure notifications surface without throwing", () =>
        {
            var network = new Network(2);
            network.Start();
            network.SendErrorFor = 10;
            network.Peers[10].Sync.Refresh(network.Members);
            Check(network.Peers[10].Sync.Party == null && network.Peers[10].Sync.Error!.Contains("transport"));
            Check(network.Peers[10].Sync.Error!.Contains("notify"));
            network.SendErrorFor = 20;
            network.Peers[20].Sync.Refresh(network.Members);
            Check(network.Peers[20].Sync.Party == null && network.Peers[20].Sync.Error!.Contains("transport"));
            network.SendErrorFor = null;
            network.Peers[20].Sync.Refresh(network.Members);
            network.Pump();
            Check(network.Peers.Values.All(peer => peer.Sync.Party != null && peer.Sync.Error == null));
        });
        test("preview callback failures are visible in state and cannot silently report success", () =>
        {
            var sync = new LobbyPreviewSynchronizer(new LobbyPreviewEnvironment(10, 10, () => Profile(10),
                (_, _) => throw new InvalidOperationException("Should not read after callback failure."),
                (_, _) => throw new InvalidOperationException("Should not send after callback failure."),
                () => throw new IOException("UI callback unavailable.")));
            sync.Refresh([10, 20]);
            Check(sync.Party == null && !sync.IsLoading && sync.Error!.Contains("UI callback unavailable"));
        });
        test("invalid lobby membership is rejected and a subsequent refresh clears its error", () =>
        {
            var network = new Network(2);
            network.Peers[10].Sync.Refresh([10, 10]);
            Check(network.Peers[10].Sync.Error != null && network.Peers[10].Sync.Party == null);
            network.Start();
            Check(network.Peers.Values.All(peer => peer.Sync.Party != null && peer.Sync.Error == null));
        });
    }

    private static void Check(bool condition, string message = "Lobby preview assertion failed.")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static Guid Profile(ulong id) => Guid.Parse($"{id:x8}-0000-0000-0000-000000000000");
    private sealed record Delivery(ulong Sender, ulong Target, LobbyPreviewPacket Packet);
    private sealed class Peer
    {
        internal LobbyPreviewSynchronizer Sync = null!;
        internal int Changes;
    }

    private sealed class Network
    {
        internal readonly Dictionary<ulong, Peer> Peers = [];
        internal readonly Dictionary<ulong, Guid> Profiles = [];
        internal readonly Queue<Delivery> Pending = [];
        internal readonly List<Delivery> Sent = [], Held = [];
        internal ulong[] Members;
        internal bool FirstVisit, WrongLineageOwner;
        internal int HostReads, ClientReads, ProfileReads, CardDataSize;
        internal long Revision = 1;
        internal Guid LastHost;
        internal Guid[] LastProfiles = [];
        internal Exception? ReadError;
        internal ulong? SendErrorFor;

        internal Network(int count)
        {
            Members = Enumerable.Range(1, count).Select(index => (ulong)index * 10).ToArray();
            foreach (var id in Members)
            {
                Profiles[id] = Profile(id);
                Replace(id);
            }
        }

        internal void Replace(ulong id)
        {
            var peer = new Peer();
            peer.Sync = new LobbyPreviewSynchronizer(new LobbyPreviewEnvironment(id, 10,
                () => { ProfileReads++; return Profiles[id]; },
                (host, profiles) =>
                {
                    if (id != 10)
                    {
                        ClientReads++;
                        throw new InvalidOperationException("A client must never read local successor data.");
                    }
                    HostReads++;
                    LastHost = host;
                    LastProfiles = profiles;
                    if (ReadError != null) throw ReadError;
                    if (FirstVisit) return null;
                    var card = JsonSerializer.SerializeToElement(new { id = "CARD.STRIKE_IRONCLAD", custom = new string('x', CardDataSize) });
                    var snapshot = new CorruptedPlayerSnapshot("CHARACTER.IRONCLAD", 80, [card],
                        CorruptedPlayerSnapshot.Hash("CHARACTER.IRONCLAD", 80, [card]));
                    return new CorruptedPartyEnvelope
                    {
                        HostProfileUuid = WrongLineageOwner ? Guid.NewGuid() : host,
                        GroupKey = SuccessorGroup.Key(profiles), Revision = Revision,
                        TerminalRunId = "previous-completed-run", Outcome = "ArchitectWin",
                        Members = profiles.Select(profile => new CorruptedPartyMember(profile, snapshot)).ToArray()
                    };
                },
                (packet, target) =>
                {
                    if (SendErrorFor == id) throw new IOException("Preview transport unavailable.");
                    foreach (var recipient in target is { } single ? [single] : Members.Where(member => member != id))
                    {
                        var delivery = new Delivery(id, recipient, packet);
                        Sent.Add(delivery);
                        Pending.Enqueue(delivery);
                    }
                },
                () => peer.Changes++));
            Peers[id] = peer;
        }

        internal void Start(bool holdSnapshots = false)
        {
            foreach (var id in Members) Peers[id].Sync.Refresh(Members);
            Pump(holdSnapshots);
        }

        internal void Deliver(Delivery delivery)
        {
            if (Peers.TryGetValue(delivery.Target, out var peer))
                peer.Sync.Receive(delivery.Packet, delivery.Sender);
        }

        internal void Pump(bool holdSnapshots = false)
        {
            int deliveries = 0;
            while (Pending.TryDequeue(out var delivery))
            {
                if (++deliveries > 10000) throw new InvalidOperationException("Lobby preview message loop.");
                if (holdSnapshots && delivery.Packet.Kind == LobbyPreviewMessageKind.Snapshot)
                    Held.Add(delivery);
                else
                    Deliver(delivery);
            }
        }
    }
}
