using Godot;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.World.Environment;

namespace ZombieApocalypse.World.Power.Loads;

/// <summary>
/// Powered cold storage. While running it halts the rot lifecycle of every
/// corpse inside <see cref="CoolingRadius"/> (see <see cref="Corpse.IsFrozen"/>),
/// preventing miasma from ever forming near the base.
///
/// Utility-tier load: it is the first appliance the grid sheds during a brownout.
/// </summary>
public partial class Freezer : PoweredDevice
{
    [ExportGroup("Cooling")]
    [Export] public float CoolingRadius = 5.0f;
    /// <summary>Real-time seconds between corpse scans.</summary>
    [Export] public float ScanInterval = 0.5f;

    [Export] public Color CoolantColor = new(0.45f, 0.75f, 1.0f);

    private float _scanTimer;
    private MeshInstance3D? _cabinet;
    private StandardMaterial3D? _cabinetMaterial;
    private OmniLight3D? _frost;

    /// <summary>Corpses currently kept frozen by this unit.</summary>
    public int FrozenCorpseCount { get; private set; }

    public override void _Ready()
    {
        NodeLabel = string.IsNullOrEmpty(NodeLabel) || NodeLabel == "Power Node" ? "Freezer" : NodeLabel;
        Priority = PowerPriority.Utility;
        RequiresInverter = true;
        base._Ready();
    }

    protected override void CreateVisuals()
    {
        _cabinetMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.82f, 0.85f, 0.88f),
            Roughness = 0.35f,
            Metallic = 0.2f
        };

        _cabinet = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(1.1f, 1.6f, 0.8f) },
            Position = new Vector3(0f, 0.8f, 0f),
            MaterialOverride = _cabinetMaterial
        };
        AddChild(_cabinet);

        _frost = new OmniLight3D
        {
            Position = new Vector3(0f, 1.7f, 0f),
            OmniRange = CoolingRadius,
            LightColor = CoolantColor,
            LightEnergy = 0.0f
        };
        AddChild(_frost);

        if (GetNodeOrNull<CollisionShape3D>("Collision") == null)
        {
            AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(1.1f, 1.6f, 0.8f) },
                Position = new Vector3(0f, 0.8f, 0f)
            });
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        if (_frost != null)
            _frost.LightEnergy = Mathf.MoveToward(_frost.LightEnergy, IsPowered ? 1.1f : 0f, dt * 3f);

        if (_cabinetMaterial != null)
        {
            Color target = IsPowered ? CoolantColor : new Color(0.6f, 0.6f, 0.62f);
            _cabinetMaterial.EmissionEnabled = IsPowered;
            _cabinetMaterial.Emission = target;
            _cabinetMaterial.AlbedoColor = _cabinetMaterial.AlbedoColor.Lerp(target, dt * 2f);
        }

        _scanTimer -= dt;
        if (_scanTimer > 0f) return;
        _scanTimer = ScanInterval;

        ApplyCooling();
    }

    /// <summary>Freezes unpowered corpses in range; releases them again when the grid drops.</summary>
    private void ApplyCooling()
    {
        int frozen = 0;
        var corpses = GetTree().GetNodesInGroup("corpses");
        float r2 = CoolingRadius * CoolingRadius;

        foreach (var node in corpses)
        {
            if (node is not Corpse corpse || !GodotObject.IsInstanceValid(corpse)) continue;

            float distSq = corpse.GlobalPosition.DistanceSquaredTo(GlobalPosition);
            if (distSq > r2) continue;

            corpse.IsFrozen = IsPowered;
            if (IsPowered) frozen++;
        }

        if (frozen != FrozenCorpseCount)
        {
            FrozenCorpseCount = frozen;
            if (IsPowered)
                GD.Print($"[Freezer] Preserving {frozen} corpse(s) — miasma suppressed.");
        }
    }
}
