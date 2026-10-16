using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;

namespace TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

// Corruption buffs are applied to every Corrupted Player body when it enters the Elite.
internal static class CorruptedPlayerBuffs
{
    internal static readonly ModelId SingeingIchor = PowerId("SINGEING_ICHOR_POWER");
    internal static readonly ModelId RingOfTheViper = PowerId("RING_OF_THE_VIPER_POWER");
    internal static readonly ModelId TarnishedCrown = PowerId("TARNISHED_CROWN_POWER");
    internal static readonly ModelId FracturedUrn = PowerId("FRACTURED_URN_POWER");
    internal static readonly ModelId OvervoltedMatrix = PowerId("OVERVOLTED_MATRIX_POWER");
    internal static readonly ModelId EchoShard = PowerId("ECHO_SHARD_POWER");

    // Ascension 9 (Deadly Enemies).
    internal const int EchoShardAscension = 9;

    internal static IReadOnlyList<ModelId> For(CharacterModel character, int ascension)
    {
        var buffs = new List<ModelId>();
        if (character switch
            {
                Ironclad => SingeingIchor,
                Silent => RingOfTheViper,
                Regent => TarnishedCrown,
                Necrobinder => FracturedUrn,
                Defect => OvervoltedMatrix,
                _ => null
            } is { } characterBuff)
            buffs.Add(characterBuff);
        if (ascension >= EchoShardAscension)
            buffs.Add(EchoShard);
        return buffs;
    }

    private static ModelId PowerId(string entry) => new("POWER", "THEARCHITECT-" + entry);
}
