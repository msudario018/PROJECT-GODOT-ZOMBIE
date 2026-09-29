using Godot;
using ZombieApocalypse.Core.Data;

namespace ZombieApocalypse.World.Power.Loads;

/// <summary>
/// Base class for every powered appliance (light, fence, freezer, turret…).
///
/// Subclasses declare how many watts they need and what to do when the grid
/// connects or drops them. Power state is applied by <see cref="PowerGrid"/>
/// through <see cref="PowerGridNode.SetPowered"/>.
/// </summary>
public partial class PoweredDevice : PowerGridNode
{
    [ExportGroup("Load")]
    /// <summary>Continuous draw while running (watts).</summary>
    [Export] public float RequiredWatts = 120f;

    /// <summary>Load-shedding tier: Critical survives, Utility is cut first.</summary>
    [ExportGroup("Restrictions")]
    /// <summary>May this device be built before its power source exists?</summary>
    [Export] public bool AllowInsufficientPower = true;

    public override float GetLoadWatts() => RequiredWatts;

    /// <summary>True while the device is doing useful work.</summary>
    public bool IsRunning => IsPowered;

    public override void _Ready()
    {
        base._Ready();
        if (string.IsNullOrEmpty(NodeLabel) || NodeLabel == "Power Node")
            NodeLabel = GetType().Name;
        CreateVisuals();
        RefreshVisuals();
    }

    /// <summary>Optional per-device body; override to build meshes lazily.</summary>
    protected virtual void CreateVisuals() { }

    protected override void OnPowerStateChanged(bool powered)
    {
        GD.Print($"[{GetType().Name}] {Name} {(powered ? $"POWERED ({RequiredWatts:F0}W)" : "NO POWER — offline")}");
    }

    /// <summary>Convenience helper for HUD rows.</summary>
    public string GetStatusLine() => $"{NodeLabel}: {StatusText} ({RequiredWatts:F0}W)";
}
