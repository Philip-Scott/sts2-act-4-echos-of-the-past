using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Runs;

namespace TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

// Corruption buffs are applied to every Corrupted Player body when it enters the Elite.
internal static class CorruptedPlayerBuffs
{
    internal static readonly ModelId SingeingIchor = PowerId("SINGEING_ICHOR_POWER");
    internal static readonly ModelId RingOfTheViper = PowerId("RING_OF_THE_VIPER_POWER");
    internal static readonly ModelId TarnishedCrown = PowerId("TARNISHED_CROWN_POWER");
    internal static readonly ModelId FracturedUrn = PowerId("FRACTURED_URN_POWER");
    internal static readonly ModelId OvervoltedMatrix = PowerId("OVERVOLTED_MATRIX_POWER");
    internal static readonly ModelId DiscordantBow = PowerId("DISCORDANT_BOW_POWER");
    internal static readonly ModelId EchoShard = PowerId("ECHO_SHARD_POWER");

    internal static IReadOnlyList<ModelId> For(CharacterModel character, bool deadlyEnemies)
    {
        var buffs = new List<ModelId>();
        if (character switch
            {
                Ironclad => SingeingIchor,
                Silent => RingOfTheViper,
                Regent => TarnishedCrown,
                Necrobinder => FracturedUrn,
                Defect => OvervoltedMatrix,
                Fool => DiscordantBow,
                _ => null
            } is { } characterBuff)
            buffs.Add(characterBuff);
        if (deadlyEnemies)
            buffs.Add(EchoShard);
        return buffs;
    }

    private static ModelId PowerId(string entry) => new("POWER", "THEARCHITECT-" + entry);
}

// Scaling follows ascension effects, so Blitz levels map to the same effects as Standard ones.
internal static class CorruptedAscension
{
    internal static bool Has(GameMode mode, int level, AscensionLevel effect) =>
        new AscensionManager(mode, AscensionManager.ClampToMode(mode, level)).HasLevel(effect);

    internal static bool Has(IRunState run, AscensionLevel effect) => Has(run.GameMode, run.AscensionLevel, effect);
}
