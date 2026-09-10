using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using TheArchitect.TheArchitectCode.Persistence;

namespace TheArchitect.TheArchitectCode.Multiplayer;

public enum LobbyPreviewMessageKind { RefreshRequest, IdentityRequest, Identity, Snapshot, Failure }

public sealed record LobbyPreviewPacket
{
    public LobbyPreviewMessageKind Kind { get; set; }
    public string RequestId { get; set; } = "";
    public string Epoch { get; set; } = "";
    public ulong[] MemberNetIds { get; set; } = [];
    public string Payload { get; set; } = "";
    public string Digest { get; set; } = "";
    public int Index { get; set; }
    public int Count { get; set; } = 1;
}

internal sealed record LobbyPreviewEnvironment(
    ulong LocalNetId,
    ulong HostNetId,
    Func<Guid> GetProfileUuid,
    Func<Guid, Guid[], CorruptedPartyEnvelope?> LoadLineage,
    Action<LobbyPreviewPacket, ulong?> Send,
    Action Changed);

internal sealed class LobbyPreviewSynchronizer(LobbyPreviewEnvironment environment)
{
    internal const double TimeoutSeconds = 30;
    private readonly string hostInstance = Guid.NewGuid().ToString("N");
    private readonly Dictionary<ulong, string> requests = [];
    private readonly HashSet<(ulong Member, string Request)> retiredRequests = [];
    private readonly Dictionary<ulong, Guid> identities = [];
    private ulong[] members = [];
    private PartyPayloadBuffer payload = new();
    private Guid localProfile;
    private string requestId = "";
    private string epoch = "";
    private long generation, requestGeneration;
    private double elapsed, retryElapsed;

    internal FrozenCorruptedParty? Party { get; private set; }
    internal bool IsLoading { get; private set; }
    internal string Status { get; private set; } = "Open the preview to load the host's Corrupted Party.";
    internal string? Error { get; private set; }
    private bool IsHost => environment.LocalNetId == environment.HostNetId;

    internal void Refresh(ulong[] memberNetIds)
    {
        var next = memberNetIds.Order().ToArray();
        if (!members.SequenceEqual(next))
        {
            foreach (var request in requests)
                retiredRequests.Add((request.Key, request.Value));
            requests.Clear();
        }
        members = next;
        requestId = hostInstance + ":" + (++requestGeneration).ToString(CultureInfo.InvariantCulture);
        epoch = "";
        Reset();
        if (Error != null)
            return;
        Guard(() =>
        {
            if (members.Length is < 1 or > 4 || members.Distinct().Count() != members.Length ||
                !members.Contains(environment.LocalNetId) || !members.Contains(environment.HostNetId))
                throw new JsonException("The lobby must contain its host and one to four distinct participants.");
            if (members.Length == 1)
            {
                IsLoading = false;
                Status = "Waiting for other participants. Multiplayer lineage requires two to four profiles.";
                Notify();
                return;
            }
            ReadLocalProfile();
            if (IsHost)
                BeginHostEpoch();
            else
                Send(LobbyPreviewMessageKind.RefreshRequest, environment.HostNetId);
        });
    }

    internal void Receive(LobbyPreviewPacket packet, ulong sender)
    {
        if (members.Length < 2 || !members.Contains(sender) || sender == environment.LocalNetId ||
            (!IsHost && sender != environment.HostNetId))
            return;
        Guard(() =>
        {
            if (packet.MemberNetIds == null || !packet.MemberNetIds.Order().SequenceEqual(members))
                return;
            if (IsHost && packet.Kind == LobbyPreviewMessageKind.RefreshRequest)
            {
                ValidatePacket(packet);
                if (!ParseToken(packet.RequestId, out var nextRequest))
                    throw new JsonException("The preview request has no valid identity token.");
                if (retiredRequests.Contains((sender, packet.RequestId)))
                    return;
                if (!requests.TryGetValue(sender, out var previous) || previous != packet.RequestId)
                {
                    if (previous != null && previous[..32] == packet.RequestId[..32] &&
                        ParseToken(previous, out var priorRequest) && nextRequest <= priorRequest)
                        return;
                    if (previous != null)
                        retiredRequests.Add((sender, previous));
                    requests[sender] = packet.RequestId;
                    BeginHostEpoch();
                }
                else if (Error != null)
                    Send(LobbyPreviewMessageKind.Failure, sender, Error);
                else if (Party != null)
                {
                    Send(LobbyPreviewMessageKind.IdentityRequest, sender);
                    SendParty(Party, sender);
                }
                else
                    Send(LobbyPreviewMessageKind.IdentityRequest, sender);
                return;
            }
            if (IsHost)
            {
                if (packet.Epoch != epoch || !requests.TryGetValue(sender, out var expected) ||
                    packet.RequestId != expected || Error != null)
                    return;
                ValidatePacket(packet);
                if (packet.Kind != LobbyPreviewMessageKind.Identity)
                    throw new JsonException("A client attempted to supply host-owned preview data.");
                var profile = Guid.ParseExact(packet.Payload, "N");
                if (profile == Guid.Empty || identities.TryGetValue(sender, out var previous) && previous != profile)
                    throw new JsonException("A participant's persistent profile identity changed during preview.");
                identities[sender] = profile;
                TryLoadParty();
                return;
            }
            if (packet.Kind == LobbyPreviewMessageKind.IdentityRequest && packet.RequestId == "")
            {
                // A host may install its handlers after a client's first request was sent.
                ValidatePacket(packet);
                Send(LobbyPreviewMessageKind.RefreshRequest, environment.HostNetId);
                return;
            }
            if (packet.RequestId != requestId)
                return;
            if (packet.Kind is LobbyPreviewMessageKind.IdentityRequest or LobbyPreviewMessageKind.Failure)
            {
                if (!AcceptEpoch(packet.Epoch))
                    return;
            }
            else if (packet.Epoch != epoch || epoch == "")
                return;
            ValidatePacket(packet);
            if (Error != null)
                return;
            switch (packet.Kind)
            {
                case LobbyPreviewMessageKind.IdentityRequest:
                    Send(LobbyPreviewMessageKind.Identity, environment.HostNetId, localProfile.ToString("N"));
                    break;
                case LobbyPreviewMessageKind.Snapshot:
                    ReceiveParty(packet);
                    break;
                case LobbyPreviewMessageKind.Failure:
                    Fail(string.IsNullOrWhiteSpace(packet.Payload) ? "The host could not load this preview." : packet.Payload);
                    break;
                default:
                    throw new JsonException("Invalid host lobby preview message.");
            }
        });
    }

    internal void Tick(double deltaSeconds)
    {
        if (!IsLoading || !double.IsFinite(deltaSeconds) || deltaSeconds <= 0)
            return;
        elapsed += deltaSeconds;
        retryElapsed += deltaSeconds;
        if (elapsed >= TimeoutSeconds)
        {
            Fail("Timed out loading the lobby preview. All participants must be connected with compatible mod versions.");
            return;
        }
        if (!IsHost && retryElapsed >= 3)
        {
            retryElapsed = 0;
            Guard(() => Send(LobbyPreviewMessageKind.RefreshRequest, environment.HostNetId));
        }
    }

    private void BeginHostEpoch()
    {
        epoch = hostInstance + ":" + (++generation).ToString(CultureInfo.InvariantCulture);
        Reset();
        if (Error != null)
            return;
        ReadLocalProfile();
        identities.Clear();
        identities[environment.LocalNetId] = localProfile;
        foreach (var member in members.Where(id => id != environment.LocalNetId))
            Send(LobbyPreviewMessageKind.IdentityRequest, member);
    }

    private void ReadLocalProfile()
    {
        localProfile = environment.GetProfileUuid();
        if (localProfile == Guid.Empty)
            throw new JsonException("The active profile has no persistent identity.");
    }

    private bool AcceptEpoch(string candidate)
    {
        if (candidate == null || !ParseToken(candidate, out var next))
            throw new JsonException("Invalid lobby preview epoch.");
        if (candidate == epoch)
            return true;
        if (epoch != "" && (candidate[..32] != epoch[..32] || !ParseToken(epoch, out var current) || next <= current))
            return false;
        epoch = candidate;
        Reset();
        return Error == null;
    }

    private static bool ParseToken(string value, out long version)
    {
        version = 0;
        return value.Length > 33 && value[32] == ':' &&
            Guid.TryParseExact(value[..32], "N", out var instance) && instance != Guid.Empty &&
            long.TryParse(value[33..], NumberStyles.None, CultureInfo.InvariantCulture, out version) && version > 0;
    }

    private void TryLoadParty()
    {
        if (Party != null || identities.Count != members.Length)
            return;
        var profiles = members.Select(id => identities[id]).ToArray();
        var party = new FrozenCorruptedParty
        {
            RunId = epoch,
            HostNetId = environment.HostNetId,
            HostProfileUuid = localProfile,
            GroupKey = SuccessorGroup.Key(profiles),
            Participants = members.Select(id => new SuccessorParticipant(id, identities[id])).ToArray(),
            Lineage = environment.LoadLineage(localProfile, profiles)
        };
        ValidateParty(party);
        foreach (var member in members.Where(id => id != environment.LocalNetId))
            SendParty(party, member);
        Complete(party);
    }

    private void ReceiveParty(LobbyPreviewPacket packet)
    {
        var json = payload.Add(packet.Digest, packet.Index, packet.Count, packet.Payload);
        if (json == null)
            return;
        var party = JsonSerializer.Deserialize<FrozenCorruptedParty>(json)
            ?? throw new JsonException("The host supplied an empty lobby preview.");
        ValidateParty(party);
        if (party.Digest != packet.Digest)
            throw new JsonException("The lobby preview content hash does not match.");
        if (Party == null)
            Complete(party);
    }

    private void ValidateParty(FrozenCorruptedParty party)
    {
        party.Validate(members, environment.HostNetId);
        if (party.RunId != epoch ||
            party.Participants.Single(player => player.NetId == environment.LocalNetId).ProfileUuid != localProfile)
            throw new JsonException("The lobby preview does not match this request and active profile.");
    }

    private void Complete(FrozenCorruptedParty party)
    {
        Party = party;
        IsLoading = false;
        Status = party.Lineage == null
            ? "First Visit: the host has no Corrupted Party lineage for this exact group."
            : $"Corrupted Party - {party.Participants.Length} participants, revision {party.Lineage.Revision}.";
        Notify();
    }

    private void SendParty(FrozenCorruptedParty party, ulong target)
    {
        var json = JsonSerializer.Serialize(party);
        var digest = party.Digest;
        var count = (json.Length + PartyPayloadBuffer.ChunkSize - 1) / PartyPayloadBuffer.ChunkSize;
        if (count > PartyPayloadBuffer.MaximumChunks)
            throw new JsonException("The Corrupted Party exceeds the supported 4 MB preview limit.");
        for (int index = 0; index < count; index++)
        {
            int offset = index * PartyPayloadBuffer.ChunkSize;
            Send(LobbyPreviewMessageKind.Snapshot, target,
                json.Substring(offset, Math.Min(PartyPayloadBuffer.ChunkSize, json.Length - offset)), digest, index, count);
        }
    }

    private void Send(LobbyPreviewMessageKind kind, ulong target, string body = "", string digest = "",
        int index = 0, int count = 1) =>
        environment.Send(new LobbyPreviewPacket
        {
            Kind = kind, RequestId = IsHost ? requests.GetValueOrDefault(target, "") : requestId,
            Epoch = epoch, MemberNetIds = members.ToArray(), Payload = body, Digest = digest, Index = index, Count = count
        }, target);

    private static void ValidatePacket(LobbyPreviewPacket packet)
    {
        if (!Enum.IsDefined(packet.Kind) || packet.Payload == null || packet.Payload.Length > PartyPayloadBuffer.ChunkSize ||
            packet.RequestId == null || packet.RequestId.Length > 64 || packet.Epoch == null || packet.Epoch.Length > 64 ||
            packet.Digest == null || packet.Digest.Length > 64)
            throw new JsonException("Invalid lobby preview packet.");
    }

    private void Reset()
    {
        Party = null;
        Error = null;
        IsLoading = true;
        Status = "Loading the host's Corrupted Party; waiting for participant identities.";
        payload = new PartyPayloadBuffer();
        elapsed = retryElapsed = 0;
        Notify();
    }

    private static bool IsExpected(Exception failure) =>
        failure is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or
            NotSupportedException or FormatException or SocketException or TimeoutException;

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception failure) when (IsExpected(failure)) { Fail(failure.Message); }
    }

    private void Fail(string reason)
    {
        if (Error != null)
            return;
        Party = null;
        IsLoading = false;
        Error = "Lobby deck preview failed: " + reason;
        if (IsHost)
        {
            foreach (var member in members.Where(id => id != environment.LocalNetId && requests.ContainsKey(id)))
            {
                try { Send(LobbyPreviewMessageKind.Failure, member, Error[..Math.Min(Error.Length, PartyPayloadBuffer.ChunkSize)]); }
                catch (Exception failure) when (IsExpected(failure))
                {
                    Error += " Could not notify a participant: " + failure.Message;
                }
            }
        }
        Status = Error;
        Notify();
    }

    private void Notify()
    {
        try { environment.Changed(); }
        catch (Exception failure) when (IsExpected(failure))
        {
            Party = null;
            IsLoading = false;
            Error = Status = "Lobby deck preview notification failed: " + failure.Message;
        }
    }
}
