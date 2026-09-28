shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, unshaded, shadows_disabled;

// Fog texture from FogOfWarSystem (R channel = fog opacity)
// R=0.0: fully visible (no fog)
// R=0.65: explored (dimmed)
// R=1.0: hidden (opaque black)
uniform sampler2D fog_texture : filter_linear, repeat_clamp_to_edge;

// World-space bounds of the fog grid (XZ plane)
uniform vec2 grid_origin = vec2(-25.0, -25.0);
uniform vec2 grid_size = vec2(50.0, 50.0);

// Fog appearance
uniform vec4 fog_color : source_color = vec4(0.04, 0.04, 0.06, 0.95);
uniform bool fog_enabled = true;

// World position passed from vertex to fragment shader
varying vec3 world_pos;

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    // If fog is disabled, immediately discard fragment to leave 3D scene untouched
    if (!fog_enabled) {
        discard;
    }

    // Map world XZ position to fog texture UV coordinates
    vec2 uv = (world_pos.xz - grid_origin) / grid_size;

    // Check boundary
    float out_of_bounds = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
    float outside_alpha = 1.0 - out_of_bounds;

    vec2 clamped_uv = clamp(uv, vec2(0.0), vec2(1.0));
    float fog_alpha = texture(fog_texture, clamped_uv).r;
    float final_alpha = max(fog_alpha, outside_alpha);

    // If fully visible / cleared, discard so the raw 3D scene renders cleanly with zero overlay
    if (final_alpha <= 0.02) {
        discard;
    }

    ALBEDO = fog_color.rgb;
    ALPHA = clamp(final_alpha * fog_color.a, 0.0, 1.0);
}
