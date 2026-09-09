using System.IO;

namespace TheArchitect.TheArchitectCode.Playtest;

internal sealed record ArchitectHistorySetup(int ProfileId, string PlayerHistoryPath, string OpponentHistoryPath,
    string OutputDirectory, string Seed, int MaxEnergy, int BaseOrbSlotCount, bool ForceMirror = true)
{
    internal void Validate()
    {
        if (ProfileId is < 1 or > 3 || MaxEnergy <= 0 || BaseOrbSlotCount < 0)
            throw new InvalidDataException("History setup requires a target slot from 1 to 3 and valid base resources.");
        if (string.IsNullOrWhiteSpace(Seed))
            throw new InvalidDataException("History setup requires a nonempty seed.");
        foreach (var path in new[] { PlayerHistoryPath, OpponentHistoryPath, OutputDirectory })
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                throw new InvalidDataException("History setup requires absolute history and output paths.");
    }
}
