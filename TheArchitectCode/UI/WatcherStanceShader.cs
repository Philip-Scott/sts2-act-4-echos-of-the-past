using Godot;

namespace TheArchitect.TheArchitectCode.UI;

internal static class WatcherStanceShader
{
    private static readonly Lazy<Shader> Shader = new(() => new Shader
    {
        Code = """
            shader_type canvas_item;
            render_mode unshaded;

            uniform vec4 body_rect;
            uniform float effect_time = 0.0;
            uniform bool wrath = false;
            uniform bool front_layer = false;
            varying vec2 local_position;
            varying vec4 inherited_color;

            void vertex() {
                local_position = VERTEX;
                inherited_color = COLOR;
            }

            float glow(float distance, float width) {
                return exp(-distance * distance / (width * width));
            }

            void fragment() {
                vec2 p = (local_position - body_rect.xy) / body_rect.zw;
                p.x -= 0.5;
                float t = effect_time;
                float light = 0.0;
                float core = 0.0;
                float mist = 0.0;
                if (wrath) {
                    for (int i = 0; i < 18; i++) {
                        float seed = float(i);
                        bool front = (i % 3) == 0;
                        if (front == front_layer) {
                            float x = (fract(seed * 0.618034) - 0.5) * 1.15;
                            float speed = 0.24 + fract(seed * 0.317) * 0.22;
                            float y = 1.12 - fract(t * speed + seed * 0.137) * 1.26;
                            float sway = 0.024 * sin(t * 2.3 + seed * 2.0);
                            float length = 0.035 + fract(seed * 0.421) * 0.065;
                            float tip = glow(p.y - y, length);
                            float fade = smoothstep(-0.12, 0.12, y) * (1.0 - smoothstep(0.9, 1.12, y));
                            float d = p.x - x - sway;
                            light += glow(d, 0.020) * tip * fade * 0.7;
                            core += glow(d, 0.004) * tip * fade;
                        }
                    }
                    if (!front_layer) {
                        float pulse = 0.78 + 0.22 * sin(t * 3.6);
                        float flame = 0.82 + 0.18 * sin(p.x * 29.0 + t * 2.6 + sin(p.y * 12.0 - t * 4.0));
                        mist = glow(p.x, 0.42) * glow(p.y - 0.62, 0.5) * pulse * flame * 0.34;
                        light += glow(p.x, 0.48) * glow(p.y - 0.99, 0.055) * pulse * 0.5;
                    }
                } else {
                    for (int i = 0; i < 7; i++) {
                        float seed = float(i);
                        float cycle = fract(t * 0.105 + seed / 7.0);
                        float y = 1.02 - cycle * 1.12;
                        float rx = 0.50 + 0.06 * sin(seed * 2.1 + t * 0.4);
                        float ry = 0.043;
                        vec2 q = vec2(p.x / rx, (p.y - y + p.x * 0.12) / ry);
                        float angle = atan(q.y, q.x);
                        bool front = q.y >= 0.0;
                        float phase = angle - t * 1.9 - seed * 2.4;
                        float arc = pow(max(0.0, cos(phase)), 5.0);
                        float fade = sin(cycle * 3.141593);
                        if (front == front_layer) {
                            // Use the ellipse gradient so the flat rings do not smear sideways.
                            float radius = length(q);
                            vec2 gradient = vec2(q.x / rx + 0.12 * q.y / ry, q.y / ry);
                            float d = (radius - 1.0) /
                                max(length(gradient) / max(radius, 0.0001), 1.0 / rx);
                            light += glow(d, 0.017) * arc * fade * 0.58;
                            core += glow(d, 0.0035) * arc * fade;
                        }
                    }
                    if (!front_layer) {
                        float breath = 0.84 + 0.16 * sin(t * 1.25);
                        mist = glow(p.x, 0.44) * glow(p.y - 0.66, 0.50) * breath * 0.17;
                        light += glow(p.x, 0.50) * glow(p.y - 0.99, 0.05) * breath * 0.23;
                    }
                }
                vec3 color = wrath ? vec3(1.0, 0.025, 0.09) : vec3(0.36, 0.76, 1.0);
                color = mix(color, wrath ? vec3(1.0, 0.48, 0.38) : vec3(0.84, 0.97, 1.0),
                    clamp(core, 0.0, 1.0));
                float alpha = clamp(mist + light + core * 0.8, 0.0, front_layer ? 0.8 : 0.92);
                float horizontal_fade = 1.0 - smoothstep(0.62, 0.80, abs(p.x));
                float vertical_fade = smoothstep(-0.16, -0.10, p.y) *
                    (1.0 - smoothstep(1.04, 1.11, p.y));
                alpha *= horizontal_fade * vertical_fade;
                COLOR = vec4(color * inherited_color.rgb, alpha * inherited_color.a);
            }
            """
    });

    internal static Shader Resource => Shader.Value;
}
