using Godot;

namespace ZombieApocalypse.Systems.Combat;

/// <summary>
/// Manages visual ballistic tracers and bullet impact feedback.
/// </summary>
public partial class ProjectileManager : Node3D
{
    public static ProjectileManager? Instance { get; private set; }

    private StandardMaterial3D? _tracerMaterial;
    private StandardMaterial3D? _impactMaterial;

    public override void _Ready()
    {
        Instance = this;

        _tracerMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1.0f, 0.9f, 0.4f, 0.85f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha
        };

        _impactMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1.0f, 0.8f, 0.3f),
        };
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Spawns a high-speed visual bullet tracer between two points.
    /// </summary>
    public void SpawnTracer(Vector3 start, Vector3 end)
    {
        var tracer = new MeshInstance3D();
        var diff = end - start;
        float length = diff.Length();
        if (length < 0.001f) return;   // zero-length: no meaningful direction

        var cylinder = new CylinderMesh
        {
            TopRadius = 0.035f,
            BottomRadius = 0.035f,
            Height = length
        };
        tracer.Mesh = cylinder;
        tracer.MaterialOverride = _tracerMaterial;

        // Must be in the tree before assigning a global transform.
        AddChild(tracer);

        // Orient in global space, with the cylinder's local +Y laid directly along
        // the shot. The basis is assembled explicitly rather than via LookAt +
        // RotateObjectLocal, because Node3D.Rotate* and Basis.Rotated rotate in
        // parent space, not around the node's own axis — which is what made
        // tracers point somewhere unrelated to where the bullet went.
        Vector3 direction = diff / length;

        Vector3 yAxis = direction;                                  // length axis
        Vector3 xAxis = Vector3.Up.Cross(yAxis).Normalized();
        if (xAxis.LengthSquared() < 0.0001f)
            xAxis = Vector3.Forward.Cross(yAxis).Normalized();      // straight up/down
        if (xAxis.LengthSquared() < 0.0001f)
            xAxis = Vector3.Right;

        Vector3 zAxis = xAxis.Cross(yAxis).Normalized();            // completes the set
        tracer.GlobalBasis = new Basis(xAxis, yAxis, zAxis);

        // Position at the midpoint, in global space.
        tracer.GlobalPosition = (start + end) * 0.5f;

        var tween = CreateTween();
        tween.TweenProperty(tracer, "scale", new Vector3(0.1f, 1f, 0.1f), 0.06f);
        tween.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(tracer))
                tracer.QueueFree();
        }));
    }

    /// <summary>
    /// Spawns a brief impact spark at a hit surface.
    /// </summary>
    public void SpawnImpact(Vector3 position, Vector3 normal)
    {
        var impact = new MeshInstance3D();
        var sphere = new SphereMesh { Radius = 0.12f, Height = 0.24f };
        impact.Mesh = sphere;
        impact.MaterialOverride = _impactMaterial;
        impact.Position = position + normal * 0.05f;

        AddChild(impact);

        var tween = CreateTween();
        tween.TweenProperty(impact, "scale", Vector3.Zero, 0.15f);
        tween.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(impact))
                impact.QueueFree();
        }));
    }
}
