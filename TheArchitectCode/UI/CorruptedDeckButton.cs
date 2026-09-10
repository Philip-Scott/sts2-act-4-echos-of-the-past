using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace TheArchitect.TheArchitectCode.UI;

internal partial class CorruptedDeckButton : NButton
{
    private Label label = null!;
    private TextureRect background = null!;
    internal string Text { get; init; } = "";

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
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        base._GuiInput(inputEvent);
        if (inputEvent.IsAction(MegaInput.select))
            AcceptEvent();
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
