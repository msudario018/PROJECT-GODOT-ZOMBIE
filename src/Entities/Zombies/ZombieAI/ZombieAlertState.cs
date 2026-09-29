using Godot;
using System.Collections.Generic;

namespace ZombieApocalypse.Entities.Zombies.ZombieAI;

/// <summary>
/// Alert / investigative state.
/// The zombie navigates to a suspicious sound origin or last seen location.
/// </summary>
public partial class ZombieAlertState : ZombieBaseState
{
    [Export] public float InvestigateSpeedMultiplier = 0.8f;
    [Export] public float SearchTimeAtLocation = 3.0f;

    private Vector3 _investigatePosition;
    private bool _reachedLocation;
    private float _searchTimer;

    public override void Enter(Dictionary<string, Variant>? msg = null)
    {
        if (msg != null && msg.TryGetValue("TargetPos", out var posVal))
        {
            _investigatePosition = (Vector3)posVal;
        }
        else
        {
            _investigatePosition = Zombie.Sensory.LastHeardSoundPosition;
        }

        _reachedLocation = false;
        _searchTimer = SearchTimeAtLocation;

        Zombie.Sensory.TargetSpotted += OnTargetSpotted;
        Zombie.Sensory.SoundHeard += OnSoundHeard;

        // Emit an alert growl
        Zombie.AudioEmitter?.EmitZombieVoice(false);
    }

    public override void Exit()
    {
        Zombie.Sensory.TargetSpotted -= OnTargetSpotted;
        Zombie.Sensory.SoundHeard -= OnSoundHeard;
    }

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;

        if (!_reachedLocation)
        {
            Vector3 diff = _investigatePosition - Zombie.GlobalPosition;
            diff.Y = 0f;

            if (diff.LengthSquared() < 1.0f)
            {
                _reachedLocation = true;
                Zombie.Velocity = new Vector3(0f, Zombie.Velocity.Y, 0f);
            }
            else
            {
                Vector3 dir = diff.Normalized();
                float speed = Zombie.EffectiveMoveSpeed * InvestigateSpeedMultiplier;
                Zombie.Velocity = new Vector3(dir.X * speed, Zombie.Velocity.Y, dir.Z * speed);
                Zombie.FaceDirection(dir, dt);
            }
        }
        else
        {
            // Searching around
            _searchTimer -= dt;
            if (_searchTimer <= 0f)
            {
                EmitSignal(SignalName.Transitioned, "Idle");
            }
        }
    }

    private void OnTargetSpotted(Node3D target)
    {
        EmitSignal(SignalName.Transitioned, "Chase");
    }

    private void OnSoundHeard(Vector3 position, int soundType)
    {
        // Re-target to new closer sound
        _investigatePosition = position;
        _reachedLocation = false;
        _searchTimer = SearchTimeAtLocation;
    }
}
