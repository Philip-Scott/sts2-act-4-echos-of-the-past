namespace TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

public static class CorruptedPlayerHealth
{
    public static int CalculateMaxHp(int originalMaxHp, int ascension)
    {
        if (originalMaxHp <= 0)
            throw new ArgumentOutOfRangeException(nameof(originalMaxHp));
        return checked((int)Math.Ceiling(originalMaxHp * (ascension >= 8 ? 2.5m : 2m)));
    }
}
