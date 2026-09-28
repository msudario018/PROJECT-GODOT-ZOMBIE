using Godot;
using System.Collections.Generic;

namespace ZombieApocalypse.Entities.Zombies.ZombieAI;

/// <summary>
/// Hurt / flinch state on taking damage.
/// Applies brief hitstun before resuming chase or investigation.
/// </summary>
public partial class ZombieHurtState : ZombieBaseState
{
    [Export] public float StunDuration = 0.22f;

    private float _timer = 0f;

    public override void Enter(Dictionary<string, Variant>? msg = null)
    {
        _timer = 0f;
    }

    public override void PhysicsUpdate(double delta)
    {
        _timer += (float)delta;
        if (_timer >= StunDuration)
        {
            EmitSignal(SignalName.Transitioned, "Chase");
        }
    }
}
