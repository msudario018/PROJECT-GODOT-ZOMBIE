using Godot;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.World.Environment;

namespace ZombieApocalypse.World.Power.Loads;

/// <summary>
/// Powered perimeter floodlight.
///
/// Draws <see cref="PoweredDevice.RequiredWatts"/> while lit and constantly
/// clears a pool of light on the ground — the base's primary night defense
/// against Stalker ambushes. Automatically switches on when the day/night
/// cycle reaches dusk.
/// </summary>
public partial class SearchLight : PoweredDevice
{
    [ExportGroup("Light")]
    [Export] public float LightEnergy = 4.0f;
    [Export] public float OmniRange = 16.0f;
    [Export] public float LightHeight = 3.2f;
    [Export] public Color LightColor = new(1.0f, 0.95f, 0.8f);

    [ExportGroup("Automation")]
    /// <summary>Only illuminate between dusk and dawn.</summary>
    [Export] public bool NightOnly = true;

    private OmniLight3D? _light;
    private MeshInstance3D? _mast;
    private StandardMaterial3D? _bulbMaterial;

    public override void _Ready()
    {
        NodeLabel = string.IsNullOrEmpty(NodeLabel) || NodeLabel == "Power Node" ? "Searchlight" : NodeLabel;
        Priority = PowerPriority.Defensive;
        base._Ready();
    }

    protected override void CreateVisuals()
    {
        var mast = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.12f, Height = LightHeight },
            Position = new Vector3(0f, LightHeight * 0.5f, 0f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.3f, 0.32f) }
        };
        AddChild(mast);
        _mast = mast;

        var head = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.22f, Height = 0.44f },
            Position = new Vector3(0f, LightHeight, 0f)
        };
        _bulbMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.2f, 0.2f, 0.2f),
            EmissionEnabled = true,
            Emission = new Color(0.1f, 0.1f, 0.1f)
        };
        head.MaterialOverride = _bulbMaterial;
        AddChild(head);

        _light = new OmniLight3D
        {
            Position = new Vector3(0f, LightHeight, 0f),
            OmniRange = OmniRange,
            LightEnergy = 0f,
            LightColor = LightColor,
            ShadowEnabled = true
        };
        AddChild(_light);

        if (GetNodeOrNull<CollisionShape3D>("Collision") == null)
        {
            AddChild(new CollisionShape3D
            {
                Shape = new CylinderShape3D { Radius = 0.2f, Height = LightHeight },
                Position = new Vector3(0f, LightHeight * 0.5f, 0f)
            });
        }
    }

    public override void _Process(double delta)
    {
        if (_light == null) return;

        bool night = DayNightCycle.Instance?.IsNight ?? false;
        bool shouldGlow = IsPowered && (!NightOnly || night);

        float target = shouldGlow ? LightEnergy : 0f;
        _light.LightEnergy = Mathf.MoveToward(_light.LightEnergy, target, (float)delta * 8f);

        if (_bulbMaterial != null)
        {
            Color c = shouldGlow ? LightColor : new Color(0.2f, 0.2f, 0.2f);
            _bulbMaterial.Emission = c;
            _bulbMaterial.AlbedoColor = c;
        }
    }
}
