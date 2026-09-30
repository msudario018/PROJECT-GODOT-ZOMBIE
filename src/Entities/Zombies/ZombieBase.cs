using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Core.Spatial;
using ZombieApocalypse.Core.StateMachine;
using ZombieApocalypse.Entities.Zombies.ZombieAI;

namespace ZombieApocalypse.Entities.Zombies;

/// <summary>
/// Base class for all zombie archetypes.
/// 
/// Composed of:
///   - <see cref="HealthComponent"/>: tracks HP, damage mitigation, death events.
///   - <see cref="SensorySystem"/>: vision cone, hearing, proximity detection.
///   - <see cref="AudioEmitterComponent"/>: acoustic footprint and vocalizations.
///   - <see cref="StateMachine"/>: manages Idle, Alert, Chase, Attack, Hurt, Dead states.
///   - <see cref="NavigationAgent3D"/>: precision pathfinding for solo zombies.
///   - Two-tier navigation: switches to <see cref="FlowFieldNavigator"/> when in horde density.
/// </summary>
public partial class ZombieBase : CharacterBody3D
{
    [ExportGroup("Stats")]
    [Export] public string ArchetypeName = "Zombie";
    [Export] public float MoveSpeed = 2.0f;
    [Export] public float AttackDamage = 15.0f;
    [Export] public float AttackRange = 1.4f;
    [Export] public float AttackCooldown = 1.5f;
    [Export] public float HordePressureForce = 1.0f;
    [Export] public float TurnSpeed = 8.0f;
    [Export] public float Gravity = 30.0f;

    [ExportGroup("Navigation Tuning")]
    [Export] public int HordeThreshold = 3; // Use FlowField if >= this many zombies nearby
    [Export] public float SpatialQueryRadius = 6.0f;

    // Component References
    // Lazily resolved: StateMachine (a child) activates the initial state in its
    // own _Ready, which runs BEFORE ZombieBase._Ready — so states must be able to
    // reach these components at that point.
    private HealthComponent? _health;
    private SensorySystem? _sensory;
    private AudioEmitterComponent? _audioEmitter;
    private StateMachine? _stateMachine;
    private NavigationAgent3D? _navAgent;
    private Node3D? _mesh;

    public HealthComponent Health => _health ??= GetNode<HealthComponent>("HealthComponent");
    public SensorySystem Sensory => _sensory ??= GetNode<SensorySystem>("SensorySystem");
    public AudioEmitterComponent AudioEmitter => _audioEmitter ??= GetNode<AudioEmitterComponent>("AudioEmitterComponent");
    public StateMachine StateMachine => _stateMachine ??= GetNode<StateMachine>("StateMachine");
    public NavigationAgent3D NavAgent => _navAgent ??= GetNode<NavigationAgent3D>("NavigationAgent3D");
    public Node3D Mesh => _mesh ??= GetNode<Node3D>("Mesh");

    // Runtime state
    public bool IsDead => Health != null && !Health.IsAlive;
    public Vector3 KnockbackVelocity = Vector3.Zero;
    private static SpatialGrid? _sharedSpatialGrid;

    public static SpatialGrid SharedSpatialGrid => _sharedSpatialGrid ??= new SpatialGrid(4.0f);

    public override void _Ready()
    {
        Health.Died += OnDied;
        Health.DamageTaken += OnDamageTaken;

        // Difficulty preset scales the horde's pace and bite at spawn time.
        var config = ZombieApocalypse.Core.Autoloads.ConfigManager.Instance;
        if (config != null)
        {
            MoveSpeed *= config.ZombieSpeedMultiplier;
            AttackDamage *= config.ZombieDamageMultiplier;
        }

        AddToGroup("zombies");
        SharedSpatialGrid.UpdateEntity(this, GlobalPosition);
    }

    public override void _ExitTree()
    {
        SharedSpatialGrid.RemoveEntity(this);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // Apply downward gravity
        if (!IsOnFloor())
        {
            var v = Velocity;
            v.Y -= Gravity * dt;
            Velocity = v;
        }

        // Apply knockback decay
        if (KnockbackVelocity.LengthSquared() > 0.01f)
        {
            Velocity += KnockbackVelocity;
            KnockbackVelocity = KnockbackVelocity.MoveToward(Vector3.Zero, 25.0f * dt);
        }

        MoveAndSlide();

        if (!IsDead)
        {
            SharedSpatialGrid.UpdateEntity(this, GlobalPosition);
        }
    }

    /// <summary>
    /// Smoothly rotates the mesh toward the specified direction on the XZ plane.
    /// </summary>
    public void FaceDirection(Vector3 direction, float delta)
    {
        direction.Y = 0f;
        if (direction.LengthSquared() < 0.001f) return;

        float targetAngle = Mathf.Atan2(direction.X, direction.Z);
        float currentAngle = Mesh.Rotation.Y;
        Mesh.Rotation = new Vector3(0f, Mathf.LerpAngle(currentAngle, targetAngle, TurnSpeed * delta), 0f);
    }

    /// <summary>
    /// Checks whether this zombie should use horde flow-field navigation
    /// instead of individual NavigationAgent3D.
    /// </summary>
    /// <summary>
    /// Move speed including the night frenzy modifier from <see cref="World.Environment.DayNightCycle"/>.
    /// At night all zombies become faster (×1.35). AI states should use this
    /// instead of the raw <see cref="MoveSpeed"/> export.
    /// </summary>
    public float EffectiveMoveSpeed =>
        MoveSpeed * (World.Environment.DayNightCycle.Instance?.ZombieNightMultiplier ?? 1f);

    public bool ShouldUseFlowField()
    {
        if (FlowFieldNavigator.Instance == null) return false;

        var nearby = SharedSpatialGrid.QueryRadius(GlobalPosition, SpatialQueryRadius);
        return nearby.Count >= HordeThreshold;
    }

    /// <summary>
    /// Called when the zombie takes combat damage. Applies hit reaction and knockback.
    /// </summary>
    public void ApplyDamage(float damage, DamageType type, Node? attacker, Vector3 knockback)
    {
        if (IsDead) return;

        KnockbackVelocity = knockback;
        Health.TakeDamage(damage, type, attacker);

        // Flash mesh red
        PlayHitFlash();

        if (!IsDead && StateMachine.CurrentStateName != "Hurt")
        {
            var msg = new Dictionary<string, Variant>
            {
                { "Attacker", attacker ?? this }
            };
            StateMachine.TransitionTo("Hurt", msg);
        }
    }

    private void OnDamageTaken(float amount, int damageType)
    {
        // Emit grunt / pain sound
        AudioEmitter?.EmitZombieVoice(false);
    }

    private void OnDied()
    {
        SharedSpatialGrid.RemoveEntity(this);
        StateMachine.TransitionTo("Dead");
    }

    private void PlayHitFlash()
    {
        // Visual feedback on hit
        if (Mesh is MeshInstance3D meshInstance && meshInstance.GetActiveMaterial(0) is StandardMaterial3D mat)
        {
            var tween = CreateTween();
            Color originalColor = mat.AlbedoColor;
            mat.AlbedoColor = new Color(1f, 0.2f, 0.2f);
            tween.TweenProperty(mat, "albedo_color", originalColor, 0.12f);
        }
    }
}
