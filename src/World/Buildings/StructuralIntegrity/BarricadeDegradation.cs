using Godot;
using ZombieApocalypse.Entities.Zombies;
using ZombieApocalypse.World.Defenses;

namespace ZombieApocalypse.World.Buildings.StructuralIntegrity;

/// <summary>
/// Monitors physical contact from zombie hordes against a wall or barricade.
/// Automatically increments horde pressure on the parent <see cref="WallBase"/>.
/// </summary>
public partial class BarricadeDegradation : Area3D
{
    private WallBase? _wall;

    public override void _Ready()
    {
        _wall = GetParent() as WallBase;

        // Monitor layer 3 (Zombies)
        CollisionLayer = 0;
        CollisionMask = 4; // Layer 3 (Zombies = bit 2, mask = 4)

        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

    private void OnBodyEntered(Node3D body)
    {
        if (body is ZombieBase zombie && _wall != null)
        {
            _wall.AddPushingZombie(zombie.HordePressureForce);
        }
    }

    private void OnBodyExited(Node3D body)
    {
        if (body is ZombieBase && _wall != null)
        {
            _wall.RemovePushingZombie();
        }
    }
}
