namespace TheArchitect.TheArchitectCode.UI;

internal sealed class CorruptedDeckResetGesture
{
    internal int Count { get; private set; }
    internal int Version { get; private set; }
    internal bool IsResetting { get; private set; }
    internal bool IsBurned { get; private set; }

    internal bool Click(bool canReset)
    {
        if (IsResetting || IsBurned)
            return false;
        if (!canReset)
        {
            Reset();
            return false;
        }

        Count++;
        IsResetting = Count == 10;
        return IsResetting;
    }

    internal void Reset()
    {
        Count = 0;
        IsBurned = false;
        Version++;
    }

    internal bool Complete(bool success, int version)
    {
        IsResetting = false;
        if (version != Version)
            return false;

        Count = 0;
        IsBurned = success;
        return success;
    }
}
