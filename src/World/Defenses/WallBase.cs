using Godot;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Core.Vision;

namespace ZombieApocalypse.World.Defenses;

/// <summary>
/// Base class for all defensive wall segments.
/// 
/// Integrates:
///   - Dynamic <see cref="NavigationObstacle3D"/>: instantly pushes zombie pathfinding avoidance
///     and invalidates the flow-field without requiring a full thread-locking NavigationRegion3D rebake.
///   - <see cref="HealthComponent"/>: tracks structural HP and per-type resistances.
///   - <see cref="VisionOccluder"/>: blocks player/zombie field of view (for opaque walls).
///   - Structural pressure accumulation from pressing zombie hordes.
/// </summary>
public partial class WallBase : StaticBody3D
{
    [ExportGroup("Wall Specifications")]
    [Export] public string WallName = "Wall Segment";
    [Export] public int WallTier = 1;
    [Export] public float MaxDurability = 250.0f;
    [Export] public float PressureThreshold = 10.0f;
    [Export] public float ObstacleRadius = 1.2f;
    [Export] public bool BlocksVision = true;

    // Component references
    public HealthComponent Health { get; private set; } = null!;
    public NavigationObstacle3D NavObstacle { get; private set; } = null!;
    public VisionOccluder? Occluder { get; private set; }

    // Structural pressure state
    public float AccumulatedPressure { get; private set; } = 0f;
    private int _pushingZombieCount = 0;
    private const float PressureDissipationRate = 4.0f;

    public override void _Ready()
    {
        // Collision layer 1 (World / Obstacle)
        SetCollisionLayerValue(1, true);

        Health = GetNodeOrNull<HealthComponent>("HealthComponent") ?? new HealthComponent();
        if (Health.GetParent() == null) AddChild(Health);
        Health.SetMaxHealth(MaxDurability, true);
        Health.Died += OnDestroyed;
        Health.DamageTaken += OnDamageTaken;

        // Dynamic NavMesh Obstacle setup (Section 6c)
        NavObstacle = GetNodeOrNull<NavigationObstacle3D>("NavigationObstacle3D") ?? new NavigationObstacle3D();
        if (NavObstacle.GetParent() == null) AddChild(NavObstacle);
        NavObstacle.Radius = ObstacleRadius;
        NavObstacle.AvoidanceEnabled = true;

        // Vision Occluder (Layer 4)
        if (BlocksVision)
        {
            Occluder = GetNodeOrNull<VisionOccluder>("VisionOccluder") ?? new VisionOccluder();
            if (Occluder.GetParent() == null) AddChild(Occluder);
        }

        // Notify navigation and flow-field systems
        EventBus.Instance?.EmitNavObstacleAdded(GlobalPosition);

        AddToGroup("walls");
        AddToGroup("nav_geometry");   // runtime NavigationBaker bakes holes for walls
        GD.Print($"[WallBase] Placed {WallName} (Tier {WallTier}, HP: {MaxDurability}) at {GlobalPosition}.");
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        if (_pushingZombieCount > 0)
        {
            // Horde pressure accumulation (Section 8): pressure * (1 + 0.05 * zombie_count)
            float pressureRate = _pushingZombieCount * (1.0f + 0.05f * _pushingZombieCount);
            AccumulatedPressure += pressureRate * dt;

            if (AccumulatedPressure >= PressureThreshold)
            {
                // Burst structural pressure damage
                float burstDamage = 15.0f * (1.0f + 0.1f * _pushingZombieCount);
                AccumulatedPressure = 0f;
                Health.TakeDamage(burstDamage, DamageType.Blunt, null);
                GD.Print($"[WallBase] {WallName} took {burstDamage:F1} structural pressure damage from {_pushingZombieCount} zombies!");
            }
        }
        else if (AccumulatedPressure > 0f)
        {
            // Dissipate pressure when no zombies are pushing
            AccumulatedPressure = Mathf.Max(0f, AccumulatedPressure - PressureDissipationRate * dt);
        }
    }

    /// <summary>
    /// Register a zombie pressing against this wall segment.
    /// </summary>
    public void AddPushingZombie(float force)
    {
        _pushingZombieCount++;
        AccumulatedPressure += force * 0.5f;
    }

    /// <summary>
    /// Deregister a zombie that stopped pressing against this wall.
    /// </summary>
    public void RemovePushingZombie()
    {
        _pushingZombieCount = Mathf.Max(0, _pushingZombieCount - 1);
    }

    private void OnDamageTaken(float damage, int damageType)
    {
        EventBus.Instance?.EmitWallDamaged(this, damage);
    }

    private void OnDestroyed()
    {
        GD.Print($"[WallBase] {WallName} destroyed at {GlobalPosition}!");

        EventBus.Instance?.EmitWallDamaged(this, Health.MaxHealth);
        EventBus.Instance?.EmitNavObstacleRemoved(GlobalPosition);
        EventBus.Instance?.EmitStructuralFailure(this);

        if (GodotObject.IsInstanceValid(NavObstacle))
        {
            NavObstacle.QueueFree();
        }

        QueueFree();
    }
}
