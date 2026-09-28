using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Spatial;

namespace ZombieApocalypse.Entities.Zombies.ZombieAI;

/// <summary>
/// Active chase state.
/// Drives pursuit of the target using two-tier navigation:
///   1. Flow-field vector sampling when in horde density (> HordeThreshold zombies nearby).
///   2. NavigationAgent3D A* fallback for solo zombies or precision pathing.
/// Transitions to Attack when within melee range.
/// </summary>
public partial class ZombieChaseState : ZombieBaseState
{
    [Export] public float LostTargetTimeout = 3.5f;

    private float _lostTimer = 0f;

    public override void Enter(Dictionary<string, Variant>? msg = null)
    {
        _lostTimer = 0f;
        // Aggro vocalization
        Zombie.AudioEmitter?.EmitZombieVoice(false);
    }

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;

        var target = Zombie.Sensory.CurrentTarget;
        Vector3 targetPos;

        if (target != null && GodotObject.IsInstanceValid(target))
        {
            targetPos = target.GlobalPosition;
            _lostTimer = 0f;
        }
        else
        {
            targetPos = Zombie.Sensory.LastKnownTargetPosition;
            _lostTimer += dt;
            if (_lostTimer >= LostTargetTimeout)
            {
                var alertMsg = new Dictionary<string, Variant>
                {
                    { "TargetPos", targetPos }
                };
                Zombie.StateMachine.TransitionTo("Alert", alertMsg);
                return;
            }
        }

        Vector3 diff = targetPos - Zombie.GlobalPosition;
        diff.Y = 0f;
        float distToTarget = diff.Length();

        // Check if within attack range
        if (distToTarget <= Zombie.AttackRange && target != null)
        {
            EmitSignal(SignalName.Transitioned, "Attack");
            return;
        }

        // Determine movement direction via two-tier navigation
        Vector3 moveDir = Vector3.Zero;

        if (Zombie.ShouldUseFlowField() && FlowFieldNavigator.Instance != null)
        {
            // Horde mode: sample precomputed flow field
            moveDir = FlowFieldNavigator.Instance.GetFlowDirection(Zombie.GlobalPosition);
        }
        else if (Zombie.NavAgent != null && Zombie.NavAgent.IsNavigationFinished() == false)
        {
            // Solo mode: use NavigationAgent3D
            Zombie.NavAgent.TargetPosition = targetPos;
            Vector3 nextPos = Zombie.NavAgent.GetNextPathPosition();
            Vector3 navDiff = nextPos - Zombie.GlobalPosition;
            navDiff.Y = 0f;
            moveDir = navDiff.LengthSquared() > 0.01f ? navDiff.Normalized() : diff.Normalized();
        }
        else
        {
            // Direct steering fallback
            moveDir = diff.LengthSquared() > 0.01f ? diff.Normalized() : Vector3.Zero;
        }

        // Apply horizontal velocity and facing
        Zombie.Velocity = new Vector3(moveDir.X * Zombie.MoveSpeed, Zombie.Velocity.Y, moveDir.Z * Zombie.MoveSpeed);
        Zombie.FaceDirection(moveDir, dt);
    }
}
