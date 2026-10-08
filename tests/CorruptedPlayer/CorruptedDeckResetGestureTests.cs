using TheArchitect.TheArchitectCode.UI;

internal static class CorruptedDeckResetGestureTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("deck reset needs ten consecutive permitted right clicks", () =>
        {
            var gesture = new CorruptedDeckResetGesture();
            for (int click = 1; click <= 10; click++)
            {
                Check(gesture.Click(true) == (click == 10));
                Check(gesture.Count == click);
                Check(gesture.IsResetting == (click == 10));
            }
            Check(!gesture.Click(true) && gesture.Count == 10);
        });

        test("deck reset interruption and authority loss clear the streak", () =>
        {
            var gesture = new CorruptedDeckResetGesture();
            for (int click = 0; click < 9; click++)
                gesture.Click(true);
            gesture.Reset();
            Check(!gesture.Click(true) && gesture.Count == 1);
            Check(!gesture.Click(false) && gesture.Count == 0);
            Check(!gesture.IsResetting);
        });

        test("failed deck reset allows retry but successful reset stays burned", () =>
        {
            var gesture = new CorruptedDeckResetGesture();
            for (int click = 0; click < 10; click++)
                gesture.Click(true);
            Check(!gesture.Complete(false, gesture.Version));
            Check(!gesture.IsResetting && !gesture.IsBurned && gesture.Count == 0);
            for (int click = 0; click < 10; click++)
                gesture.Click(true);
            Check(gesture.Complete(true, gesture.Version));
            Check(gesture.IsBurned && !gesture.Click(true));
            gesture.Reset();
            Check(!gesture.IsBurned && !gesture.Click(true) && gesture.Count == 1);
        });

        test("party changes cannot start overlapping reset or burn a replacement button", () =>
        {
            var gesture = new CorruptedDeckResetGesture();
            for (int click = 0; click < 10; click++)
                gesture.Click(true);
            int previousVersion = gesture.Version;
            gesture.Reset();
            Check(gesture.IsResetting && !gesture.Click(true));
            Check(!gesture.Complete(true, previousVersion));
            Check(!gesture.IsResetting && !gesture.IsBurned);
            Check(!gesture.Click(true) && gesture.Count == 1);
        });
    }

    private static void Check(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("Deck reset gesture assertion failed.");
    }
}
