namespace ZombieApocalypse.World.Defenses.Traps;

using Godot;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Data;

/// <summary>
/// Punji Trench: sharpened stakes in a shallow pit.
///
/// Cheap, silent, and devastating to anything that walks into it. Designed as
/// counterplay for the <see cref="Entities.Zombies.Archetypes.BruteTank"/>:
/// a charging brute impales itself before it reaches the wall, and unlike a
/// tripwire it does not announce the camp's position.
/// </summary>
public partial class PunjiTrench : TrapBase
{
    [ExportGroup("Stakes")]
    /// <summary>Piercing damage dealt to the victim.</summary>
    [Export] public float StakeDamage = 85f;
    /// <summary>Impulse that stops the victim in its tracks.</summary>
    [Export] public float ImpulseForce = 6f;
    /// <summary>Fraction of damage a brute takes instead (it is too big to stop).</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float BruteResistance = 0.55f;
    /// <summary>Seconds the trap is buried before it can be disarmed safely.</summary>
    [Export] public float SafeDisarmDelay = 2f;

    /// <summary>Damage the last victim actually took after resistance.</summary>
    public float LastDamageDealt { get; private set; }

    protected override string TrapName => "punji";

    public override void _Ready()
    {
        // Stakes need a moment to bed in before they can be safely disarmed.
        SetSafeDisarmDelay(SafeDisarmDelay);
        base._Ready();
    }

    protected override void ApplyEffect(Node3D victim)
    {
        var health = HealthOf(victim);
        if (health == null || !health.IsAlive) return;

        float damage = StakeDamage;

        // A charging brute has mass: it takes the impalement but keeps coming.
        if (victim is Entities.Zombies.Archetypes.BruteTank)
        {
            damage *= 1f - BruteResistance;
            GD.Print("[PunjiTrench] Stakes pierce the brute but fail to stop it.");
        }

        LastDamageDealt = health.TakeDamage(damage, DamageType.Pierce, this);
    }
}
