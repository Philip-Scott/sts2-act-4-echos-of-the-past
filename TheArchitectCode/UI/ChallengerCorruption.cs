using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace TheArchitect.TheArchitectCode.UI;

public partial class ChallengerCorruption : Node
{
    private const float Intensity = 0.75f;
    private static readonly StringName EffectTime = "effect_time";
    private static readonly StringName Strength = "strength";
    private static readonly StringName BodyRect = "body_rect";
    private static readonly Lazy<Shader> EchoShader = new(() => new Shader
    {
        Code = """
            shader_type canvas_item;
            render_mode unshaded, blend_premul_alpha;

            uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear;
            uniform vec4 body_rect = vec4(0.0, 0.0, 1.0, 1.0);
            uniform float effect_time = 0.0;
            uniform float strength = 0.0;
            varying vec2 local_position;
            varying vec2 screen_x;
            varying vec2 screen_y;
            varying vec4 composite_tint;

            void vertex() {
                local_position = VERTEX;
                screen_x = (CANVAS_MATRIX * MODEL_MATRIX * vec4(1.0, 0.0, 0.0, 0.0)).xy;
                screen_y = (CANVAS_MATRIX * MODEL_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xy;
                composite_tint = COLOR;
            }

            float hash(vec2 p) {
                return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
            }

            float noise(vec2 p) {
                vec2 i = floor(p);
                vec2 f = fract(p);
                f = f * f * (3.0 - 2.0 * f);
                return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x),
                    mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0)), f.x), f.y);
            }

            vec4 over(vec4 top, vec4 bottom) {
                return top + bottom * (1.0 - top.a);
            }

            void fragment() {
                vec3 violet = vec3(0.64, 0.28, 1.0);
                vec3 ice = vec3(0.40, 0.90, 1.0);
                vec2 p = (local_position - body_rect.xy) / body_rect.zw;
                vec2 dx = screen_x * SCREEN_PIXEL_SIZE * body_rect.z;
                vec2 dy = screen_y * SCREEN_PIXEL_SIZE * body_rect.w;
                float drift = (0.05 + 0.045 * sin(effect_time * 0.85)) * strength;
                float band = 1.0 - smoothstep(0.014, 0.027, abs(p.y - fract(effect_time * 0.075)));
                float slip = band * sin(effect_time * 2.0) * 0.066 * strength;
                vec4 body = textureLod(screen_texture, SCREEN_UV + dx * slip, 0.0);
                float left = textureLod(screen_texture, SCREEN_UV + dx * drift + dy * 0.012, 0.0).a;
                float right = textureLod(screen_texture, SCREEN_UV - dx * drift * 1.5 - dy * 0.021, 0.0).a;
                float cyan_alpha = left * strength * 0.36;
                float violet_alpha = right * strength * 0.45;
                vec4 echoes = over(vec4(ice * cyan_alpha, cyan_alpha),
                    vec4(violet * violet_alpha, violet_alpha));

                // CanvasGroup's cleared backbuffer contains the composed body's premultiplied color.
                vec3 color = body.a > 0.0001 ? body.rgb / body.a : vec3(0.0);
                float lines = pow(0.5 + 0.5 * sin(p.y * 102.0
                    + noise(vec2(p.y * 4.8, effect_time * 0.3)) * 8.0), 26.0);
                color = mix(color, vec3(dot(color, vec3(0.3, 0.59, 0.11))), strength * 0.2);
                color += ice * lines * strength * 0.12;
                color = mix(color, ice, band * strength * 0.32);
                vec4 result = over(vec4(color * body.a, body.a), echoes);
                COLOR = vec4(result.rgb * composite_tint.rgb, result.a) * composite_tint.a;
            }
            """
    });

    private NCreatureVisuals _visuals = null!;
    private Control _bounds = null!;
    private NCreature? _creature;
    private ChallengerBinding _back = null!;
    private ChallengerBinding _front = null!;
    private readonly List<(Node2D Body, CanvasGroup Group, ShaderMaterial Material)> _bodies = [];
    private double _time;
    private float _strength = Intensity;

    public static void Attach(NCreatureVisuals visuals)
    {
        var effect = new ChallengerCorruption
        {
            Name = "BoundEcho",
            _visuals = visuals,
            _bounds = visuals.GetNode<Control>("%Bounds")
        };
        var body = visuals.GetNode<Node2D>("%Visuals");
        effect.Wrap(body, "BoundEchoBody");
        if (visuals.GetNodeOrNull<Node2D>("%PhobiaModeVisuals") is { } alternate)
            effect.Wrap(alternate, "BoundEchoPhobiaBody");

        var group = effect._bodies[0].Group;
        var parent = group.GetParent();
        effect._back = new ChallengerBinding
        {
            Name = "BoundEchoBack", ZIndex = group.ZIndex, ZAsRelative = group.ZAsRelative
        };
        effect._front = new ChallengerBinding
        {
            Name = "BoundEchoFront", Front = true, ZIndex = group.ZIndex, ZAsRelative = group.ZAsRelative
        };
        parent.AddChild(effect._back);
        parent.MoveChild(effect._back, group.GetIndex());
        parent.AddChild(effect._front);
        parent.MoveChild(effect._front, group.GetIndex() + 1);
        visuals.AddChild(effect);
    }

    private void Wrap(Node2D body, string name)
    {
        var parent = body.GetParent();
        var index = body.GetIndex();
        var material = new ShaderMaterial { Shader = EchoShader.Value };
        var group = new CanvasGroup
        {
            Name = name,
            Material = material,
            FitMargin = 32,
            ClearMargin = 48,
            ZIndex = body.ZIndex,
            ZAsRelative = body.ZAsRelative
        };
        parent.AddChild(group);
        parent.MoveChild(group, index);
        // Keep the original Spine node, its children, scene owner and native materials intact.
        body.Reparent(group, keepGlobalTransform: false);
        body.ZIndex = 0;
        body.ZAsRelative = true;
        _bodies.Add((body, group, material));
    }

    public override void _Ready()
    {
        _creature = _visuals.GetParentOrNull<NCreature>();
    }

    public override void _Process(double delta)
    {
        _time += delta;
        bool alive = _creature?.Entity.IsDead != true;
        _strength = Mathf.MoveToward(_strength, alive ? Intensity : 0f, (float)delta * 3f);
        foreach (var (body, group, material) in _bodies)
        {
            // Native death VFX take the original body into their own viewport.
            bool attached = IsInstanceValid(body) && body.GetParent() == group;
            group.Visible = attached && body.Visible && ApplyInheritedModulation(group);
            if (!group.Visible)
                continue;
            var rect = BoundsIn(group);
            float margin = Mathf.Max(rect.Size.X * 0.15f, rect.Size.Y * 0.025f) + 8f;
            group.FitMargin = margin;
            group.ClearMargin = margin + 8f;
            material.SetShaderParameter(BodyRect, new Vector4(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y));
            material.SetShaderParameter(EffectTime, (float)_time);
            material.SetShaderParameter(Strength, _strength);
        }
        bool visible = _strength > 0 && _bodies.Any(entry => entry.Group.Visible);
        _back.Visible = visible;
        _front.Visible = visible;
        if (visible)
        {
            _back.UpdateEffect(BoundsIn(_back), (float)_time, _strength);
            _front.UpdateEffect(BoundsIn(_front), (float)_time, _strength);
        }
    }

    internal static bool ApplyInheritedModulation(CanvasGroup group)
    {
        var tint = Colors.White;
        for (var parent = group.GetParent() as CanvasItem; parent != null;
             parent = parent.TopLevel ? null : parent.GetParent() as CanvasItem)
            tint *= parent.Modulate;
        // Capture opaque attachments, then fade/tint the composite once. The compatibility
        // renderer's backbuffer can have only two alpha bits; capturing a fade loses precision.
        group.Modulate = new Color(Inverse(tint.R), Inverse(tint.G), Inverse(tint.B), Inverse(tint.A));
        group.SelfModulate = tint;
        return tint.A > 0;
    }

    private static float Inverse(float component) => component > 0 ? 1f / component : 1f;

    private Rect2 BoundsIn(Node2D target)
    {
        var transform = target.GlobalTransform.AffineInverse() * _bounds.GetGlobalTransform();
        var rect = new Rect2(transform * Vector2.Zero, Vector2.Zero);
        rect = rect.Expand(transform * new Vector2(_bounds.Size.X, 0));
        rect = rect.Expand(transform * _bounds.Size);
        rect = rect.Expand(transform * new Vector2(0, _bounds.Size.Y));
        if (rect.Size.X <= 0 || rect.Size.Y <= 0)
            throw new InvalidOperationException("Bound Echo requires non-empty character bounds.");
        return rect;
    }
}
