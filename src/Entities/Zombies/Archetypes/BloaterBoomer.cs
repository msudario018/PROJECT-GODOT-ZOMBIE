using Godot;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Core.Utilities;
using ZombieApocalypse.World.Defenses;

namespace ZombieApocalypse.Entities.Zombies.Archetypes;

/// <summary>
/// Bloater / Boomer archetype.
/// 
/// Profile:
///   - Slow, dragging gait (1.2 m/s), high durability (160 HP).
///   - Threat: Explodes on death or when within 1.8m of target!
///   - Deals 50 AoE explosive damage in a 4.5m radius, damaging players, walls, and other zombies.
///   - Emits a massive acoustic signature (65m), alerting the entire surrounding region.
/// </summary>
public partial class BloaterBoomer : ZombieBase
{
    [ExportGroup("Explosion Tuning")]
    [Export] public float ExplosionRadius = 4.5f;
    [Export] public float ExplosionDamage = 50.0f;
    [Export] public float DetonationProximity = 1.8f;

    private bool _hasExploded = false;
    private bool _isDetonating = false;

    public override void _Ready()
    {
        ArchetypeName = "Bloater";
        MoveSpeed = 1.2f;
        AttackDamage = 20.0f;
        AttackRange = 1.6f;
        AttackCooldown = 2.0f;
        HordePressureForce = 2.5f; // Massive structural push force

        base._Ready();

        Health.SetMaxHealth(160.0f, true);
        Health.Died += TriggerExplosion;
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        if (_isDetonating || _hasExploded || IsDead) return;

        // Check proximity to target for suicide detonation
        var target = Sensory?.CurrentTarget;
        if (target != null && GodotObject.IsInstanceValid(target))
        {
            float distSq = MathUtils.DistanceSquaredXZ(GlobalPosition, target.GlobalPosition);
            if (distSq <= DetonationProximity * DetonationProximity)
            {
                StartDetonationFuse();
            }
        }
    }

    private void StartDetonationFuse()
    {
        _isDetonating = true;
        Velocity = Vector3.Zero;

        // Visual fuse: swell up and flash toxic yellow
        var tween = CreateTween();
        tween.TweenProperty(Mesh, "scale", new Vector3(1.4f, 1.4f, 1.4f), 0.7f);
        tween.TweenCallback(Callable.From(TriggerExplosion));
    }

    private void TriggerExplosion()
    {
        if (_hasExploded) return;
        _hasExploded = true;

        Vector3 epicentre = GlobalPosition;
        float radiusSq = ExplosionRadius * ExplosionRadius;

        // Acoustic shockwave
        AudioEmitter?.EmitCustomSound(65.0f, AudioSourceType.Explosion);

        // Visual toxic blast sphere
        SpawnExplosionVisual(epicentre);

        // Damage all nearby entities (Player, other Zombies, Walls)
        var nearbyEntities = SharedSpatialGrid.QueryRadius(epicentre, ExplosionRadius + 1.0f);
        foreach (var entity in nearbyEntities)
        {
            if (entity == this) continue;

            float distSq = MathUtils.DistanceSquaredXZ(epicentre, entity.GlobalPosition);
            if (distSq <= radiusSq)
            {
                float falloff = 1.0f - (Mathf.Sqrt(distSq) / ExplosionRadius);
                float damage = ExplosionDamage * Mathf.Clamp(falloff, 0.3f, 1.0f);

                if (entity is ZombieBase zombie && !zombie.IsDead)
                {
                    Vector3 knockDir = (zombie.GlobalPosition - epicentre).Normalized();
                    knockDir.Y = 0.2f;
                    zombie.ApplyDamage(damage, DamageType.Explosive, this, knockDir * 12.0f);
                }
                else if (entity is Node3D node)
                {
                    var health = node.GetNodeOrNull<HealthComponent>("HealthComponent");
                    health?.TakeDamage(damage, DamageType.Explosive, this);
                }
            }
        }

        // Also damage player if within blast radius
        var player = GetTree().GetFirstNodeInGroup("player") as Node3D;
        if (player != null && GodotObject.IsInstanceValid(player))
        {
            float distSq = MathUtils.DistanceSquaredXZ(epicentre, player.GlobalPosition);
            if (distSq <= radiusSq)
            {
                float falloff = 1.0f - (Mathf.Sqrt(distSq) / ExplosionRadius);
                float damage = ExplosionDamage * Mathf.Clamp(falloff, 0.3f, 1.0f);
                var health = player.GetNodeOrNull<HealthComponent>("HealthComponent");
                health?.TakeDamage(damage, DamageType.Explosive, this);
            }
        }

        GD.Print($"[BloaterBoomer] Exploded at {epicentre}! Radius: {ExplosionRadius}m, Damage: {ExplosionDamage}.");

        // Clean up bloater
        QueueFree();
    }

    private void SpawnExplosionVisual(Vector3 position)
    {
        var blast = new MeshInstance3D();
        var sphere = new SphereMesh { Radius = ExplosionRadius, Height = ExplosionRadius * 2f };
        blast.Mesh = sphere;

        var mat = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.85f, 0.9f, 0.2f, 0.6f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        blast.MaterialOverride = mat;
        blast.Position = position + Vector3.Up * 1.0f;

        GetTree().CurrentScene?.AddChild(blast);

        var tween = blast.CreateTween();
        tween.TweenProperty(blast, "scale", new Vector3(1.2f, 1.2f, 1.2f), 0.25f);
        tween.Parallel().TweenProperty(mat, "albedo_color:a", 0f, 0.25f);
        tween.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(blast))
                blast.QueueFree();
        }));
    }
}
