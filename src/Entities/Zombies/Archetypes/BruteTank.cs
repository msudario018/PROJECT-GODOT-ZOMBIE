namespace ZombieApocalypse.Entities.Zombies.Archetypes;

using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.World.Defenses;

/// <summary>
/// Phase 8 archetype: Brute / Tank.
///
/// Profile:
///   - Enormous health pool with heavy resistances; walks through gunfire.
///   - The <see cref="RamCharge"/> is the real threat: a telegraphed dash that
///     smashes barricades and shoves anything in front of it. Telegraphed by
///     <see cref="IsWindingUp"/>, so a good player can sidestep or bait it into
///     a trap instead of eating it.
///   - Slow to turn, so flanking punji trenches and tripwires punish it.
/// </summary>
public partial class BruteTank : ZombieBase
{
    public enum RamState
    {
        /// <summary>Walking normally.</summary>
        Idle,

        /// <summary>Crouching before the charge — the player's cue to move.</summary>
        WindUp,

        /// <summary>Charging at high speed.</summary>
        Charging,

        /// <summary>Recovering after a hit; cannot charge again yet.</summary>
        Recovering
    }

    [ExportGroup("Charge")]
    /// <summary>Seconds of wind-up before the charge starts.</summary>
    [Export] public float WindUpDuration = 1.1f;
    /// <summary>Seconds the charge lasts.</summary>
    [Export] public float ChargeDuration = 1.6f;
    /// <summary>Seconds of recovery after a charge ends.</summary>
    [Export] public float RecoverDuration = 2.0f;
    /// <summary>Range at which a target triggers a charge.</summary>
    [Export] public float ChargeTriggerRange = 12f;
    /// <summary>Minimum seconds between charges.</summary>
    [Export] public float ChargeCooldown = 5f;
    /// <summary>Speed multiplier while charging.</summary>
    [Export] public float ChargeSpeedMultiplier = 3.2f;

    [ExportGroup("Impact")]
    /// <summary>Damage dealt to barricades struck by a charge.</summary>
    [Export] public float BarricadeDamage = 120f;
    /// <summary>Damage dealt to characters struck by a charge.</summary>
    [Export] public float ShoveDamage = 28f;
    /// <summary>Impulse applied to characters struck by a charge.</summary>
    [Export] public float ShoveImpulse = 14f;
    /// <summary>Radius around the brute that a charge damages.</summary>
    [Export] public float ImpactRadius = 2.2f;
    [Export] public uint ImpactMask = 1 | 2 | 4 | 5 | 6;   // World, Player, Zombies, Survivors, Bandits

    /// <summary>Current charge phase.</summary>
    public RamState State { get; private set; } = RamState.Idle;
    /// <summary>True while the brute is winding up (the dodge window).</summary>
    public bool IsWindingUp => State == RamState.WindUp;
    /// <summary>True while the charge is active.</summary>
    public bool IsCharging => State == RamState.Charging;
    /// <summary>Barricades damaged by the last charge.</summary>
    public int BarricadesBroken { get; private set; }
    /// <summary>Characters struck by the last charge.</summary>
    public int EntitiesShoved { get; private set; }
    /// <summary>Direction the charge is locked to (set at wind-up start).</summary>
    public Vector3 ChargeDirection { get; private set; }

    private float _stateTimer;
    private float _cooldown;
    private readonly HashSet<ulong> _alreadyHit = new();

    public override void _Ready()
    {
        ArchetypeName = "Brute";
        MoveSpeed = 1.7f;              // slow, heavy
        AttackDamage = 42.0f;          // a single swing is punishing
        AttackRange = 2.2f;
        AttackCooldown = 2.4f;
        TurnSpeed = 3.0f;              // commits hard to a direction
        HordePressureForce = 3.0f;     // a siege engine at a gate

        base._Ready();

        Health.SetMaxHealth(600.0f, true);
        Health.ArmorRating = 10f;
        Health.ResistSlash = 0.45f;
        Health.ResistPierce = 0.35f;
        Health.ResistBlunt = 0.5f;

        if (Sensory != null)
        {
            Sensory.SightRange = 14.0f;    // tunnel vision
            Sensory.SightAngleDeg = 90.0f;
            Sensory.ScentRange = 12f;      // follows blood readily
        }

        AddToGroup("brutes");
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        if (IsDead) return;

        float dt = (float)delta;
        _cooldown = Mathf.Max(0f, _cooldown - dt);
        _stateTimer -= dt;

        // GetNodeOrNull: a bare brute may have no sensory node yet.
        var senses = GetNodeOrNull<ZombieAI.SensorySystem>("SensorySystem");

        switch (State)
        {
            case RamState.Idle:
                if (senses?.HasTarget == true && _cooldown <= 0f
                    && senses.CurrentTarget!.GlobalPosition.DistanceTo(GlobalPosition) <= ChargeTriggerRange)
                {
                    BeginWindUp();
                }
                break;

            case RamState.WindUp:
                // Direction is locked at wind-up: the player can read and dodge.
                if (_stateTimer <= 0f) EnterState(RamState.Charging, ChargeDuration);
                break;

            case RamState.Charging:
                ApplyChargeVelocity(delta);
                ApplyChargeImpact();
                if (_stateTimer <= 0f) EnterState(RamState.Recovering, RecoverDuration);
                break;

            case RamState.Recovering:
                if (_stateTimer <= 0f) EnterState(RamState.Idle, 0f);
                break;
        }
    }

    /// <summary>Start the telegraphed charge toward the current target.</summary>
    public void BeginWindUp()
    {
        Vector3 toTarget = (Sensory?.CurrentTarget?.GlobalPosition ?? GlobalPosition) - GlobalPosition;
        toTarget.Y = 0f;

        ChargeDirection = toTarget.LengthSquared() > 0.001f ? toTarget.Normalized() : Vector3.Forward;
        _alreadyHit.Clear();
        BarricadesBroken = 0;
        EntitiesShoved = 0;

        EnterState(RamState.WindUp, WindUpDuration);
        GD.Print($"[BruteTank] Charging up! ({WindUpDuration:F1}s to dodge)");
    }

    /// <summary>Force the brute straight into the charge, skipping the wind-up.</summary>
    public void RamCharge()
    {
        if (IsDead) return;
        BeginWindUp();
        EnterState(RamState.Charging, ChargeDuration);
    }

    private void EnterState(RamState next, float duration)
    {
        State = next;
        _stateTimer = duration;

        if (next == RamState.Idle)
        {
            _cooldown = ChargeCooldown;
            Velocity = new Vector3(0f, Velocity.Y, 0f);
        }
    }

    private void ApplyChargeVelocity(double delta)
    {
        float speed = EffectiveMoveSpeed * ChargeSpeedMultiplier;
        Velocity = new Vector3(ChargeDirection.X * speed, Velocity.Y, ChargeDirection.Z * speed);
        FaceDirection(ChargeDirection, (float)delta);
    }

    /// <summary>
    /// Damage everything the charge runs into: barricades take structural
    /// damage, characters are shoved and hurt. Each target is hit once per charge.
    /// </summary>
    private void ApplyChargeImpact()
    {
        var space = GetWorld3D()?.DirectSpaceState;
        if (space == null) return;

        var shape = new SphereShape3D { Radius = ImpactRadius };
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = shape,
            Transform = new Transform3D(Basis.Identity, GlobalPosition + Vector3.Up),
            CollisionMask = ImpactMask,
            CollideWithBodies = true,
        };

        foreach (var hit in space.IntersectShape(query, 12))
        {
            if (!hit.TryGetValue("collider", out var colliderVariant)) continue;
            var collider = colliderVariant.AsGodotObject();
            if (collider == null || !GodotObject.IsInstanceValid(collider)) continue;

            var node = collider as Node;
            if (node == null || node == this) continue;
            if (!_alreadyHit.Add(node.GetInstanceId())) continue;   // one hit per charge

            // Barricade: heavy structural damage.
            var wall = FindInParents<WallBase>(node);
            if (wall != null)
            {
                wall.Health.TakeDamage(BarricadeDamage, DamageType.Blunt, this);
                BarricadesBroken++;

                // A palisade bites back: broken timber wounds the rammer.
                if (wall is ZombieApocalypse.World.Defenses.WallTiers.LogPalisade palisade)
                {
                    float reflected = palisade.ReflectSpikeDamage(this);
                    GD.Print($"[BruteTank] Took {reflected:F0} piercing damage from the spikes.");
                }

                wall.Health.Died += OnWallDestroyedByRam;
                GD.Print($"[BruteTank] SMASHED {wall.WallName} for {BarricadeDamage:F0}!");
                continue;
            }

            // Character: shove + damage. CharacterBody3D has no impulse API, so
            // the shove is applied as a velocity change on the next step.
            var health = FindInParents<HealthComponent>(node);
            if (health == null || !health.IsAlive) continue;

            health.TakeDamage(ShoveDamage, DamageType.Blunt, this);
            EntitiesShoved++;

            if (health.GetParent() is CharacterBody3D body)
                Shove(body, ChargeDirection * ShoveImpulse);
        }
    }

    /// <summary>Raised when a wall this charge destroyed collapses.</summary>
    private void OnWallDestroyedByRam()
    {
        GD.Print("[BruteTank] A barricade gave way — the perimeter is breached!");
    }

    /// <summary>Push a character body away from the charge.</summary>
    private static void Shove(CharacterBody3D body, Vector3 impulse)
    {
        body.Velocity += new Vector3(impulse.X, impulse.Y * 0.3f, impulse.Z);
    }

    private static T? FindInParents<T>(Node? start) where T : Node
    {
        Node? current = start;
        while (current != null)
        {
            if (current is T match) return match;
            current = current.GetParent();
        }
        return null;
    }
}
