using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;

namespace TheArchitect.TheArchitectCode.UI;

internal partial class CorruptedDeckButton : NButton
{
    private Label label = null!;
    private TextureRect background = null!;
    private readonly CorruptedDeckResetGesture gesture = new();
    private bool rightPressed;
    private NCardExhaustVfx? exhaust;
    private Tween? exhaustTween;
    private float exhaustProgress;
    internal string Text { get; init; } = "";
    internal Func<bool>? CanReset { get; init; }
    internal Func<Task<bool>>? ResetDeck { get; init; }
    internal Action? ResetCompleted { get; init; }

    internal static string Localize(string key) =>
        new LocString("main_menu_ui", "THEARCHITECT.DECK_PREVIEW." + key).GetFormattedText();

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.All;
        background = new TextureRect
        {
            Texture = ResourceLoader.Load<Texture2D>("res://images/ui/reward_screen/reward_skip_button.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(background);
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        label = new Label
        {
            Text = Text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.AddThemeFontOverride("font", ResourceLoader.Load<Font>("res://themes/kreon_bold_glyph_space_one.tres"));
        label.AddThemeFontSizeOverride("font_size", 24);
        label.AddThemeColorOverride("font_color", new Color("efdba6"));
        label.AddThemeColorOverride("font_outline_color", new Color("18262b"));
        label.AddThemeConstantOverride("outline_size", 6);
        AddChild(label);
        label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ConnectSignals();
        VisibilityChanged += () =>
        {
            if (!IsVisibleInTree())
                InterruptGesture();
        };
    }

    public override void _Input(InputEvent inputEvent)
    {
        base._Input(inputEvent);
        if (inputEvent is InputEventMouseButton mouse)
        {
            bool inside = IsVisibleInTree() && new Rect2(Vector2.Zero, Size).HasPoint(GetLocalMousePosition());
            if (mouse.Pressed && (mouse.ButtonIndex != MouseButton.Right || !inside) ||
                !mouse.Pressed && rightPressed && !inside)
                InterruptGesture();
        }
        else if (inputEvent.IsActionPressed(MegaInput.select))
            InterruptGesture();
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Right } mouse)
        {
            if (mouse.Pressed)
            {
                rightPressed = IsEnabled && IsVisibleInTree() && !gesture.IsResetting &&
                    !gesture.IsBurned && ResetDeck != null && CanReset?.Invoke() == true;
                if (!rightPressed)
                    InterruptGesture();
            }
            else if (rightPressed)
            {
                rightPressed = false;
                bool reset = gesture.Click(CanReset?.Invoke() == true);
                if (reset)
                    TaskHelper.RunSafely(ResetAndBurn());
                else if (gesture.Count > 3)
                    PlayExhaust((gesture.Count - 3) / 10f, false);
                else if (gesture.Count == 0)
                    ClearExhaust();
            }
            AcceptEvent();
            return;
        }
        if (gesture.IsResetting || gesture.IsBurned)
        {
            AcceptEvent();
            return;
        }
        base._GuiInput(inputEvent);
        if (inputEvent.IsAction(MegaInput.select))
            AcceptEvent();
    }

    protected override void OnPress()
    {
        ResetGesture();
        base.OnPress();
    }

    internal void ResetGesture()
    {
        bool wasBurned = gesture.IsBurned;
        rightPressed = false;
        gesture.Reset();
        ClearExhaust();
        if (wasBurned && IsInsideTree())
            Show();
    }

    private void InterruptGesture()
    {
        if (!gesture.IsResetting && !gesture.IsBurned)
            ResetGesture();
    }

    public override void _ExitTree()
    {
        ResetGesture();
        base._ExitTree();
    }

    private async Task ResetAndBurn()
    {
        int version = gesture.Version;
        bool success = false;
        try
        {
            success = await ResetDeck!();
        }
        finally
        {
            bool burn = gesture.Complete(success, version);
            if (IsInstanceValid(this) && IsInsideTree())
            {
                if (burn)
                    PlayExhaust(1f, true);
                else
                    ClearExhaust();
            }
        }
    }

    private void PlayExhaust(float progress, bool final)
    {
        exhaustTween?.Kill();
        if (exhaust == null)
        {
            // Use the game's complete card exhaust scene, including its shader, curve and particles.
            exhaust = ResourceLoader.Load<PackedScene>(NCardExhaustVfx.scenePath).Instantiate<NCardExhaustVfx>();
            exhaust.MouseFilter = MouseFilterEnum.Ignore;
            AddChild(exhaust);
            exhaust.Position = Size / 2f;
            exhaust.Scale = Size / new Vector2(300f, 422f);
            var container = exhaust.Get(NCardExhaustVfx.PropertyName._cardParentContainer).As<Control>();
            var material = exhaust.Get(NCardExhaustVfx.PropertyName._materialContainer).As<Control>();
            material.Set("instance_shader_parameters/erosion_texture_x_offset", GD.Randf());
            var snapshot = new Control
            {
                Size = Size,
                Scale = Vector2.One / exhaust.Scale,
                Position = material.Size / 2f - Size / exhaust.Scale / 2f,
                MouseFilter = MouseFilterEnum.Ignore
            };
            container.AddChild(snapshot);
            snapshot.AddChild(background.Duplicate());
            snapshot.AddChild(label.Duplicate());
            background.Hide();
            label.Hide();
            exhaust.Call(NCardExhaustVfx.MethodName.SetProgress, 0f);
        }

        exhaust.Call(NCardExhaustVfx.MethodName.SetParticlesPlaying, true);
        exhaustTween = CreateTween();
        // A hidden or disabled menu must still finish the saved reset's completion callback.
        if (final)
            exhaustTween.SetPauseMode(Tween.TweenPauseMode.Process);
        exhaustTween.TweenMethod(Callable.From<float>(value =>
        {
            exhaustProgress = value;
            exhaust.Call(NCardExhaustVfx.MethodName.SetProgress, value);
        }), exhaustProgress, progress, exhaust.Get(NCardExhaustVfx.PropertyName._exhaustDuration).AsDouble());
        exhaustTween.TweenCallback(Callable.From(() =>
        {
            exhaust.Call(NCardExhaustVfx.MethodName.SetParticlesPlaying, false);
            if (final)
            {
                exhaust.Get(NCardExhaustVfx.PropertyName._materialContainer).As<Control>().Hide();
                Hide();
                if (IsInstanceValid(this) && IsInsideTree())
                    ResetCompleted?.Invoke();
            }
        }));
    }

    private void ClearExhaust()
    {
        exhaustTween?.Kill();
        exhaustTween = null;
        if (IsInstanceValid(exhaust))
        {
            exhaust!.GetParent()?.RemoveChild(exhaust);
            exhaust.QueueFree();
        }
        exhaust = null;
        exhaustProgress = 0f;
        if (IsInstanceValid(background))
            background.Show();
        if (IsInstanceValid(label))
            label.Show();
    }

    protected override void OnFocus()
    {
        base.OnFocus();
        background.Modulate = new Color(1.25f, 1.25f, 1.25f);
        label.AddThemeColorOverride("font_color", Colors.White);
    }

    protected override void OnUnfocus()
    {
        base.OnUnfocus();
        background.Modulate = Colors.White;
        label.AddThemeColorOverride("font_color", new Color("efdba6"));
    }
}
