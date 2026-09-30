namespace ZombieApocalypse.World.Environment;

using Godot;

/// <summary>
/// Lighting and post-processing pipeline (Stage 9).
///
/// Applies the project's look in code rather than relying on .tscn sub-resources,
/// so every scene that instantiates it gets the same treatment and the values
/// are verifiable from the self-test:
///   - SSAO for ground contact shadows under characters and walls
///   - ACES filmic tonemapping so bright muzzle flashes roll off instead of clipping
///   - Restrained glow (high threshold) so only genuine light sources bloom
///   - A soft-shadowed sun at an isometric-friendly angle
///
/// The day/night cycle still drives sun energy and ambient afterwards; this only
/// owns the settings the cycle does not touch.
/// </summary>
public partial class VisualPipeline : Node
{
    public static VisualPipeline? Instance { get; private set; }

    [ExportGroup("Ambient Occlusion")]
    [Export] public bool EnableSsao = true;
    /// <summary>World-space occlusion radius (metres).</summary>
    [Export] public float SsaoRadius = 1.5f;
    /// <summary>Occlusion strength.</summary>
    [Export] public float SsaoIntensity = 2.0f;
    /// <summary>Falloff sharpness of the occlusion.</summary>
    [Export] public float SsaoPower = 1.5f;

    [ExportGroup("Tonemapping")]
    [Export] public bool UseAcesTonemap = true;
    [Export] public float TonemapExposure = 1.0f;
    [Export] public float TonemapWhite = 6.0f;

    [ExportGroup("Glow")]
    [Export] public bool EnableGlow = true;
    /// <summary>Only luminance above this blooms (keeps the effect subtle).</summary>
    [Export] public float GlowThreshold = 1.05f;
    [Export] public float GlowIntensity = 0.35f;
    [Export] public float GlowBloom = 0.05f;

    [ExportGroup("Sun")]
    /// <summary>Direction the key light travels, in degrees (isometric-ish rake).</summary>
    [Export] public Vector3 SunEulerDegrees = new(-52f, -38f, 0f);
    [Export] public bool EnableSunShadows = true;
    [Export] public float SunShadowOpacity = 0.85f;
    [Export] public float DirectionalShadowMaxDistance = 70f;

    private WorldEnvironment? _worldEnvironment;
    private DirectionalLight3D? _sun;

    /// <summary>True once the pipeline has applied its settings.</summary>
    public bool IsApplied { get; private set; }
    /// <summary>The WorldEnvironment this pipeline configured (may be null).</summary>
    public WorldEnvironment? WorldEnvironment => _worldEnvironment;
    /// <summary>The key light this pipeline configured (may be null).</summary>
    public DirectionalLight3D? Sun => _sun;

    public override void _Ready()
    {
        Instance = this;
        Apply();
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Find the scene's environment and sun, then configure both.</summary>
    public void Apply()
    {
        ResolveNodes();
        if (_worldEnvironment?.Environment != null) ConfigureEnvironment(_worldEnvironment.Environment);
        if (_sun != null) ConfigureSun(_sun);

        IsApplied = true;
        GD.Print($"[VisualPipeline] SSAO {(EnableSsao ? $"r{SsaoRadius} i{SsaoIntensity}" : "off")}, " +
                 $"tonemap {(UseAcesTonemap ? "ACES" : "linear")}, " +
                 $"glow {(EnableGlow ? $"threshold {GlowThreshold}" : "off")}, " +
                 $"sun shadows {(EnableSunShadows ? "on" : "off")}.");
    }

    private void ResolveNodes()
    {
        var tree = GetTree();
        var scene = tree?.CurrentScene;

        _worldEnvironment ??= scene?.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
        _sun ??= scene?.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D")
                 ?? scene?.GetNodeOrNull<DirectionalLight3D>("Sun")
                 ?? (scene?.FindChild("DirectionalLight3D", true, false) as DirectionalLight3D);
    }

    /// <summary>SSAO, tonemapping and glow on the scene environment.</summary>
    public void ConfigureEnvironment(Environment env)
    {
        // SSAO: contact shadows keep characters and walls grounded.
        env.SsaoEnabled = EnableSsao;
        if (EnableSsao)
        {
            env.SsaoRadius = SsaoRadius;
            env.SsaoIntensity = SsaoIntensity;
            env.SsaoPower = SsaoPower;
            env.SsaoLightAffect = 0.15f;   // keep occlusion from crushing the key light
        }

        // Tonemapping: ACES rolls off muzzle flashes and floodlights gracefully.
        env.TonemapMode = UseAcesTonemap ? Environment.ToneMapper.Aces : Environment.ToneMapper.Linear;
        env.TonemapExposure = TonemapExposure;
        env.TonemapWhite = TonemapWhite;

        // Glow with a high threshold: only real light sources bloom.
        if (env.HasMethod("set_glow_enabled"))
        {
            env.Set("glow_enabled", EnableGlow);
            if (EnableGlow)
            {
                env.Set("glow_intensity", GlowIntensity);
                env.Set("glow_bloom", GlowBloom);
                env.Set("glow_hdr_threshold", GlowThreshold);
                env.Set("glow_blend_mode", (int)Environment.GlowBlendModeEnum.Additive);
            }
        }
    }

    /// <summary>Soft shadows and the isometric sun angle.</summary>
    public void ConfigureSun(DirectionalLight3D sun)
    {
        sun.RotationDegrees = SunEulerDegrees;
        sun.ShadowEnabled = EnableSunShadows;

        if (!EnableSunShadows) return;

        // Soft contact shadows: a wider filter reads better at this camera angle.
        sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
        sun.DirectionalShadowMaxDistance = DirectionalShadowMaxDistance;
        sun.ShadowOpacity = SunShadowOpacity;
        sun.ShadowBlur = 1.4f;
        sun.ShadowNormalBias = 1.2f;
    }
}
