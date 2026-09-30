using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Entities.NPC;
using ZombieApocalypse.World.Environment;
using ZombieApocalypse.World.Power;

namespace ZombieApocalypse.Entities.Survivors;

/// <summary>
/// Camp-wide morale tracker. Survivors, deaths and events push morale up or
/// down; low morale slows survivors, high morale speeds them up.
/// </summary>
public partial class MoraleSystem : Node
{
    public static MoraleSystem? Instance { get; private set; }

    [ExportGroup("Morale")]
    [Export(PropertyHint.Range, "0,100,1")] public float StartingMorale = 70f;
    [Export(PropertyHint.Range, "0,100,1")] public float MinMorale = 0f;
    [Export(PropertyHint.Range, "0,100,1")] public float MaxMorale = 100f;

    [ExportGroup("Drift")]
    [Export] public float HungerPenaltyPerSecond = 0.15f;
    [Export] public float MiasmaPenaltyPerSecond = 0.4f;
    [Export] public float PowerBonusPerSecond = 0.1f;
    [Export] public float NeutralDriftPerSecond = 0.2f;

    [ExportGroup("Events")]
    [Export] public float SurvivorDeathPenalty = 12f;
    [Export] public float PlayerDeathPenalty = 25f;
    [Export] public float BanditKillBonus = 4f;
    [Export] public float ZombieKillBonus = 0.5f;
    [Export] public float CorpseBuriedBonus = 1.5f;

    [ExportGroup("Speed")]
    [Export] public float LowMoraleThreshold = 30f;
    [Export] public float HighMoraleThreshold = 75f;
    [Export] public float LowMoraleSpeed = 0.8f;
    [Export] public float HighMoraleSpeed = 1.1f;

    public float Morale { get; private set; }

    private float _recalcTimer;

    public override void _Ready()
    {
        Instance = this;
        Morale = StartingMorale;

        if (EventBus.Instance != null)
        {
            EventBus.Instance.OnEntityKilled += OnEntityKilled;
            EventBus.Instance.OnCorpseBuried += OnCorpseBuried;
        }

        EmitMorale();
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.OnEntityKilled -= OnEntityKilled;
            EventBus.Instance.OnCorpseBuried -= OnCorpseBuried;
        }

        if (Instance == this) Instance = null;
    }

    /// <summary>Additive morale change, clamped. Emits to the EventBus.</summary>
    public void AdjustMorale(float delta)
    {
        float before = Morale;
        Morale = Mathf.Clamp(Morale + delta, MinMorale, MaxMorale);
        if (Mathf.Abs(Morale - before) > 0.001f)
            EmitMorale();
    }

    /// <summary>Test/debug helper: set morale directly.</summary>
    public void SetMorale(float value)
    {
        Morale = Mathf.Clamp(value, MinMorale, MaxMorale);
        EmitMorale();
    }

    /// <summary>Survivor task-speed multiplier from current morale.</summary>
    public float GetSurvivorSpeedMultiplier()
    {
        if (Morale < LowMoraleThreshold) return LowMoraleSpeed;
        if (Morale >= HighMoraleThreshold) return HighMoraleSpeed;
        return 1.0f;
    }

    // ── Simulation tick ──────────────────────────────────────────────

    public override void _Process(double delta)
    {
        _recalcTimer -= (float)delta;
        if (_recalcTimer > 0f) return;
        _recalcTimer = 1.0f;

        // Sample the live world instead of safe defaults so morale actually
        // reacts to the camp's condition.
        int miasmaZones = CorpseManager.Instance?.GetActiveMiasmaCount() ?? 0;

        bool powerOnline = PowerGrid.Instance == null
            || PowerGrid.Instance.TotalGenerationWatts > 0.5f
            || PowerGrid.Instance.BatteryPercent > 0.02f;

        float hungerFraction = 1f;
        var player = GetTree().GetFirstNodeInGroup("player") as Node;
        var stats = player?.GetNodeOrNull<Entities.Player.PlayerStats>("PlayerStats");
        if (stats != null)
            hungerFraction = Mathf.Clamp(stats.Hunger / 100f, 0f, 1f);

        Recalculate(1.0f, hungerFraction, miasmaZones, powerOnline);
    }

    /// <summary>
    /// Deterministic morale tick. Public so headless tests can drive it with
    /// fixed inputs instead of reading live world state.
    /// </summary>
    /// <param name="dtSeconds">Simulated seconds to advance.</param>
    /// <param name="hungerFraction">0..1 average survivor hunger (1 = full).</param>
    /// <param name="activeMiasmaZones">Current miasma zone count.</param>
    /// <param name="powerOnline">True when the grid reports generation.</param>
    public void Recalculate(float dtSeconds, float hungerFraction = 1f, int activeMiasmaZones = 0, bool powerOnline = true)
    {
        float delta = 0f;

        // Surplus hunger pulls toward neutral; starving survivors drag morale down.
        if (hungerFraction < 0.5f)
            delta -= HungerPenaltyPerSecond * (1f - hungerFraction * 2f) * dtSeconds;
        else
            delta += NeutralDriftPerSecond * dtSeconds * 0.25f;

        delta -= MiasmaPenaltyPerSecond * activeMiasmaZones * dtSeconds;

        if (powerOnline)
            delta += PowerBonusPerSecond * dtSeconds;

        // Camp always drifts slowly back toward a liveable middle.
        delta += (60f - Morale) * 0.002f * dtSeconds;

        if (Mathf.Abs(delta) > 0.0001f)
            AdjustMorale(delta);
    }

    // ── Event hooks ──────────────────────────────────────────────────

    private void OnEntityKilled(Node entity)
    {
        if (entity is Bandits.BanditBase)
            AdjustMorale(BanditKillBonus);
        else if (entity is Zombies.ZombieBase)
            AdjustMorale(ZombieKillBonus);
        else if (entity is SurvivorBase)
            AdjustMorale(-SurvivorDeathPenalty);
        else if (entity is Player.PlayerController)
            AdjustMorale(-PlayerDeathPenalty);
    }

    private void OnCorpseBuried(Godot.Vector3 _)
    {
        AdjustMorale(CorpseBuriedBonus);
    }

    private void EmitMorale()
    {
        EventBus.Instance?.EmitCampMoraleChanged(Morale);
    }
}
