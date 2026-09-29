using Godot;
using ZombieApocalypse.Core.Data;

namespace ZombieApocalypse.World.Power;

/// <summary>
/// Base class for every node in the off-grid electrical network.
///
/// A <see cref="PowerGridNode"/> is either a source (generator, battery bank),
/// a converter (inverter), or a load (lights, fences, freezers, turrets).
/// Nodes are grouped and connected into islands by <see cref="PowerGrid"/>:
///
///   • Explicit connections through <see cref="Distribution.WiringSegment"/> cables.
///   • Implicit proximity connections (≤ <see cref="PowerGrid.ProximityConnectRadius"/>)
///     representing short jumper cables between adjacent hardware.
///
/// Nodes are discovered through the "power_nodes" group, so any subclass simply
/// has to be added to a scene to join the grid.
/// </summary>
public partial class PowerGridNode : Node3D
{
    [ExportGroup("Grid Identity")]
    /// <summary>Human readable name shown in the power UI and debug logs.</summary>
    [Export] public string NodeLabel = "Power Node";

    /// <summary>Load-shedding tier used when generation cannot satisfy demand.</summary>
    [Export] public PowerPriority Priority = PowerPriority.Utility;

    /// <summary>
    /// True for AC-only hardware: it only runs when a powered
    /// <see cref="Storage.Inverter"/> exists inside the same island.
    /// </summary>
    [Export] public bool RequiresInverter = false;

    [ExportGroup("Debug Visuals")]
    [Export] public bool ShowStatusLight = true;

    // ── Runtime state ────────────────────────────────────────────────
    /// <summary>Island index assigned by the grid during the last rebuild (-1 = not in a grid).</summary>
    public int IslandId { get; internal set; } = -1;

    /// <summary>Whether this node currently receives electrical power.</summary>
    public bool IsPowered { get; private set; }

    /// <summary>True if this node can store energy (battery banks).</summary>
    public virtual bool IsStorage => false;

    /// <summary>Power this node can deliver into its island right now (watts).</summary>
    public virtual float GetSupplyWatts() => 0f;

    /// <summary>Power this node draws while powered (watts). 0 = not a load.</summary>
    public virtual float GetLoadWatts() => 0f;

    public virtual float StoredWattHours => 0f;
    public virtual float StorageCapacityWattHours => 0f;

    /// <summary>Short status string for HUD/debug output.</summary>
    public string StatusText => IsPowered ? "POWERED" : "NO POWER";

    private MeshInstance3D? _statusBulb;
    private StandardMaterial3D? _statusMaterial;

    public override void _Ready()
    {
        AddToGroup("power_nodes");
        CreateStatusBulb();
        PowerGrid.Instance?.RequestRescan();
    }

    public override void _ExitTree()
    {
        PowerGrid.Instance?.RequestRescan();
    }

    // ── Power state ──────────────────────────────────────────────────

    /// <summary>Applied by <see cref="PowerGrid"/> every simulation tick.</summary>
    public virtual void SetPowered(bool powered)
    {
        if (IsPowered == powered) return;
        IsPowered = powered;
        RefreshStatusBulb();
        OnPowerStateChanged(powered);
    }

    /// <summary>Hook for subclasses: react to being powered on/off.</summary>
    protected virtual void OnPowerStateChanged(bool powered) { }

    /// <summary>Forces an immediate visual refresh (used after scene boot).</summary>
    public void RefreshVisuals() => RefreshStatusBulb();

    // ── Debug bulb ───────────────────────────────────────────────────

    private void CreateStatusBulb()
    {
        if (!ShowStatusLight) return;

        _statusBulb = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.09f, Height = 0.18f },
            Position = new Vector3(0f, 1.5f, 0f)
        };

        _statusMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.9f, 0.15f, 0.15f),
            EmissionEnabled = true,
            Emission = new Color(0.9f, 0.15f, 0.15f),
            EmissionEnergyMultiplier = 1.5f
        };
        _statusBulb.MaterialOverride = _statusMaterial;
        AddChild(_statusBulb);
    }

    private void RefreshStatusBulb()
    {
        if (_statusMaterial == null) return;
        Color c = IsPowered ? new Color(0.25f, 0.95f, 0.35f) : new Color(0.9f, 0.15f, 0.15f);
        _statusMaterial.AlbedoColor = c;
        _statusMaterial.Emission = c;
    }
}
