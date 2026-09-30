using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Core.Spatial;
using ZombieApocalypse.Core.StateMachine;
using ZombieApocalypse.Entities.Zombies.ZombieAI;
using ZombieApocalypse.World.Environment;

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

    /// <summary>
    /// The authored stats before the difficulty preset is applied. Difficulty is
    /// resolved per use (<see cref="EffectiveMoveSpeed"/> /
    /// <see cref="EffectiveAttackDamage"/>) so changing the preset in the pause
    /// menu retunes zombies that are already in the world.
    /// </summary>
    public float BaseMoveSpeed { get; private set; }
    public float BaseAttackDamage { get; private set; }

    /// <summary>Attack damage after the difficulty preset.</summary>
    public float EffectiveAttackDamage =>
        BaseAttackDamage * (Core.Autoloads.ConfigManager.Instance?.ZombieDamageMultiplier ?? 1f);

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
    private CharacterVisual? _visual;

    public HealthComponent Health => _health ??= GetNode<HealthComponent>("HealthComponent");
    public SensorySystem Sensory => _sensory ??= GetNode<SensorySystem>("SensorySystem");

    /// <summary>
    /// Optional: a zombie spawned without a voice or a body (a headless test
    /// fixture, a placeholder) must still run, so these are soft lookups.
    /// </summary>
    public AudioEmitterComponent? AudioEmitter
        => _audioEmitter ??= GetNodeOrNull<AudioEmitterComponent>("AudioEmitterComponent");

    public StateMachine StateMachine => _stateMachine ??= GetNode<StateMachine>("StateMachine");
    public NavigationAgent3D NavAgent => _navAgent ??= GetNode<NavigationAgent3D>("NavigationAgent3D");
    public Node3D? Mesh => _mesh ??= _visual?.PrimaryMesh ?? GetNodeOrNull<Node3D>("Mesh");

    // Runtime state
    public bool IsDead => Health != null && !Health.IsAlive;
    public Vector3 KnockbackVelocity = Vector3.Zero;
    private static SpatialGrid? _sharedSpatialGrid;

    /// <summary>Where this zombie was told to investigate (scream, scent, gunshot).</summary>
    public Vector3 AlertPosition { get; private set; }
    /// <summary>Seconds of alert remaining before it returns to idle.</summary>
    public float AlertTimer { get; private set; }
    /// <summary>True while an external alert is driving this zombie.</summary>
    public bool IsAlerted => AlertTimer > 0f;

    public static SpatialGrid SharedSpatialGrid => _sharedSpatialGrid ??= new SpatialGrid(4.0f);

    public override void _Ready()
    {
        Health.Died += OnDied;
        Health.DamageTaken += OnDamageTaken;

        // Visual pivot: rotation, hit flash and model swapping live here so the
        // collision capsule never has to move.
        _visual = GetNodeOrNull<CharacterVisual>("CharacterVisual");
        _visual?.CacheMeshes();

        // Snapshot the authored stats; the difficulty preset is applied on read
        // (EffectiveMoveSpeed / EffectiveAttackDamage) so it can change mid-run.
        BaseMoveSpeed = MoveSpeed;
        BaseAttackDamage = AttackDamage;

        AddToGroup("zombies");
        SharedSpatialGrid.UpdateEntity(this, GlobalPosition);
    }

    /// <summary>Visual pivot for this zombie (null in scenes not yet migrated).</summary>
    public CharacterVisual? Visual => _visual;

    public override void _ExitTree()
    {
        SharedSpatialGrid.RemoveEntity(this);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // Count down any external alert (scream, scent handoff).
        if (AlertTimer > 0f) AlertTimer = Mathf.Max(0f, AlertTimer - dt);

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

        // Bodyless zombies (test fixtures) still turn logically, just not visually.
        if (Mesh is not { } body) return;

        float targetAngle = Mathf.Atan2(direction.X, direction.Z);
        float currentAngle = body.Rotation.Y;
        body.Rotation = new Vector3(0f, Mathf.LerpAngle(currentAngle, targetAngle, TurnSpeed * delta), 0f);
    }

    /// <summary>
    /// Checks whether this zombie should use horde flow-field navigation
    /// instead of individual NavigationAgent3D.
    /// </summary>
    /// <summary>
    /// Move speed after every live modifier: the difficulty preset, the night
    /// frenzy (<see cref="Core.Autoloads.TimeManager.ZombieNightMultiplier"/>) and
    /// the weather/season horde pressure. AI states should use this rather than
    /// the raw <see cref="MoveSpeed"/> export.
    ///
    /// The clock is read from the autoload rather than DayNightCycle, so the
    /// multiplier applies in scenes that have no DayNightCycle node.
    /// </summary>
    public float EffectiveMoveSpeed =>
        BaseMoveSpeed
        * (Core.Autoloads.ConfigManager.Instance?.ZombieSpeedMultiplier ?? 1f)
        * (Core.Autoloads.TimeManager.Instance?.ZombieNightMultiplier ?? 1f)
        * (World.Environment.WeatherSystem.Instance?.HordePressureMultiplier ?? 1f);

    /// <summary>
    /// Send this zombie to investigate a position for a while. Used by the
    /// Screamer's sector alert and by the hounds closing on a scent trail.
    /// Safe to call on a dead or despawned zombie.
    /// </summary>
    public void AlertTo(Vector3 position, float durationSeconds = 20f)
    {
        if (IsDead) return;

        AlertPosition = position;
        AlertTimer = Mathf.Max(AlertTimer, durationSeconds);

        // GetNodeOrNull, not the StateMachine property: alerting must never throw
        // on a partially constructed zombie (e.g. a bare node in a test), and a
        // throw inside _Ready would leave the game with no way to quit.
        var machine = GetNodeOrNull<Core.StateMachine.StateMachine>("StateMachine");
        machine?.TransitionTo("Alert", new System.Collections.Generic.Dictionary<string, Variant>
        {
            { "TargetPos", position }
        });
    }

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
        // The CharacterVisual owns the flash (it tints every mesh, model or
        // primitive) and restores the original materials afterwards.
        if (_visual != null)
        {
            _visual.FlashHit();
            return;
        }

        // Legacy path for scenes without a visual pivot.
        if (Mesh is MeshInstance3D meshInstance && meshInstance.GetActiveMaterial(0) is StandardMaterial3D mat)
        {
            var tween = CreateTween();
            Color originalColor = mat.AlbedoColor;
            mat.AlbedoColor = new Color(1f, 0.2f, 0.2f);
            tween.TweenProperty(mat, "albedo_color", originalColor, 0.12f);
        }
    }
}
