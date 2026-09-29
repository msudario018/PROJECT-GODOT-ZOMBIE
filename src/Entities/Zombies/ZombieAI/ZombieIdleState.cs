using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Utilities;

namespace ZombieApocalypse.Entities.Zombies.ZombieAI;

/// <summary>
/// Passive idle / wander state.
/// Listens to SensorySystem for vision alerts and sound events.
/// </summary>
public partial class ZombieIdleState : ZombieBaseState
{
    [Export] public float GroanIntervalMin = 6.0f;
    [Export] public float GroanIntervalMax = 14.0f;
    [Export] public float WanderRadius = 3.0f;

    private float _nextGroanTimer;
    private float _wanderTimer;
    private Vector3 _spawnPosition;
    private Vector3 _wanderTarget;
    private bool _hasWanderTarget;

    public override void _Ready()
    {
        base._Ready();
        _spawnPosition = Zombie.GlobalPosition;
        ResetGroanTimer();
    }

    public override void Enter(Dictionary<string, Variant>? msg = null)
    {
        Zombie.Velocity = new Vector3(0f, Zombie.Velocity.Y, 0f);
        _wanderTimer = (float)GD.RandRange(2.0, 5.0);
        _hasWanderTarget = false;

        // Subscribe to sensory signals
        Zombie.Sensory.TargetSpotted += OnTargetSpotted;
        Zombie.Sensory.SoundHeard += OnSoundHeard;
    }

    public override void Exit()
    {
        Zombie.Sensory.TargetSpotted -= OnTargetSpotted;
        Zombie.Sensory.SoundHeard -= OnSoundHeard;
    }

    public override void Update(double delta)
    {
        float dt = (float)delta;
        _nextGroanTimer -= dt;
        if (_nextGroanTimer <= 0f)
        {
            Zombie.AudioEmitter?.EmitZombieVoice(false);
            ResetGroanTimer();
        }

        // Wander logic
        _wanderTimer -= dt;
        if (_wanderTimer <= 0f)
        {
            _wanderTimer = (float)GD.RandRange(3.0, 7.0);
            PickWanderTarget();
        }
    }

    public override void PhysicsUpdate(double delta)
    {
        // If wandering toward point
        if (_hasWanderTarget)
        {
            float dt = (float)delta;
            Vector3 diff = _wanderTarget - Zombie.GlobalPosition;
            diff.Y = 0f;

            if (diff.LengthSquared() < 0.25f)
            {
                _hasWanderTarget = false;
                Zombie.Velocity = new Vector3(0f, Zombie.Velocity.Y, 0f);
            }
            else
            {
                Vector3 dir = diff.Normalized();
                Zombie.Velocity = new Vector3(dir.X * Zombie.EffectiveMoveSpeed * 0.4f, Zombie.Velocity.Y, dir.Z * Zombie.EffectiveMoveSpeed * 0.4f);
                Zombie.FaceDirection(dir, dt);
            }
        }
    }

    private void PickWanderTarget()
    {
        float angle = (float)GD.RandRange(0, Mathf.Tau);
        float dist = (float)GD.RandRange(0.5, WanderRadius);
        _wanderTarget = _spawnPosition + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
        _hasWanderTarget = true;
    }

    private void ResetGroanTimer()
    {
        _nextGroanTimer = (float)GD.RandRange(GroanIntervalMin, GroanIntervalMax);
    }

    private void OnTargetSpotted(Node3D target)
    {
        EmitSignal(SignalName.Transitioned, "Chase");
    }

    private void OnSoundHeard(Vector3 position, int soundType)
    {
        var msg = new Dictionary<string, Variant>
        {
            { "TargetPos", position }
        };
        Zombie.StateMachine.TransitionTo("Alert", msg);
    }
}
