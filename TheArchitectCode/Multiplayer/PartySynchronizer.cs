using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using TheArchitect.TheArchitectCode.Persistence;

namespace TheArchitect.TheArchitectCode.Multiplayer;

public enum ArchitectPartyMessageKind { IdentityRequest, Identity, Origin, EntryRequest, Prepare, Acknowledge, Commit, Reject, Failure }

public sealed record ArchitectPartyPacket
{
    public ArchitectPartyMessageKind Kind { get; set; }
    public ulong Seed { get; set; }
    public string Payload { get; set; } = "";
    public string Digest { get; set; } = "";
    public int Index { get; set; }
    public int Count { get; set; } = 1;
}

internal sealed record PartySyncEnvironment(
    ArchitectRun State,
    ulong[] OriginalPlayers,
    ulong LocalPlayer,
    ulong HostPlayer,
    ulong Seed,
    bool IsInArchitectAct,
    Func<Guid> GetProfileUuid,
    Func<Guid, Guid[], CorruptedPartyEnvelope?> LoadLineage,
    Action<FrozenCorruptedParty> ValidateContent,
    Action<ArchitectPartyPacket, ulong?> Send,
    Action<string> ShowError,
    Func<bool> IsConnected);

internal sealed class PartySynchronizer
{
    private readonly PartySyncEnvironment environment;
    private readonly ArchitectRun state;
    private readonly ulong[] originalPlayers;
    private readonly ulong host;
    private readonly Dictionary<ulong, Guid> identities = [];
    private readonly TaskCompletionSource<bool> ready = new();
    private readonly PartyPayloadBuffer payload = new();
    private PartyEntryConsensus? consensus;
    private Guid localProfile;
    private bool started;
    private bool requested;
    private bool committed;
    private string? error;

    internal bool IsReady => committed && error == null;
    private bool IsHost => environment.LocalPlayer == host;

    internal PartySynchronizer(PartySyncEnvironment environment)
    {
        this.environment = environment;
        state = environment.State;
        originalPlayers = environment.OriginalPlayers.Order().ToArray();
        host = environment.HostPlayer;
    }

    internal void Start()
    {
        if (started)
            return;
        started = true;
        try
        {
            state.BindNetworkOrigin(originalPlayers, host);
            localProfile = environment.GetProfileUuid();
            if (state.Origin is { } origin)
            {
                origin.Validate(originalPlayers, host);
                if (origin.Participants.Single(player => player.NetId == environment.LocalPlayer).ProfileUuid != localProfile)
                    throw new JsonException("The active profile does not own this saved run participant.");
            }
            else if (state.Entered || environment.IsInArchitectAct)
                throw new JsonException("This multiplayer Act 4 save has no original party identity; entry cannot be reconstructed.");
            identities[environment.LocalPlayer] = localProfile;
            if (IsHost)
                Send(ArchitectPartyMessageKind.IdentityRequest);
            else
                Send(ArchitectPartyMessageKind.Identity, localProfile.ToString("N"));
        }
        catch (Exception failure) when (failure is JsonException or IOException or UnauthorizedAccessException or
            InvalidOperationException or NotSupportedException or FormatException or SocketException or TimeoutException)
        {
            Fail(failure.Message);
        }
    }

    internal async Task EnsureEntry()
    {
        Start();
        try
        {
            if (!requested && error == null)
            {
                requested = true;
                if (IsHost)
                {
                    Send(ArchitectPartyMessageKind.IdentityRequest);
                    TryPrepare();
                }
                else
                    Send(ArchitectPartyMessageKind.EntryRequest, localProfile.ToString("N"));
            }
        }
        catch (Exception failure) when (failure is JsonException or IOException or UnauthorizedAccessException or
            InvalidOperationException or NotSupportedException or FormatException or SocketException or TimeoutException)
        {
            Fail(failure.Message);
        }
        if (await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(45))) != ready.Task)
            Fail("Timed out synchronizing the Architect party. All original participants must be connected with matching mod content.");
        await ready.Task;
    }

    internal void Receive(ArchitectPartyPacket message, ulong sender)
    {
        if (message.Seed != environment.Seed || !originalPlayers.Contains(sender))
            return;
        Start();
        try
        {
            if (!Enum.IsDefined(message.Kind) || message.Payload.Length > PartyPayloadBuffer.ChunkSize)
                throw new JsonException("Invalid Architect multiplayer message.");
            if (error != null)
            {
                if (IsHost && sender != host)
                    Send(ArchitectPartyMessageKind.Failure, error, target: sender);
                return;
            }
            if (IsHost)
                ReceiveAsHost(message, sender);
            else
            {
                if (sender != host)
                    throw new JsonException("Only the starting host can synchronize the Architect party.");
                ReceiveAsClient(message);
            }
        }
        catch (Exception failure) when (failure is JsonException or IOException or UnauthorizedAccessException or
            InvalidOperationException or NotSupportedException or FormatException or SocketException or TimeoutException)
        {
            Fail(failure.Message);
        }
    }

    private void ReceiveAsHost(ArchitectPartyPacket message, ulong sender)
    {
        switch (message.Kind)
        {
            case ArchitectPartyMessageKind.Identity:
            case ArchitectPartyMessageKind.EntryRequest:
                var profile = Guid.ParseExact(message.Payload, "N");
                if (profile == Guid.Empty || identities.TryGetValue(sender, out var previous) && previous != profile ||
                    state.Origin is { } origin &&
                    origin.Participants.Single(player => player.NetId == sender).ProfileUuid != profile)
                    throw new JsonException("An original participant's persistent profile identity changed.");
                identities[sender] = profile;
                BindOrigin();
                if (message.Kind == ArchitectPartyMessageKind.EntryRequest)
                    requested = true;
                if (state.Origin != null)
                    SendOrigin(sender);
                TryPrepare();
                if (committed)
                    SendParty(consensus!.Party, sender);
                break;
            case ArchitectPartyMessageKind.Acknowledge:
                if (consensus == null)
                    throw new JsonException("Architect party acknowledgement arrived without a prepared party.");
                consensus.Acknowledge(sender, message.Digest);
                if (committed)
                    Send(ArchitectPartyMessageKind.Commit, digest: consensus.Digest, target: sender);
                else if (consensus.Ready)
                    Commit();
                break;
            case ArchitectPartyMessageKind.Reject:
                Fail($"A participant could not validate the Architect party: {message.Payload}");
                break;
            default:
                throw new JsonException("A client attempted to send a host-only Architect party message.");
        }
    }

    private void ReceiveAsClient(ArchitectPartyPacket message)
    {
        switch (message.Kind)
        {
            case ArchitectPartyMessageKind.IdentityRequest:
                Send(ArchitectPartyMessageKind.Identity, localProfile.ToString("N"));
                break;
            case ArchitectPartyMessageKind.Origin:
                var origin = JsonSerializer.Deserialize<FrozenCorruptedParty>(message.Payload)
                    ?? throw new JsonException("The host supplied an empty original party.");
                Validate(origin);
                if (origin.Lineage != null)
                    throw new JsonException("Original party registration cannot select a lineage.");
                state.BindOrigin(origin);
                break;
            case ArchitectPartyMessageKind.Prepare:
                var json = payload.Add(message.Digest, message.Index, message.Count, message.Payload);
                if (json == null)
                    return;
                var party = JsonSerializer.Deserialize<FrozenCorruptedParty>(json)
                    ?? throw new JsonException("The host supplied an empty entry party.");
                Validate(party);
                if (party.Digest != message.Digest)
                    throw new JsonException("The transferred Architect party content hash does not match.");
                if (state.Entered && state.EntryParty?.Digest != party.Digest)
                    throw new JsonException("The host attempted to replace a saved Architect encounter.");
                if (state.PendingParty is { } pending && pending.Digest != party.Digest)
                    throw new JsonException("The host attempted to replace a previously frozen Architect entry.");
                state.PendingParty = party;
                environment.ValidateContent(party);
                consensus = new PartyEntryConsensus(party, originalPlayers, host);
                Send(ArchitectPartyMessageKind.Acknowledge, digest: party.Digest);
                break;
            case ArchitectPartyMessageKind.Commit:
                if (consensus == null || consensus.Digest != message.Digest)
                    throw new JsonException("The host committed an Architect party this peer did not validate.");
                state.EnterParty(consensus.Party);
                committed = true;
                ready.TrySetResult(true);
                break;
            case ArchitectPartyMessageKind.Failure:
                Fail(message.Payload, notifyHost: false);
                break;
            default:
                throw new JsonException("Invalid host Architect synchronization message.");
        }
    }

    private void BindOrigin()
    {
        if (identities.Count != originalPlayers.Length)
            return;
        var origin = new FrozenCorruptedParty
        {
            RunId = state.Id,
            HostNetId = host,
            HostProfileUuid = localProfile,
            GroupKey = SuccessorGroup.Key(identities.Values),
            Participants = originalPlayers.Select(id => new SuccessorParticipant(id, identities[id])).ToArray()
        };
        origin.Validate(originalPlayers, host);
        state.BindOrigin(origin);
        SendOrigin();
    }

    private void SendOrigin(ulong? target = null) =>
        Send(ArchitectPartyMessageKind.Origin, JsonSerializer.Serialize(state.Origin), target: target);

    private void TryPrepare()
    {
        if (!requested || state.Origin == null || consensus != null || identities.Count != originalPlayers.Length)
            return;
        var party = state.EntryParty ?? state.PendingParty;
        if (state.Entered && party == null)
            throw new JsonException("The saved multiplayer encounter is missing its frozen party.");
        party ??= state.Origin with
        {
            Lineage = environment.LoadLineage(localProfile,
                state.Origin.Participants.Select(player => player.ProfileUuid).ToArray())
        };
        Validate(party);
        state.PendingParty = party;
        environment.ValidateContent(party);
        consensus = new PartyEntryConsensus(party, originalPlayers, host);
        consensus.Acknowledge(host, consensus.Digest);
        SendOrigin();
        SendParty(party);
    }

    private void Validate(FrozenCorruptedParty party)
    {
        party.Validate(originalPlayers, host);
        if (party.Participants.Single(player => player.NetId == environment.LocalPlayer).ProfileUuid != localProfile)
            throw new JsonException("The host's party contains a different identity for the active profile.");
    }

    private void Commit()
    {
        state.EnterParty(consensus!.Party);
        Send(ArchitectPartyMessageKind.Commit, digest: consensus.Digest);
        committed = true;
        ready.TrySetResult(true);
    }

    private void SendParty(FrozenCorruptedParty party, ulong? target = null)
    {
        var json = JsonSerializer.Serialize(party);
        var count = (json.Length + PartyPayloadBuffer.ChunkSize - 1) / PartyPayloadBuffer.ChunkSize;
        if (count > PartyPayloadBuffer.MaximumChunks)
            throw new JsonException("The Corrupted Party exceeds the supported 4 MB synchronization limit.");
        for (int index = 0; index < count; index++)
        {
            int offset = index * PartyPayloadBuffer.ChunkSize;
            Send(ArchitectPartyMessageKind.Prepare,
                json.Substring(offset, Math.Min(PartyPayloadBuffer.ChunkSize, json.Length - offset)),
                party.Digest, target, index, count);
        }
    }

    private void Send(ArchitectPartyMessageKind kind, string body = "", string digest = "",
        ulong? target = null, int index = 0, int count = 1)
    {
        var message = new ArchitectPartyPacket
        {
            Kind = kind, Seed = environment.Seed, Payload = body, Digest = digest, Index = index, Count = count
        };
        environment.Send(message, target);
    }

    private void Fail(string reason, bool notifyHost = true)
    {
        if (error != null)
            return;
        error = ("Architect multiplayer synchronization failed: " + reason)[..Math.Min(
            PartyPayloadBuffer.ChunkSize, "Architect multiplayer synchronization failed: ".Length + reason.Length)];
        consensus?.Reject(error);
        if (environment.IsConnected() && (IsHost || notifyHost))
            Send(IsHost ? ArchitectPartyMessageKind.Failure : ArchitectPartyMessageKind.Reject, error);
        ready.TrySetException(new InvalidOperationException(error));
        environment.ShowError(error);
    }
}
