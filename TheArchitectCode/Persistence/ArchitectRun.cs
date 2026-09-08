using System.Runtime.CompilerServices;
using System.Text.Json;
using BaseLib.Patches.Saves;
using MegaCrit.Sts2.Core.Runs;

namespace TheArchitect.TheArchitectCode.Persistence;

public sealed class ArchitectRun
{
    private static readonly ConditionalWeakTable<IRunState, ArchitectRun> States = new();
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Entered { get; set; }
    public ChallengerEnvelope? EntrySnapshot { get; set; }
    public string? Outcome { get; set; }
    public long? SnapshotRevision { get; set; }

    public static ArchitectRun Get(IRunState run) => States.GetOrCreateValue(run);

    public static void Register()
    {
        ExtendedSaveTypes.RegisterSavedValue<IRunState, string>(
            "TheArchitect.Run.v1",
            run => run.Players.Count == 1 ? JsonSerializer.Serialize(Get(run)) : null,
            (run, json) =>
            {
                if (string.IsNullOrEmpty(json))
                    return;
                var state = JsonSerializer.Deserialize<ArchitectRun>(json) ??
                    throw new JsonException("Architect run state is null.");
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
        EntrySnapshot = ChallengerStore.Load();
        if (EntrySnapshot?.Snapshot?.ResolveCharacter() == null && EntrySnapshot != null)
        {
            MainFile.Logger.Warn("Previous Challenger character is unavailable; using First Visit.");
            EntrySnapshot = null;
        }
        Entered = true;
    }
}
