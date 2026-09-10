using System.Runtime.CompilerServices;
using System.Text.Json;
using BaseLib.Patches.Saves;
using MegaCrit.Sts2.Core.Runs;

namespace TheArchitect.TheArchitectCode.Persistence;

public sealed partial class ArchitectRun
{
    private static readonly ConditionalWeakTable<IRunState, ArchitectRun> States = new();
    public static ArchitectRun Get(IRunState run) => States.GetOrCreateValue(run);

    public static void Register()
    {
        ExtendedSaveTypes.RegisterSavedValue<IRunState, string>(
            "TheArchitect.Run.v1",
            run => JsonSerializer.Serialize(Get(run)),
            (run, json) =>
            {
                if (string.IsNullOrEmpty(json))
                    return;
                var state = JsonSerializer.Deserialize<ArchitectRun>(json) ??
                    throw new JsonException("Architect run state is null.");
                if (state.StartingHostNetId is { } startingHost)
                    state.BindNetworkOrigin(run.Players.Select(player => player.NetId).ToArray(), startingHost);
                if (state.Origin is { } origin)
                    origin.Validate(run.Players.Select(player => player.NetId).ToArray(), origin.HostNetId);
                if (state.PendingParty is { } pending)
                {
                    pending.Validate(run.Players.Select(player => player.NetId).ToArray(), pending.HostNetId);
                    if (state.Id != pending.RunId || state.Origin == null ||
                        state.Origin.GroupKey != pending.GroupKey || state.Origin.HostNetId != pending.HostNetId)
                        throw new JsonException("Architect pending party does not match its saved run lineage.");
                }
                if (state.EntryParty is { } party)
                {
                    party.Validate(run.Players.Select(player => player.NetId).ToArray(), party.HostNetId);
                    if (!state.Entered || state.Id != party.RunId || state.Origin == null ||
                        state.Origin.GroupKey != party.GroupKey || state.Origin.HostNetId != party.HostNetId)
                        throw new JsonException("Architect saved frozen party does not match its run lineage.");
                }
                States.Remove(run);
                States.Add(run, state);
            },
            (json, writer) => writer.WriteString(json),
            reader => reader.ReadString());
    }

    public void Enter()
    {
        if (Entered)
            return;
        if (Origin != null)
            throw new InvalidOperationException("Multiplayer entry requires the host's synchronized party.");
        EntrySnapshot = CorruptedPlayerStore.Load();
        if (EntrySnapshot?.Snapshot?.ResolveCharacter() == null && EntrySnapshot != null)
        {
            MainFile.Logger.Warn("Previous Corrupted Player character is unavailable; using First Visit.");
            EntrySnapshot = null;
        }
        Entered = true;
    }

}
