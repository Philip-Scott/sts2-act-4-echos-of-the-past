using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace TheArchitect.TheArchitectCode.UI;

public partial class CorruptedPlayerCorruption : Node
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
    private CorruptedPlayerBinding _back = null!;
    private CorruptedPlayerBinding _front = null!;
    private sealed class BodyEffect(Node2D body, CanvasGroup? group, ShaderMaterial? material)
    {
        internal Node2D Body { get; } = body;
        internal Node Parent { get; } = body.GetParent();
        internal CanvasGroup? Group { get; } = group;
        internal ShaderMaterial? Material { get; } = material;
        internal Node2D Anchor => Group ?? Body;
        internal Rect2? Rect;
        internal float Strength = float.NaN;
    }

    private sealed class BoundsCache
    {
        internal Transform2D Target;
        internal Transform2D Bounds;
        internal Vector2 Size;
        internal Rect2 Rect;
    }

    private readonly List<BodyEffect> _bodies = [];
    private readonly Dictionary<Node2D, BoundsCache> _boundsCache = new();
    internal int GeometryUpdateCount { get; private set; }
    private double _time;
    private float _strength = Intensity;
    private bool _animateBinding;
    private bool _persistAfterDeath;

    public static void Attach(NCreatureVisuals visuals, bool animateBinding = false, bool persistAfterDeath = false)
    {
        var effect = new CorruptedPlayerCorruption
        {
            Name = "BoundEcho",
            _visuals = visuals,
            _bounds = visuals.GetNode<Control>("%Bounds"),
            _animateBinding = animateBinding,
            _persistAfterDeath = persistAfterDeath,
            _strength = animateBinding ? 0f : Intensity
        };
        var body = visuals.GetNode<Node2D>("%Visuals");
        effect.Wrap(body, "BoundEchoBody");
        if (visuals.GetNodeOrNull<Node2D>("%PhobiaModeVisuals") is { } alternate)
            effect.Wrap(alternate, "BoundEchoPhobiaBody");

        var group = effect._bodies[0].Anchor;
        var parent = group.GetParent();
        effect._back = new CorruptedPlayerBinding
        {
            Name = "BoundEchoBack", ZIndex = group.ZIndex, ZAsRelative = group.ZAsRelative
        };
        effect._front = new CorruptedPlayerBinding
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
        // Nested CanvasGroups share the screen backbuffer. Keep native compositors
        // (and their custom smoke/particle shaders) intact, with bindings only.
        if (ContainsCanvasGroup(body))
        {
            _bodies.Add(new BodyEffect(body, null, null));
            return;
        }
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
        _bodies.Add(new BodyEffect(body, group, material));
    }

    internal static bool ContainsCanvasGroup(Node node) =>
        node is CanvasGroup || node.GetChildren().Any(ContainsCanvasGroup);

    internal static bool SupportsNativeHue(Material? material) =>
        material == null || material is ShaderMaterial { Shader: { } shader } &&
        shader.GetShaderUniformList().Any(uniform => uniform.AsGodotDictionary()["name"].AsString() == "h");

    [HarmonyPatch(typeof(NCreatureVisuals), nameof(NCreatureVisuals.SetScaleAndHue))]
    internal static class CorruptedVisualHuePatch
    {
        private static void Prefix(NCreatureVisuals __instance, ref float hue)
        {
            if (!Mathf.IsZeroApprox(hue) &&
                __instance.GetNodeOrNull<CorruptedPlayerCorruption>("BoundEcho") != null &&
                !SupportsNativeHue(__instance.SpineBody?.GetNormalMaterial()))
                hue = 0f;
        }
    }

    public override void _Ready()
    {
        _creature = _visuals.GetParentOrNull<NCreature>();
    }

    public override void _Process(double delta)
    {
        _time += delta;
        bool alive = _persistAfterDeath || _creature?.Entity.IsDead != true;
        _strength = _animateBinding && alive
            ? Intensity * Mathf.SmoothStep(0f, 1f, (float)(_time - 0.5) / 1.2f)
            : Mathf.MoveToward(_strength, alive ? Intensity : 0f, (float)delta * 3f);
        var bindingStrength = _animateBinding && alive
            ? Intensity * Mathf.SmoothStep(0f, 1f, (float)_time / 0.8f)
            : _strength;
        bool anyVisible = false;
        foreach (var effect in _bodies)
        {
            var body = effect.Body;
            var group = effect.Group;
            if (group == null)
            {
                anyVisible |= IsInstanceValid(body) && body.GetParent() == effect.Parent && body.IsVisibleInTree();
                continue;
            }
            var material = effect.Material!;
            // Native death VFX take the original body into their own viewport.
            bool attached = IsInstanceValid(body) && body.GetParent() == group;
            group.Visible = attached && body.Visible && ApplyInheritedModulation(group);
            if (!group.Visible)
                continue;
            anyVisible = true;
            var rect = BoundsIn(group);
            if (effect.Rect is not { } previous || !previous.IsEqualApprox(rect))
            {
                float margin = Mathf.Max(rect.Size.X * 0.15f, rect.Size.Y * 0.025f) + 8f;
                group.FitMargin = margin;
                group.ClearMargin = margin + 8f;
                material.SetShaderParameter(BodyRect, new Vector4(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y));
                effect.Rect = rect;
                GeometryUpdateCount++;
            }
            material.SetShaderParameter(EffectTime, (float)_time);
            if (effect.Strength != _strength)
            {
                material.SetShaderParameter(Strength, _strength);
                effect.Strength = _strength;
            }
        }
        bool visible = bindingStrength > 0 && anyVisible;
        _back.Visible = visible;
        _front.Visible = visible;
        if (visible)
        {
            _back.UpdateEffect(BoundsIn(_back), (float)_time, bindingStrength);
            _front.UpdateEffect(BoundsIn(_front), (float)_time, bindingStrength);
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
        var inverse = new Color(Inverse(tint.R), Inverse(tint.G), Inverse(tint.B), Inverse(tint.A));
        if (group.Modulate != inverse)
            group.Modulate = inverse;
        if (group.SelfModulate != tint)
            group.SelfModulate = tint;
        return tint.A > 0;
    }

    private static float Inverse(float component) => component > 0 ? 1f / component : 1f;

    private Rect2 BoundsIn(Node2D target)
    {
        var targetTransform = target.GlobalTransform;
        var boundsTransform = _bounds.GetGlobalTransform();
        var size = _bounds.Size;
        if (_boundsCache.TryGetValue(target, out var cached) &&
            cached.Target == targetTransform && cached.Bounds == boundsTransform && cached.Size == size)
            return cached.Rect;
        var transform = targetTransform.AffineInverse() * boundsTransform;
        var rect = new Rect2(transform * Vector2.Zero, Vector2.Zero);
        rect = rect.Expand(transform * new Vector2(size.X, 0));
        rect = rect.Expand(transform * size);
        rect = rect.Expand(transform * new Vector2(0, size.Y));
        if (rect.Size.X <= 0 || rect.Size.Y <= 0)
            throw new InvalidOperationException("Bound Echo requires non-empty character bounds.");
        cached ??= new BoundsCache();
        cached.Target = targetTransform;
        cached.Bounds = boundsTransform;
        cached.Size = size;
        cached.Rect = rect;
        _boundsCache[target] = cached;
        return rect;
    }
}
