using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Data;

namespace ZombieApocalypse.Entities.Zombies.ZombieAI;

/// <summary>
/// Melee attack state.
/// Executes windup, deals damage to target's HealthComponent, and handles cooldown.
/// </summary>
public partial class ZombieAttackState : ZombieBaseState
{
    [Export] public float WindupTime = 0.35f;

    private float _timer = 0f;
    private bool _hasDealtDamage = false;

    public override void Enter(Dictionary<string, Variant>? msg = null)
    {
        _timer = 0f;
        _hasDealtDamage = false;
        Zombie.Velocity = new Vector3(0f, Zombie.Velocity.Y, 0f);
    }

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;
        _timer += dt;

        var target = Zombie.Sensory.CurrentTarget;
        if (target == null || !GodotObject.IsInstanceValid(target))
        {
            EmitSignal(SignalName.Transitioned, "Chase");
            return;
        }

        // Keep facing target while in attack
        Vector3 diff = target.GlobalPosition - Zombie.GlobalPosition;
        diff.Y = 0f;
        Zombie.FaceDirection(diff.Normalized(), dt);

        // Windup completed -> deal damage
        if (!_hasDealtDamage && _timer >= WindupTime)
        {
            _hasDealtDamage = true;
            ExecuteAttack(target, diff.Length());
        }

        // Cooldown completed -> check follow-up
        if (_timer >= Zombie.AttackCooldown)
        {
            if (diff.Length() <= Zombie.AttackRange)
            {
                // Reset attack cycle for another swing
                _timer = 0f;
                _hasDealtDamage = false;
            }
            else
            {
                EmitSignal(SignalName.Transitioned, "Chase");
            }
        }
    }

    private void ExecuteAttack(Node3D target, float distance)
    {
        // Emit attack swing noise
        Zombie.AudioEmitter?.EmitMeleeSwing();

        // Check if still within range at time of impact
        if (distance <= Zombie.AttackRange * 1.3f)
        {
            var targetHealth = target.GetNodeOrNull<HealthComponent>("HealthComponent");
            if (targetHealth != null && targetHealth.IsAlive)
            {
                targetHealth.TakeDamage(Zombie.EffectiveAttackDamage, DamageType.Slash, Zombie);
            }
        }
    }
}
