using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace TheArchitect.TheArchitectCode.Relics;

internal static class MirrorDuplication
{
    internal const int CopyCount = 3;

    private static bool IsBlocked(RelicModel relic) =>
        relic is HandheldMirror
            or TouchOfOrobas // Replaces the starter relic.
            or PaelsEye // Multiple copies do not work.
            or GoldenCompass // Only applies to the Act 2 map.
            or FurCoat // Additional copies trivialize the fight.
            or LordsParasol // Additional copies break the shop.
            or ArchaicTooth // The required starter card may already have been transformed.
            or PaperKrane // Only the first owned copy modifies Weak.
            or PaperPhrog // Only the first owned copy modifies Vulnerable.
            or LavaRock // Only rewards the Act 1 boss.
            or WingedBoots // Copies spend charges on the same jump; Act 4 has no branching route.
            or Byrdpip; // Duplicate pets are not consistently addressed independently.

    internal static ModelId[] CapturePopulation(Player owner) =>
        CapturePopulation(owner.Relics);

    internal static ModelId[] CapturePopulation(IEnumerable<RelicModel> owned) =>
        owned.Where(relic => !relic.IsMelted && !IsBlocked(relic))
            .Select(relic => relic.Id)
            .Distinct()
            .OrderBy(id => id.Category, StringComparer.Ordinal)
            .ThenBy(id => id.Entry, StringComparer.Ordinal)
            .ToArray();

    internal static RelicModel[] PrepareCopies(IEnumerable<RelicModel> owned, Func<int, int> nextInt)
    {
        var sources = owned.Where(relic => !relic.IsMelted).ToArray();
        var selected = SelectThree(CapturePopulation(sources), nextInt);
        return selected.Select(id => CreateCopy(sources.First(relic => relic.Id == id))).ToArray();
    }

    internal static RelicModel CreateCopy(RelicModel source)
    {
        var copy = ModelDb.GetById<RelicModel>(source.Id).ToMutable();
        // Preserve prepared rewards and earned training, not ordinary combat counters.
        switch (source, copy)
        {
            case (DustyTome original, DustyTome duplicate):
                duplicate.AncientCard = original.AncientCard ??
                    throw new InvalidOperationException("Handheld Mirror cannot copy a Dusty Tome without its prepared card.");
                break;
            case (SeaGlass original, SeaGlass duplicate):
                duplicate.CharacterId = original.CharacterId ??
                    throw new InvalidOperationException("Handheld Mirror cannot copy Sea Glass without its prepared character.");
                break;
            case (Girya original, Girya duplicate):
                duplicate.TimesLifted = original.TimesLifted;
                break;
        }
        return copy;
    }

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
