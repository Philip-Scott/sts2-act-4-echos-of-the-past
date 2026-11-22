namespace TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

public static class CorruptedPlayerHealth
{
    public static int CalculateMaxHp(int originalMaxHp, bool toughEnemies)
    {
        if (originalMaxHp <= 0)
            throw new ArgumentOutOfRangeException(nameof(originalMaxHp));
        return checked((int)Math.Ceiling(originalMaxHp * (toughEnemies ? 4.1m : 3.5m)));
    }
}
