using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace TheArchitect.TheArchitectCode.Relics;

internal static class MirrorDuplication
{
    internal const int CopyCount = 3;

    internal static ModelId[] CapturePopulation(Player owner) =>
        CapturePopulation(owner.Relics);

    internal static ModelId[] CapturePopulation(IEnumerable<RelicModel> owned) =>
        owned.Where(relic => !relic.IsMelted && relic is not HandheldMirror)
            .Select(relic => relic.Id)
            .Distinct()
            .OrderBy(id => id.Category, StringComparer.Ordinal)
            .ThenBy(id => id.Entry, StringComparer.Ordinal)
            .ToArray();

    internal static ModelId[] SelectThree(IEnumerable<ModelId> population, Func<int, int> nextInt)
    {
        var remaining = population.Distinct().ToList();
        if (remaining.Count < CopyCount)
            throw new InvalidOperationException("Handheld Mirror cannot award fewer than three distinct relic types.");

        var selected = new ModelId[CopyCount];
        for (var i = 0; i < selected.Length; i++)
        {
            var index = nextInt(remaining.Count);
            selected[i] = remaining[index];
            remaining.RemoveAt(index);
        }
        return selected;
    }
}
