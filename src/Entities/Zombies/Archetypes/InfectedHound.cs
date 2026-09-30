namespace ZombieApocalypse.Entities.Zombies.Archetypes;

using Godot;

/// <summary>
/// Phase 8 archetype: Infected Hound.
///
/// Profile:
///   - Four-legged sprinter (6.5 m/s) that hunts by blood scent rather than
///     sight, so it will happily track a bleeding player through the dark and
///     around walls.
///   - Very fragile (40 HP) and a weak biter (10 dmg): pure pressure, no tank.
///   - Operates in packs — it alerts nearby hounds to a scent, so one trail
///     wakes the whole kennel.
/// </summary>
public partial class InfectedHound : ZombieBase
{
    [ExportGroup("Hunting")]
    /// <summary>How far the hound can smell blood.</summary>
    [Export] public float ScentTrackingRange = 28f;
    /// <summary>Strength of scent required to lock on.</summary>
    [Export] public float ScentLockThreshold = 0.15f;
    /// <summary>Radius in which other hounds are rallied onto the same trail.</summary>
    [Export] public float PackCallRadius = 18f;

    [ExportGroup("Melee")]
    /// <summary>How close the hound tries to get before lunging.</summary>
    [Export] public float LungeRange = 2.0f;
    /// <summary>Damage of the lunge.</summary>
    [Export] public float LungeDamage = 10f;

    /// <summary>True while the hound is following a blood trail.</summary>
    public bool IsOnTheTrail => Sensory.IsTrackingScent;
    /// <summary>Position the hound is currently heading to.</summary>
    public Vector3 TrailTarget => Sensory.TrackedScentPosition;
    /// <summary>How many hounds the last pack call rallied.</summary>
    public int LastPackCallCount { get; private set; }
    /// <summary>Distance from the hound to its trail target.</summary>
    public float DistanceToTrail =>
        TrailTarget.DistanceTo(GlobalPosition);

    public override void _Ready()
    {
        ArchetypeName = "Hound";
        MoveSpeed = 6.5f;              // fastest thing in the game bar the sprinter
        AttackDamage = LungeDamage;
        AttackRange = 1.6f;
        AttackCooldown = 0.9f;
        TurnSpeed = 14f;
        HordePressureForce = 0.8f;
        Gravity = 30f;

        base._Ready();

        Health.SetMaxHealth(40.0f, true);

        if (Sensory != null)
        {
            // Poor eyesight, extraordinary nose: this is a scent hunter.
            Sensory.SightRange = 12.0f;
            Sensory.SightAngleDeg = 150.0f;
            Sensory.HearingSensitivity = 1.2f;
            Sensory.ScentRange = ScentTrackingRange;
            Sensory.ScentThreshold = ScentLockThreshold;
        }

        AddToGroup("hounds");
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        if (IsDead) return;

        // TryTrackScent is already driven by the sensory tick; this reads the
        // result and rallies the pack the first time a trail is acquired.
        if (IsOnTheTrail && LastPackCallCount == 0)
            CallPack();
    }

    /// <summary>
    /// Follow the blood trail. Returns the velocity the hound should use, or
    /// zero when there is no trail to follow.
    /// </summary>
    public Vector3 GetTrailVelocity(float delta)
    {
        if (!IsOnTheTrail) return Vector3.Zero;

        Vector3 toTrail = TrailTarget - GlobalPosition;
        toTrail.Y = 0f;
        if (toTrail.LengthSquared() < 0.04f) return Vector3.Zero;

        Vector3 direction = toTrail.Normalized();
        float speed = EffectiveMoveSpeed;
        FaceDirection(direction, delta);

        return new Vector3(direction.X * speed, Velocity.Y, direction.Z * speed);
    }

    /// <summary>Rally nearby hounds onto the same trail.</summary>
    public int CallPack()
    {
        int called = 0;
        Vector3 rallyPoint = IsOnTheTrail ? TrailTarget : GlobalPosition;

        foreach (var node in GetTree().GetNodesInGroup("hounds"))
        {
            if (node is not InfectedHound hound || hound == this) continue;
            if (!GodotObject.IsInstanceValid(hound) || hound.IsDead) continue;
            if (hound.GlobalPosition.DistanceTo(GlobalPosition) > PackCallRadius) continue;

            hound.AlertTo(rallyPoint, 15f);
            called++;
        }

        LastPackCallCount = called;
        if (called > 0)
            GD.Print($"[InfectedHound] Pack call — {called} hound(s) joined the trail at {rallyPoint:F1}.");
        return called;
    }

    /// <summary>Lunge at a target, damaging it on contact.</summary>
    public void Lunge(Node3D target)
    {
        if (IsDead || target == null || !GodotObject.IsInstanceValid(target)) return;

        var health = target.GetNodeOrNull<Core.Components.HealthComponent>("HealthComponent");
        health?.TakeDamage(LungeDamage, Core.Data.DamageType.Slash, this);

        // Short burst of speed in the target's direction.
        Vector3 direction = (target.GlobalPosition - GlobalPosition).Normalized();
        Velocity += new Vector3(direction.X * 6f, 0f, direction.Z * 6f);

        GD.Print($"[InfectedHound] Lunged at {target.Name} for {LungeDamage:F0}.");
    }
}
