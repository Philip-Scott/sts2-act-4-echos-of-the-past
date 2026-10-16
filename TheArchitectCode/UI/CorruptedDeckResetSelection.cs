using System.Text.Json;
using System.Text.Json.Nodes;
using TheArchitect.TheArchitectCode.Persistence;

namespace TheArchitect.TheArchitectCode.UI;

internal sealed record CorruptedDeckResetSelection(string Json, FrozenCorruptedParty? Party)
{
    internal static CorruptedDeckResetSelection Create(string? json, ulong[] players,
        ulong? host = null, Guid? hostProfile = null)
    {
        var root = string.IsNullOrEmpty(json) ? new JsonObject() :
            JsonNode.Parse(json) as JsonObject ?? throw new JsonException("Architect run state is not an object.");
        root[nameof(ArchitectRun.EntrySnapshot)] = null;
        root[nameof(ArchitectRun.PlaytestOpponents)] = null;

        // Discard decks before deserializing. The failsafe must work even when their contents are invalid.
        foreach (var key in new[] { nameof(ArchitectRun.Origin), nameof(ArchitectRun.PendingParty), nameof(ArchitectRun.EntryParty) })
        {
            if (root[key] is JsonObject party)
                party[nameof(FrozenCorruptedParty.Lineage)] = null;
        }
        var state = root.Deserialize<ArchitectRun>() ?? throw new JsonException("Architect run state is null.");
        if (players.Length == 1)
        {
            if (host != null || state.Origin != null || state.StartingHostNetId != null ||
                state.EntryParty != null || state.PendingParty != null)
                throw new JsonException("Cannot reset a multiplayer opponent as a solo run.");
            return new(root.ToJsonString(), null);
        }

        var origin = state.Origin ?? throw new JsonException("The saved party has no original participant identities.");
        if (host == null || hostProfile == null || origin.HostProfileUuid != hostProfile ||
            state.StartingHostNetId != host || state.Id != origin.RunId)
            throw new InvalidOperationException("Only the original host profile can reset this saved party.");
        origin.Validate(players, host.Value);
        state.BindNetworkOrigin(players, host.Value);
        foreach (var party in new[] { state.EntryParty, state.PendingParty }.OfType<FrozenCorruptedParty>())
        {
            party.Validate(players, host.Value);
            if (party.RunId != origin.RunId || party.HostProfileUuid != origin.HostProfileUuid ||
                party.GroupKey != origin.GroupKey || !party.Participants.SequenceEqual(origin.Participants))
                throw new JsonException("The saved opponent belongs to a different party.");
        }
        // Keep an explicit First Visit selection so resume cannot load a newer lineage.
        var selectedKey = state.Entered ? nameof(ArchitectRun.EntryParty) : nameof(ArchitectRun.PendingParty);
        root[selectedKey] ??= JsonSerializer.SerializeToNode(origin);
        if (state.Entered)
            root[nameof(ArchitectRun.PendingParty)] = null;
        return new(root.ToJsonString(), origin);
    }
}
