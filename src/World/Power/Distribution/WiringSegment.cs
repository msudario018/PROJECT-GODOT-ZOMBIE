using Godot;

namespace ZombieApocalypse.World.Power.Distribution;

/// <summary>
/// A physical run of cable joining two <see cref="PowerGridNode"/>s.
///
/// Cables are the only connection that spans arbitrary distance. Severing a
/// cable (zombie breach, fire, player demolition, or the debug key [X]) splits
/// the network into disconnected islands — the generator may still be running
/// while the perimeter fence sits dark.
///
/// Place the node anywhere in the level and point <see cref="NodeAPath"/> /
/// <see cref="NodeBPath"/> at the two endpoints.
/// </summary>
public partial class WiringSegment : Node3D
{
    [ExportGroup("Endpoints")]
    [Export] public NodePath NodeAPath = "";
    [Export] public NodePath NodeBPath = "";

    [ExportGroup("Electrical Rating")]
    /// <summary>Maximum watts the cable can carry before it burns out.</summary>
    [Export] public float MaxThroughputWatts = 5000f;

    [ExportGroup("Visuals")]
    [Export] public bool DrawCableMesh = true;
    [Export] public float CableRadius = 0.035f;
    [Export] public Color CableColor = new(0.15f, 0.15f, 0.17f);

    /// <summary>True when the cable is cut — no power flows through it.</summary>
    public bool IsSevered { get; private set; }

    public PowerGridNode? NodeA { get; private set; }
    public PowerGridNode? NodeB { get; private set; }

    private MeshInstance3D? _cableMesh;
    private StandardMaterial3D? _cableMaterial;

    public override void _Ready()
    {
        AddToGroup("power_wires");
        RefreshEndpoints();

        if (DrawCableMesh)
            CreateCableMesh();

        PowerGrid.Instance?.RequestRescan();
    }

    /// <summary>
    /// Re-resolves both endpoints. Called on _Ready and on every grid rescan so
    /// cables still work when their endpoints are created after the cable (or
    /// when paths are assigned at runtime).
    /// </summary>
    public void RefreshEndpoints()
    {
        NodeA = ResolveEndpoint(NodeAPath);
        NodeB = ResolveEndpoint(NodeBPath);

        if (NodeA == null || NodeB == null)
        {
            GD.PrintErr($"[WiringSegment] {Name}: endpoint missing (A={NodeAPath}, B={NodeBPath}). Cable inert.");
            if (_cableMesh != null) _cableMesh.Visible = false;
        }
        else if (_cableMesh != null)
        {
            RefreshCableVisual();
        }
    }

    public override void _ExitTree()
    {
        PowerGrid.Instance?.RequestRescan();
    }

    private PowerGridNode? ResolveEndpoint(NodePath path)
    {
        if (path.IsEmpty) return null;
        return GetNodeOrNull<PowerGridNode>(path);
    }

    /// <summary>Cut the cable: its island is split on the next grid rebuild.</summary>
    public void Sever()
    {
        if (IsSevered) return;
        IsSevered = true;
        RefreshCableVisual();
        PowerGrid.Instance?.RequestRescan();
        GD.Print($"[WiringSegment] {Name} SEVERED — network will re-island.");
    }

    /// <summary>Reconnect a previously severed cable.</summary>
    public void Repair()
    {
        if (!IsSevered) return;
        IsSevered = false;
        RefreshCableVisual();
        PowerGrid.Instance?.RequestRescan();
        GD.Print($"[WiringSegment] {Name} repaired — islands may reconnect.");
    }

    public void ToggleSevered()
    {
        if (IsSevered) Repair(); else Sever();
    }

    // ── Visuals ──────────────────────────────────────────────────────

    private void CreateCableMesh()
    {
        _cableMesh = new MeshInstance3D();
        _cableMaterial = new StandardMaterial3D { AlbedoColor = CableColor, Roughness = 0.5f };
        _cableMesh.MaterialOverride = _cableMaterial;
        AddChild(_cableMesh);
        RefreshCableVisual();
    }

    private void RefreshCableVisual()
    {
        if (_cableMesh == null) return;

        if (NodeA == null || NodeB == null)
        {
            _cableMesh.Visible = false;
            return;
        }

        Vector3 a = NodeA.GlobalPosition + Vector3.Up * 1.2f;
        Vector3 b = NodeB.GlobalPosition + Vector3.Up * 1.2f;
        float length = a.DistanceTo(b);

        if (length < 0.05f)
        {
            _cableMesh.Visible = false;
            return;
        }

        _cableMesh.Visible = true;

        var cylinder = new CylinderMesh
        {
            TopRadius = CableRadius,
            BottomRadius = CableRadius,
            Height = length
        };
        _cableMesh.Mesh = cylinder;

        // CylinderMesh is aligned to local +Y: rotate it onto the (b - a) direction.
        Vector3 dir = (b - a).Normalized();
        Vector3 midpoint = (a + b) * 0.5f;

        var t = new Transform3D(AlignYTo(dir), midpoint);
        _cableMesh.GlobalTransform = t;

        if (_cableMaterial != null)
        {
            _cableMaterial.AlbedoColor = IsSevered
                ? new Color(0.45f, 0.12f, 0.1f)
                : CableColor;
        }
    }

    /// <summary>Rotation basis that maps local +Y onto the given direction.</summary>
    private static Basis AlignYTo(Vector3 dir)
    {
        Vector3 up = Vector3.Up;
        Vector3 axis = up.Cross(dir);
        if (axis.LengthSquared() < 0.000001f)
            return dir.Dot(up) > 0f ? Basis.Identity : new Basis(new Vector3(1f, 0f, 0f), Mathf.Pi);

        axis = axis.Normalized();
        float angle = Mathf.Acos(Mathf.Clamp(up.Dot(dir), -1f, 1f));
        return new Basis(axis, angle);
    }
}
