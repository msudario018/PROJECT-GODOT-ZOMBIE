using Godot;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.World.Environment;

namespace ZombieApocalypse.Entities.Player;

/// <summary>
/// Tracks the player's biological survival needs:
///   - Hunger       (0–100, drains over time; 0 = starvation)
///   - Thirst       (0–100, drains faster than hunger; 0 = dehydration)
///   - Stamina      (0–100, consumed by sprinting, recovers at rest)
///
/// Effects when critical:
///   - Hunger ≤ 10  → slow HP drain (starvation damage)
///   - Thirst ≤ 10  → faster HP drain + blurred vision signal
///   - Stamina = 0  → sprint disabled, move speed −30%
///
/// Exposes read-only properties consumed by HUD and PlayerController.
/// </summary>
public partial class PlayerStats : Node
{
    // ── Configuration ────────────────────────────────────────────────
    [ExportGroup("Drain Rates (units/second, real time)")]
    [Export] public float HungerDrainRate   = 0.4f;   // ~4 min to empty at rest
    [Export] public float ThirstDrainRate   = 0.7f;   // ~2.4 min to empty
    [Export] public float StaminaDrainRate  = 20.0f;  // sprint drains fast
    [Export] public float StaminaRegenRate  = 12.0f;  // regen when not sprinting
    [Export] public float StarveDamageRate  = 1.0f;   // HP/s when hunger ≤ 10
    [Export] public float ThirstDamageRate  = 2.0f;   // HP/s when thirst ≤ 10

    // ── Public State ─────────────────────────────────────────────────
    public float Hunger  { get; private set; } = 80f;
    public float Thirst  { get; private set; } = 80f;
    public float Stamina { get; private set; } = 100f;

    public float HungerPercent  => Hunger  / 100f;
    public float ThirstPercent  => Thirst  / 100f;
    public float StaminaPercent => Stamina / 100f;

    /// <summary>True when stamina is fully depleted (sprint locked).</summary>
    public bool IsExhausted { get; private set; }

    // ── Signals ──────────────────────────────────────────────────────
    [Signal] public delegate void StatsChangedEventHandler(float hunger, float thirst, float stamina);
    [Signal] public delegate void StaminaDepletedEventHandler();
    [Signal] public delegate void StaminaRecoveredEventHandler();

    // ── References ───────────────────────────────────────────────────
    private Core.Components.HealthComponent? _health;
    private bool _isSprinting;

    public override void _Ready()
    {
        _health = GetParent()?.GetNodeOrNull<Core.Components.HealthComponent>("HealthComponent");
    }

    // ── Called by PlayerController each frame ────────────────────────

    /// <summary>Update stamina based on whether the player is currently sprinting.</summary>
    public void SetSprinting(bool sprinting) => _isSprinting = sprinting;

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // Drain hunger and thirst over real time
        Hunger  = Mathf.Max(0f, Hunger  - HungerDrainRate  * dt);
        // Heat and humidity raise the thirst drain (WeatherSystem owns the curve).
        float thirstRate = ThirstDrainRate * (WeatherSystem.Instance?.ThirstDrainMultiplier ?? 1f);
        Thirst  = Mathf.Max(0f, Thirst  - thirstRate * dt);

        // Stamina: drain on sprint, regen otherwise
        if (_isSprinting && Stamina > 0f)
        {
            Stamina = Mathf.Max(0f, Stamina - StaminaDrainRate * dt);
            if (Stamina <= 0f && !IsExhausted)
            {
                IsExhausted = true;
                EmitSignal(SignalName.StaminaDepleted);
                GD.Print("[PlayerStats] Stamina depleted — sprint locked.");
            }
        }
        else
        {
            float prevStamina = Stamina;
            Stamina = Mathf.Min(100f, Stamina + StaminaRegenRate * dt);
            // Unlock sprint once stamina recovers to 25%
            if (IsExhausted && Stamina >= 25f)
            {
                IsExhausted = false;
                EmitSignal(SignalName.StaminaRecovered);
                GD.Print("[PlayerStats] Stamina recovered — sprint unlocked.");
            }
        }

        // Critical effects
        if (_health != null)
        {
            if (Hunger <= 10f)
                _health.TakeDamage(StarveDamageRate * dt, ZombieApocalypse.Core.Data.DamageType.Toxic, null);
            if (Thirst <= 10f)
                _health.TakeDamage(ThirstDamageRate * dt, ZombieApocalypse.Core.Data.DamageType.Toxic, null);
        }

        EmitSignal(SignalName.StatsChanged, Hunger, Thirst, Stamina);
    }

    // ── Public API (called by inventory use, crafting, etc.) ─────────

    /// <summary>Restore hunger (eating). Capped at 100.</summary>
    public void Eat(float foodValue)
    {
        Hunger = Mathf.Min(100f, Hunger + foodValue);
        GD.Print($"[PlayerStats] Ate → Hunger {Hunger:F0}");
    }

    /// <summary>Restore thirst (drinking). Capped at 100.</summary>
    public void Drink(float waterValue)
    {
        Thirst = Mathf.Min(100f, Thirst + waterValue);
        GD.Print($"[PlayerStats] Drank → Thirst {Thirst:F0}");
    }

    /// <summary>Instant stamina boost (energy drink, etc.).</summary>
    public void RestoreStamina(float amount)
    {
        Stamina = Mathf.Min(100f, Stamina + amount);
    }

    /// <summary>Can the player sprint right now (not exhausted, stamina > 0)?</summary>
    public bool CanSprint() => !IsExhausted && Stamina > 0f;

    /// <summary>
    /// Restore the vitals bar directly (used by save/load). Clamped to 0-100 and
    /// clears the exhausted flag so a loaded player can sprint again.
    /// </summary>
    public void Restore(float hunger, float thirst, float stamina)
    {
        Hunger = Mathf.Clamp(hunger, 0f, 100f);
        Thirst = Mathf.Clamp(thirst, 0f, 100f);
        Stamina = Mathf.Clamp(stamina, 0f, 100f);
        IsExhausted = false;
        EmitSignal(SignalName.StatsChanged, Hunger, Thirst, Stamina);
    }
}
