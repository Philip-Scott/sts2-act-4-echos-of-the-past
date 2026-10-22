namespace AnimatedPlayerHands;

/// <summary>
/// Keeps failures inside this display. Pile events run inside the game's card commands, so an
/// exception escaping from here could break combat on this client; modded cards can also throw while
/// rendering for an owner other than the local player.
/// </summary>
internal static class Faults
{
    private static readonly HashSet<string> Reported = [];

    // Logs each distinct problem once, naming the code that threw so a faulty mod can be identified.
    internal static void Report(string context, Exception error)
    {
        var source = error.TargetSite?.DeclaringType?.FullName ?? "unknown";
        if (Reported.Add($"{context}|{error.GetType().FullName}|{source}"))
            ModEntry.Logger.Warn($"{context}: {error.GetType().Name} from {source}: {error.Message}");
    }

    internal static Action<T> Guard<T>(string context, Action<T> action) => value =>
    {
        try
        {
            action(value);
        }
        catch (Exception error)
        {
            Report(context, error);
        }
    };
}
