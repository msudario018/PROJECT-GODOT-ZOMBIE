using Godot;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Entities.Zombies;

namespace ZombieApocalypse.World.Power.Loads;

/// <summary>
/// Electrified fence energizer: shocks every zombie inside
/// <see cref="ShockRadius"/> while powered.
///
/// Trade-off: the arc crackle is loud, so a powered fence keeps killing but also
/// keeps calling more of the horde toward the perimeter.
/// </summary>
public partial class ElectricFence : PoweredDevice
{
    [ExportGroup("Shock Field")]
    [Export] public float ShockRadius = 3.0f;
    [Export] public float ShockDamagePerSecond = 22.0f;
    [Export] public float ShockKnockback = 6.0f;
    [Export] public float ShockTickInterval = 0.4f;

    [ExportGroup("Noise")]
    [Export] public float CrackleNoiseRadius = 25.0f;
    [Export] public float CrackleInterval = 3.0f;

    private float _shockTimer;
    private float _noiseTimer;
    private MeshInstance3D? _fieldMesh;
    private StandardMaterial3D? _fieldMaterial;

    public override void _Ready()
    {
        NodeLabel = string.IsNullOrEmpty(NodeLabel) || NodeLabel == "Power Node" ? "Electric Fence" : NodeLabel;
        Priority = PowerPriority.Defensive;
        RequiresInverter = true;
        base._Ready();
    }

    protected override void CreateVisuals()
    {
        _fieldMesh = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = ShockRadius,
                BottomRadius = ShockRadius,
                Height = 0.06f
            },
            Position = new Vector3(0f, 0.05f, 0f),
            Visible = false
        };

        _fieldMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.4f, 0.8f, 1f, 0.14f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled = true,
            Emission = new Color(0.2f, 0.6f, 1f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        _fieldMesh.MaterialOverride = _fieldMaterial;
        AddChild(_fieldMesh);

        if (GetNodeOrNull<MeshInstance3D>("Mesh") == null)
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.3f, 1.6f, 0.3f) },
                Position = new Vector3(0f, 0.8f, 0f),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.45f, 0.15f) }
            });
        }

        if (GetNodeOrNull<CollisionShape3D>("Collision") == null)
        {
            AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(0.3f, 1.6f, 0.3f) },
                Position = new Vector3(0f, 0.8f, 0f)
            });
        }
    }

    protected override void OnPowerStateChanged(bool powered)
    {
        base.OnPowerStateChanged(powered);
        if (_fieldMesh != null) _fieldMesh.Visible = powered;
        if (_fieldMaterial != null)
            _fieldMaterial.AlbedoColor = powered
                ? new Color(0.4f, 0.8f, 1f, 0.14f)
                : new Color(0.2f, 0.2f, 0.2f, 0.08f);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        if (!IsPowered)
        {
            _shockTimer = 0f;
            return;
        }

        _shockTimer -= dt;
        if (_shockTimer <= 0f)
        {
            _shockTimer = ShockTickInterval;
            ShockNearbyZombies();
        }

        _noiseTimer -= dt;
        if (_noiseTimer <= 0f)
        {
            _noiseTimer = CrackleInterval;
            Core.Autoloads.EventBus.Instance?.EmitSound(GlobalPosition, CrackleNoiseRadius, AudioSourceType.Environmental);
        }
    }

    private void ShockNearbyZombies()
    {
        var candidates = ZombieBase.SharedSpatialGrid.QueryRadius(GlobalPosition, ShockRadius);
        foreach (var node in candidates)
        {
            if (node is not ZombieBase zombie || zombie.IsDead) continue;

            Vector3 away = zombie.GlobalPosition - GlobalPosition;
            away.Y = 0f;
            if (away.LengthSquared() > 0.0001f) away = away.Normalized();

            zombie.ApplyDamage(
                ShockDamagePerSecond * ShockTickInterval,
                DamageType.Electric,
                this,
                away * ShockKnockback);
        }
    }
}
