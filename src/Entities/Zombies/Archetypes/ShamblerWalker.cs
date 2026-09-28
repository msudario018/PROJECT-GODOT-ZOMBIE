using Godot;

namespace ZombieApocalypse.Entities.Zombies.Archetypes;

/// <summary>
/// Shambler / Walker archetype.
/// 
/// Profile:
///   - Slow, dragging gait (1.8 m/s).
///   - High crowd density; lethal when cornering survivors in corridors.
///   - Susceptible to blunt head trauma and line-of-sight breaking.
/// </summary>
public partial class ShamblerWalker : ZombieBase
{
    public override void _Ready()
    {
        ArchetypeName = "Shambler";
        MoveSpeed = 1.8f;
        AttackDamage = 12.0f;
        AttackRange = 1.3f;
        AttackCooldown = 1.4f;
        HordePressureForce = 1.0f;

        base._Ready();

        Health.SetMaxHealth(80.0f, true);
    }
}
