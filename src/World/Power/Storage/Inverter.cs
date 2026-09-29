using Godot;
using ZombieApocalypse.Core.Data;

namespace ZombieApocalypse.World.Power.Storage;

/// <summary>
/// DC → AC inverter. It is the gate every AC-only appliance depends on:
/// if the island has no powered inverter, <see cref="PowerGridNode.RequiresInverter"/>
/// loads are shed even when the generator is running at full output.
///
/// Draws a small standby load and applies <see cref="Efficiency"/> losses.
/// </summary>
public partial class Inverter : PowerGridNode
{
    [ExportGroup("Conversion")]
    /// <summary>DC→AC conversion efficiency (0..1).</summary>
    [Export(PropertyHint.Range, "0.5,1,0.01")] public float Efficiency = 0.92f;
    /// <summary>Standby self-consumption (watts) drawn from the island.</summary>
    [Export] public float IdleLoadWatts = 30f;
    /// <summary>Maximum AC watts this unit can deliver.</summary>
    [Export] public float MaxThroughputWatts = 1500f;

    public override float GetLoadWatts() => IdleLoadWatts;

    /// <summary>AC watts available downstream of this inverter at the current charge level.</summary>
    public float AvailableAcWatts => IsPowered ? MaxThroughputWatts * Efficiency : 0f;

    public override void _Ready()
    {
        base._Ready();
        NodeLabel = string.IsNullOrEmpty(NodeLabel) || NodeLabel == "Power Node" ? "Inverter" : NodeLabel;
        Priority = PowerPriority.Critical;
        CreateVisuals();
        RefreshVisuals();
    }

    protected override void OnPowerStateChanged(bool powered)
    {
        GD.Print($"[Inverter] {NodeLabel} {(powered ? "ON — AC bus live" : "OFF — AC loads shed")}");
    }

    private void CreateVisuals()
    {
        if (GetNodeOrNull<MeshInstance3D>("Mesh") == null)
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.7f, 0.5f, 0.5f) },
                Position = new Vector3(0f, 0.25f, 0f),
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.35f, 0.35f, 0.4f),
                    EmissionEnabled = true,
                    Emission = new Color(0.05f, 0.35f, 0.15f)
                }
            });
        }

        if (GetNodeOrNull<CollisionShape3D>("Collision") == null)
        {
            AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(0.7f, 0.5f, 0.5f) },
                Position = new Vector3(0f, 0.25f, 0f)
            });
        }
    }
}
