using Godot;
using ZombieApocalypse.Core.Data;

namespace ZombieApocalypse.World.Power.Storage;

/// <summary>
/// Battery bank: stores surplus generation and covers deficits.
///
/// Depth-of-discharge (DoD) tracking degrades capacity over the battery's life,
/// mirroring the blueprint rule: discharging below <see cref="SafeDepthOfDischarge"/>
/// costs a fraction of capacity per deep cycle.
/// </summary>
public partial class BatteryBank : PowerGridNode
{
    [ExportGroup("Capacity")]
    /// <summary>Usable energy capacity in watt-hours.</summary>
    [Export] public float CapacityWattHours = 1200f;
    /// <summary>Charge level reported to the grid on startup (0..1).</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float InitialChargePercent = 1.0f;

    [ExportGroup("Rates")]
    [Export] public float MaxChargeRateWatts = 600f;
    [Export] public float MaxDischargeRateWatts = 900f;

    [ExportGroup("Degradation")]
    /// <summary>Discharging deeper than this damages the cells.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float SafeDepthOfDischarge = 0.5f;
    /// <summary>Capacity lost (percent) per deep-discharge cycle.</summary>
    [Export] public float CapacityLossPerDeepCyclePercent = 0.1f;

    public override bool IsStorage => true;
    public float ChargeWattHours { get; private set; }
    public override float StoredWattHours => ChargeWattHours;
    public override float StorageCapacityWattHours => CapacityWattHours;

    public float ChargePercent => CapacityWattHours > 0.01f
        ? Mathf.Clamp(ChargeWattHours / CapacityWattHours, 0f, 1f)
        : 0f;

    /// <summary>Discharge available right now, limited by the inverter draw rate.</summary>
    public override float GetSupplyWatts() => ChargeWattHours > 0.01f ? MaxDischargeRateWatts : 0f;

    private bool _deepDischarged;

    public override void _Ready()
    {
        base._Ready();
        NodeLabel = string.IsNullOrEmpty(NodeLabel) || NodeLabel == "Power Node" ? "Battery Bank" : NodeLabel;
        Priority = PowerPriority.Critical;

        ChargeWattHours = CapacityWattHours * Mathf.Clamp(InitialChargePercent, 0f, 1f);
        CreateVisuals();
        RefreshVisuals();
    }

    /// <summary>Adds energy (charger / solar surplus). Returns watt-hours accepted.</summary>
    public float AddWattHours(float wattHours)
    {
        if (wattHours <= 0f) return 0f;
        float before = ChargeWattHours;
        ChargeWattHours = Mathf.Min(CapacityWattHours, ChargeWattHours + wattHours);

        // Recovering above the safe floor ends the deep-discharge episode
        if (_deepDischarged && ChargePercent > SafeDepthOfDischarge + 0.05f)
            _deepDischarged = false;

        return ChargeWattHours - before;
    }

    /// <summary>
    /// Removes energy (load deficit). Returns watt-hours actually delivered.
    /// Energy drawn below the safe depth incrementally degrades capacity.
    /// </summary>
    public float DrawWattHours(float wattHours)
    {
        if (wattHours <= 0f) return 0f;

        float delivered = Mathf.Min(ChargeWattHours, wattHours);
        ChargeWattHours -= delivered;

        if (ChargePercent <= SafeDepthOfDischarge)
            _deepDischarged = true;

        if (ChargePercent <= 0.001f && _deepDischarged)
        {
            _deepDischarged = false;
            float loss = CapacityWattHours * (CapacityLossPerDeepCyclePercent / 100f);
            CapacityWattHours = Mathf.Max(50f, CapacityWattHours - loss);
            GD.Print($"[BatteryBank] Deep cycle → capacity degraded to {CapacityWattHours:F0}Wh");
        }

        return delivered;
    }

    /// <summary>
    /// Forces the stored energy level (save/load restoration and tests).
    /// </summary>
    public void SetChargeWattHours(float wattHours)
    {
        ChargeWattHours = Mathf.Clamp(wattHours, 0f, CapacityWattHours);
    }

    // ── Visuals ──────────────────────────────────────────────────────

    private void CreateVisuals()
    {
        if (GetNodeOrNull<MeshInstance3D>("Mesh") == null)
        {
            var rack = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(1.2f, 1.4f, 0.7f) },
                Position = new Vector3(0f, 0.7f, 0f),
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.18f, 0.22f, 0.3f),
                    Roughness = 0.5f
                }
            };
            AddChild(rack);
        }

        if (GetNodeOrNull<CollisionShape3D>("Collision") == null)
        {
            AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(1.2f, 1.4f, 0.7f) },
                Position = new Vector3(0f, 0.7f, 0f)
            });
        }
    }
}
