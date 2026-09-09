using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;

namespace TheArchitect.TheArchitectCode.Playtest;

internal partial class NativeDemoObserver : Node
{
    private static string _stage = "startup";
    private ulong _lastReceipt;
    private ulong _unfocusedFrames;
    private bool _capturing;

    internal static void RecordCapture(string stage)
    {
        if (!stage.StartsWith("captures/", StringComparison.Ordinal))
            _stage = stage;
    }

    public override void _Process(double delta)
    {
        var game = NGame.Instance!;
        if (!game.GetWindow().HasFocus())
            _unfocusedFrames++;
        var now = Time.GetTicksMsec();
        if (now - _lastReceipt >= 500)
        {
            var pointer = DisplayServer.MouseGetPosition();
            var receipt = JsonSerializer.Serialize(new
            {
                stage = _stage,
                focused = game.GetWindow().HasFocus(),
                frames = Engine.GetProcessFrames(),
                unfocused_frames = _unfocusedFrames,
                desktop_pointer_x = pointer.X,
                desktop_pointer_y = pointer.Y,
                window_title = game.GetWindow().Title,
                updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            var path = Path.Combine(NativeDemoSafety.RuntimePath, "telemetry.json");
            File.WriteAllText(path + ".tmp", receipt);
            File.Move(path + ".tmp", path, overwrite: true);
            _lastReceipt = now;
        }
        var request = Path.Combine(NativeDemoSafety.RuntimePath, "capture-request");
        if (_capturing || !File.Exists(request))
            return;
        var name = File.ReadAllText(request);
        File.Delete(request);
        if (!Regex.IsMatch(name, @"\Acapture-[0-9]+\z"))
            throw new InvalidOperationException("Invalid native viewport capture request.");
        _capturing = true;
        TaskHelper.RunSafely(CaptureRequested(name));
    }

    private async Task CaptureRequested(string name)
    {
        try
        {
            await NativeDemoPlaytest.Capture("captures/" + name);
            File.WriteAllText(Path.Combine(NativeDemoSafety.RuntimePath, "captures", name + ".ready"), "ok");
        }
        finally
        {
            _capturing = false;
        }
    }
}
