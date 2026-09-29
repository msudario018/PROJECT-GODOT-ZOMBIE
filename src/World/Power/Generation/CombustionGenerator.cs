using Godot;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Data;

namespace ZombieApocalypse.World.Power.Generation;

/// <summary>
/// Portable combustion generator: the workhorse DC source of the base.
///
///   • Burns fuel while running; each fuel canister inserted refuels the tank.
///   • Generates <see cref="SupplyWatts"/> whenever fuel remains.
///   • Emits a continuous acoustic footprint (the generator hum) that pulls
///     zombies toward the base — the classic trade-off of running power.
///
/// Interact with [E] while carrying a fuel canister to refuel.
/// </summary>
public partial class CombustionGenerator : PowerGridNode
{
    [ExportGroup("Output")]
    [Export] public float SupplyWatts = 1200f;

    [ExportGroup("Fuel")]
    /// <summary>Fuel currently in the tank, in "canister equivalents" (0..MaxCanisters).</summary>
    [Export] public float FuelCanisters = 2.0f;
    [Export] public float MaxCanisters = 4.0f;
    /// <summary>Real-time hours of runtime provided by one full canister.</summary>
    [Export] public float HoursPerCanister = 0.25f;

    [ExportGroup("Noise")]
    /// <summary>Acoustic radius of the running generator (attracts hordes).</summary>
    [Export] public float NoiseRadius = 55.0f;
    [Export] public float NoiseInterval = 2.5f;

    [ExportGroup("Controls")]
    [Export] public bool Running = true;

    public float FuelPercent => MaxCanisters > 0.01f ? Mathf.Clamp(FuelCanisters / MaxCanisters, 0f, 1f) : 0f;

    private float _noiseTimer;
    private AudioEmitterComponent? _audioEmitter;
    private Label3D? _promptLabel;
    private MeshInstance3D? _flywheel;
    private float _flywheelAngle;

    public override void _Ready()
    {
        base._Ready();
        AddToGroup("interactables");

        NodeLabel = string.IsNullOrEmpty(NodeLabel) || NodeLabel == "Power Node" ? "Combustion Generator" : NodeLabel;
        Priority = PowerPriority.Critical;

        _audioEmitter = GetNodeOrNull<AudioEmitterComponent>("AudioEmitterComponent");
        CreateVisuals();
        RefreshVisuals();
    }

    public override float GetSupplyWatts() => (Running && FuelCanisters > 0.001f) ? SupplyWatts : 0f;

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        bool burning = Running && FuelCanisters > 0.001f;
        if (burning)
        {
            // Consume fuel: one canister per `HoursPerCanister` real-time hours
            float cansPerSecond = HoursPerCanister > 0.001f ? 1f / (HoursPerCanister * 3600f) : 0f;
            FuelCanisters = Mathf.Max(0f, FuelCanisters - cansPerSecond * dt);

            if (_flywheel != null)
            {
                _flywheelAngle += dt * 12f;
                _flywheel.Rotation = new Vector3(0f, 0f, _flywheelAngle);
            }

            _noiseTimer -= dt;
            if (_noiseTimer <= 0f)
            {
                _noiseTimer = NoiseInterval;
                if (_audioEmitter != null)
                    _audioEmitter.EmitCustomSound(NoiseRadius, AudioSourceType.Generator);
                else
                    EventBus.Instance?.EmitSound(GlobalPosition, NoiseRadius, AudioSourceType.Generator);
            }

            if (FuelCanisters <= 0f)
                GD.Print($"[CombustionGenerator] {Name} ran dry — base power lost!");
        }

        if (_promptLabel != null && _promptLabel.Visible)
            _promptLabel.Text = GetPromptText();
    }

    // ── Refuelling ───────────────────────────────────────────────────

    /// <summary>Adds canisters directly (loot, debug, survivor task AI).</summary>
    public int AddFuel(int canisters)
    {
        float space = MaxCanisters - FuelCanisters;
        int accepted = Mathf.Clamp(canisters, 0, Mathf.FloorToInt(space + 0.0001f));
        FuelCanisters = Mathf.Min(MaxCanisters, FuelCanisters + accepted);
        return accepted;
    }

    /// <summary>Refuels from the player's inventory. Returns true if a canister was consumed.</summary>
    public bool RefuelFromInventory(InventoryComponent inventory)
    {
        if (FuelCanisters >= MaxCanisters - 0.01f) return false;
        if (!inventory.Has("fuel_can")) return false;

        inventory.TryRemove("fuel_can", 1);
        FuelCanisters = Mathf.Min(MaxCanisters, FuelCanisters + 1f);
        Running = true;
        GD.Print($"[CombustionGenerator] Refuelled → {FuelCanisters:F1}/{MaxCanisters:F0} canisters.");
        return true;
    }

    public void ToggleRunning() => Running = !Running;

    // ── Interactable prompt API (used by PlayerInteraction) ─────────

    public void ShowPrompt(bool visible)
    {
        if (_promptLabel == null) return;
        _promptLabel.Text = GetPromptText();
        _promptLabel.Visible = visible;
    }

    public string GetPromptText()
    {
        string state = GetSupplyWatts() > 0f ? "RUNNING" : "STOPPED";
        return $"[E] Refuel Generator — {state} ({FuelCanisters:F1}/{MaxCanisters:F0} cans)";
    }

    // ── Visuals ──────────────────────────────────────────────────────

    private void CreateVisuals()
    {
        if (GetNodeOrNull<MeshInstance3D>("Mesh") == null)
        {
            var body = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(1.4f, 1.0f, 0.9f) },
                Position = new Vector3(0f, 0.5f, 0f),
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.28f, 0.32f, 0.3f),
                    Roughness = 0.65f
                }
            };
            AddChild(body);
        }

        if (GetNodeOrNull<CollisionShape3D>("Collision") == null)
        {
            var col = new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(1.4f, 1.0f, 0.9f) },
                Position = new Vector3(0f, 0.5f, 0f)
            };
            AddChild(col);
        }

        var flywheel = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.28f, BottomRadius = 0.28f, Height = 0.12f },
            Position = new Vector3(0.45f, 0.75f, 0.5f),
            RotationDegrees = new Vector3(90f, 0f, 0f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.5f, 0.2f) }
        };
        AddChild(flywheel);
        _flywheel = flywheel;

        _promptLabel = new Label3D
        {
            Text = GetPromptText(),
            Position = new Vector3(0f, 1.9f, 0f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 20,
            Visible = false
        };
        AddChild(_promptLabel);
    }
}
