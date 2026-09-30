using Godot;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Data;

namespace ZombieApocalypse.Core.Components;

/// <summary>
/// Reusable health component for any entity: player, survivors, zombies,
/// bandits, walls, buildings, vehicles, powered devices.
///
/// Supports:
///   - Current/max HP tracking
///   - Flat armor reduction
///   - Per-damage-type resistance array (indexed by DamageType enum)
///   - Heal, TakeDamage, SetMaxHealth API
///   - Godot signals for HealthChanged, DamageTaken, Healed, Died
///   - EventBus integration (OnDamageDealt, OnEntityKilled)
///
/// Attach as a child Node of any entity that needs health tracking.
/// </summary>
public partial class HealthComponent : Node
{
    // ── Configuration ──────────────────────────────────────────────
    [ExportGroup("Health")]
    [Export] public float MaxHealth = 100f;
    [Export] public float ArmorRating = 0f;

    [ExportGroup("Resistances")]
    [Export(PropertyHint.Range, "0,1,0.05")]
    public float ResistBlunt = 0f;
    [Export(PropertyHint.Range, "0,1,0.05")]
    public float ResistSlash = 0f;
    [Export(PropertyHint.Range, "0,1,0.05")]
    public float ResistPierce = 0f;
    [Export(PropertyHint.Range, "0,1,0.05")]
    public float ResistBallistic = 0f;
    [Export(PropertyHint.Range, "0,1,0.05")]
    public float ResistFire = 0f;
    [Export(PropertyHint.Range, "0,1,0.05")]
    public float ResistExplosive = 0f;
    [Export(PropertyHint.Range, "0,1,0.05")]
    public float ResistToxic = 0f;
    [Export(PropertyHint.Range, "0,1,0.05")]
    public float ResistElectric = 0f;

    // ── Signals ────────────────────────────────────────────────────
    [Signal] public delegate void HealthChangedEventHandler(float currentHealth, float maxHealth);
    [Signal] public delegate void DamageTakenEventHandler(float amount, int damageType);
    [Signal] public delegate void HealedEventHandler(float amount);
    [Signal] public delegate void DiedEventHandler();

    // ── State ──────────────────────────────────────────────────────
    public float CurrentHealth { get; private set; }
    public bool IsAlive => CurrentHealth > 0f;
    public float HealthPercent => MaxHealth > 0f ? CurrentHealth / MaxHealth : 0f;
    public bool IsFullHealth => CurrentHealth >= MaxHealth;

    public override void _Ready()
    {
        CurrentHealth = MaxHealth;
    }

    // ── Damage ─────────────────────────────────────────────────────

    /// <summary>
    /// Apply damage to this entity. Reduces HP by the final amount after
    /// armor and resistance calculations.
    /// </summary>
    /// <param name="rawDamage">Raw incoming damage before mitigation.</param>
    /// <param name="type">Damage type for resistance lookup.</param>
    /// <param name="attacker">The node that dealt the damage (nullable).</param>
    /// <returns>The actual damage dealt after all mitigation.</returns>
    public float TakeDamage(float rawDamage, DamageType type = DamageType.Blunt, Node? attacker = null)
    {
        if (!IsAlive) return 0f;

        // Apply armor (flat reduction)
        float afterArmor = Mathf.Max(0f, rawDamage - ArmorRating);

        // Apply type-specific resistance (percentage reduction)
        float resistance = GetResistance(type);
        float finalDamage = afterArmor * (1f - resistance);

        // Apply damage
        CurrentHealth = Mathf.Max(0f, CurrentHealth - finalDamage);

        // Notify via Godot signals (local listeners)
        EmitSignal(SignalName.DamageTaken, finalDamage, (int)type);
        EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);

        // Notify via EventBus (global listeners)
        EventBus.Instance?.EmitDamageDealt(attacker ?? this, GetParent(), finalDamage);

        // Check for death
        if (!IsAlive)
        {
            EmitSignal(SignalName.Died);
            EventBus.Instance?.EmitEntityKilled(GetParent());
            GD.Print($"[HealthComponent] {GetParent()?.Name ?? "?"} died. Damage: {finalDamage:F1} ({type})");
        }

        return finalDamage;
    }

    // ── Healing ────────────────────────────────────────────────────

    /// <summary>Restore HP up to MaxHealth.</summary>
    /// <returns>Actual amount healed.</returns>
    public float Heal(float amount)
    {
        if (!IsAlive || amount <= 0f) return 0f;

        float oldHealth = CurrentHealth;
        CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + amount);
        float actualHeal = CurrentHealth - oldHealth;

        if (actualHeal > 0f)
        {
            EmitSignal(SignalName.Healed, actualHeal);
            EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);
        }

        return actualHeal;
    }

    /// <summary>Fully restore HP to MaxHealth.</summary>
    public void HealToFull() => Heal(MaxHealth - CurrentHealth);

    // ── Configuration ──────────────────────────────────────────────

    /// <summary>Change the max health. Optionally heal to the new max.</summary>
    public void SetMaxHealth(float newMax, bool healToFull = false)
    {
        MaxHealth = Mathf.Max(1f, newMax);
        if (healToFull)
            CurrentHealth = MaxHealth;
        else
            CurrentHealth = Mathf.Min(CurrentHealth, MaxHealth);
        EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);
    }

    /// <summary>
    /// Set the current health directly, clamped to [0, MaxHealth]. Used when
    /// loading a save so restoring never re-triggers damage or death signals.
    /// </summary>
    public void SetHealth(float value)
    {
        CurrentHealth = Mathf.Clamp(value, 0f, MaxHealth);
        EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);
    }

    /// <summary>Instantly kill this entity (bypasses armor/resistance).</summary>
    public void Kill()
    {
        if (!IsAlive) return;
        CurrentHealth = 0f;
        EmitSignal(SignalName.HealthChanged, 0f, MaxHealth);
        EmitSignal(SignalName.Died);
        EventBus.Instance?.EmitEntityKilled(GetParent());
    }

    // ── Resistance Lookup ──────────────────────────────────────────

    /// <summary>Get the resistance value (0-1) for a specific damage type.</summary>
    public float GetResistance(DamageType type)
    {
        float r = type switch
        {
            DamageType.Blunt     => ResistBlunt,
            DamageType.Slash     => ResistSlash,
            DamageType.Pierce    => ResistPierce,
            DamageType.Ballistic => ResistBallistic,
            DamageType.Fire      => ResistFire,
            DamageType.Explosive => ResistExplosive,
            DamageType.Toxic     => ResistToxic,
            DamageType.Electric  => ResistElectric,
            _                    => 0f
        };
        return Mathf.Clamp(r, 0f, 1f);
    }
}
