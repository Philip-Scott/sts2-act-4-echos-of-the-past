namespace TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

public sealed class CorruptedPartyPhase
{
    private readonly bool[] _defeated;
    private int _remaining;

    public CorruptedPartyPhase(int members)
    {
        if (members <= 0)
            throw new ArgumentOutOfRangeException(nameof(members));
        _defeated = new bool[members];
        _remaining = members;
    }

    public bool Defeat(int member)
    {
        if ((uint)member >= (uint)_defeated.Length)
            throw new ArgumentOutOfRangeException(nameof(member));
        if (_defeated[member])
            return false;
        _defeated[member] = true;
        return --_remaining == 0;
    }
}
