using Godot;

namespace ZombieApocalypse.Core.Vision;

/// <summary>
/// Renders the fog-of-war overlay as a 3D plane with a custom shader.
///
/// Architecture:
///   - Creates a PlaneMesh covering the world area at an elevated Y position
///   - Loads fog_of_war.gdshader and configures a ShaderMaterial
///   - Each frame, the shader reads the fog texture from FogOfWarSystem
///     and draws black with variable alpha based on visibility state
///   - Uses depth_test_disabled so the fog renders on top of all 3D geometry
///   - The shader maps fragment world positions to fog texture UVs
///
/// Must be a sibling of (or have access to) FogOfWarSystem.
/// </summary>
public partial class FogOfWarRenderer : MeshInstance3D
{
    public static FogOfWarRenderer? Instance { get; private set; }

    // ── Configuration ──────────────────────────────────────────────
    [Export] public float FogPlaneHeight = 2.5f;
    [Export] public float FogPlanePadding = 2.0f;
    [Export] public bool FogEnabled = false;

    // ── References ─────────────────────────────────────────────────
    private FogOfWarSystem? _fogSystem;
    private ShaderMaterial? _material;
    private bool _initialized;

    public override void _Ready()
    {
        Instance = this;
        Visible = FogEnabled;

        // Find sibling FogOfWarSystem (should be in the same parent container)
        _fogSystem = FogOfWarSystem.Instance
                  ?? GetParent()?.GetNodeOrNull<FogOfWarSystem>("FogOfWarSystem")
                  ?? GetTree()?.Root?.FindChild("FogOfWarSystem", true, false) as FogOfWarSystem;

        if (_fogSystem == null)
        {
            GD.PrintErr("[FogOfWarRenderer] FogOfWarSystem not found. Fog overlay disabled.");
            return;
        }

        SetupFogPlane();
        _initialized = true;

        GD.Print($"[FogOfWarRenderer] Fog overlay initialized. " +
                 $"Plane: {_fogSystem.WorldSize.X + FogPlanePadding * 2}×{_fogSystem.WorldSize.Y + FogPlanePadding * 2}m " +
                 $"at Y={FogPlaneHeight} (Enabled={FogEnabled}, press F1 to toggle)");
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.F1)
        {
            ToggleFog();
        }
    }

    /// <summary>
    /// Toggle fog of war on or off for debugging / visibility.
    /// </summary>
    public void ToggleFog()
    {
        FogEnabled = !FogEnabled;
        Visible = FogEnabled;
        _material?.SetShaderParameter("fog_enabled", FogEnabled);
        GD.Print($"[FogOfWarRenderer] Fog of War toggled: {(FogEnabled ? "ON" : "OFF")}");
    }

    public override void _Process(double delta)
    {
        // Ensure the texture reference stays current (texture data is
        // updated in-place by FogOfWarSystem, but we re-set the uniform
        // in case the ImageTexture was recreated)
        if (_initialized && _fogSystem?.FogTexture != null && _material != null)
        {
            _material.SetShaderParameter("fog_texture", _fogSystem.FogTexture);
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Setup
    // ════════════════════════════════════════════════════════════════

    private void SetupFogPlane()
    {
        var worldOrigin = _fogSystem!.WorldOrigin;
        var worldSize = _fogSystem.WorldSize;

        // Create a plane mesh that covers the entire world with padding
        var planeMesh = new PlaneMesh();
        planeMesh.Size = new Vector2(
            worldSize.X + FogPlanePadding * 2f,
            worldSize.Y + FogPlanePadding * 2f
        );
        Mesh = planeMesh;

        // Position the fog plane at the center of the world, elevated
        float centerX = worldOrigin.X + worldSize.X * 0.5f;
        float centerZ = worldOrigin.Y + worldSize.Y * 0.5f;
        GlobalPosition = new Vector3(centerX, FogPlaneHeight, centerZ);

        // Don't cast shadows
        CastShadow = ShadowCastingSetting.Off;

        // Load and configure the fog shader
        var shader = GD.Load<Shader>("res://assets/shaders/fog_of_war.gdshader");
        if (shader == null)
        {
            GD.PrintErr("[FogOfWarRenderer] fog_of_war.gdshader not found!");
            return;
        }

        _material = new ShaderMaterial();
        _material.Shader = shader;
        _material.RenderPriority = 10; // Render after other transparent objects

        // Pass world bounds to the shader
        _material.SetShaderParameter("grid_origin", worldOrigin);
        _material.SetShaderParameter("grid_size", worldSize);
        _material.SetShaderParameter("fog_enabled", FogEnabled);

        // Link the fog texture
        if (_fogSystem.FogTexture != null)
            _material.SetShaderParameter("fog_texture", _fogSystem.FogTexture);

        MaterialOverride = _material;
    }
}
