using Godot;

namespace ZombieApocalypse.Entities.Zombies.Archetypes;

/// <summary>
/// Sprinter / Runner archetype.
/// 
/// Profile:
///   - Fast, erratic full-sprint charges (4.8 m/s).
///   - Fragile (60 HP) but delivers high burst damage (18 dmg) with rapid cooldown (1.0s).
///   - High sensory sight radius (22m). Immediately aggros on gunshots and sprint footsteps.
/// </summary>
public partial class SprinterRunner : ZombieBase
{
    public override void _Ready()
    {
        ArchetypeName = "Sprinter";
        MoveSpeed = 4.8f;
        AttackDamage = 18.0f;
        AttackRange = 1.4f;
        AttackCooldown = 1.0f;
        TurnSpeed = 12.0f;
        HordePressureForce = 1.2f;

        base._Ready();

        Health.SetMaxHealth(60.0f, true);

        // Enhance sensory cone
        if (Sensory != null)
        {
            Sensory.SightRange = 22.0f;
            Sensory.SightAngleDeg = 120.0f;
            Sensory.HearingSensitivity = 1.5f;
        }
    }
}
