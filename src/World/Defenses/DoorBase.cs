using Godot;
using ZombieApocalypse.Core.Autoloads;

namespace ZombieApocalypse.World.Defenses;

/// <summary>
/// Interactive doorway / portal defense structure.
/// 
/// Behaviors:
///   - Closed: Blocks physical movement, blocks vision (Layer 4), registers NavObstacle,
///     and forces zombies to attack the door to break through.
///   - Open: Physical collision disabled, NavObstacle disabled, vision clear,
///     rotates visual door leaf by 90 degrees.
///   - Dynamic flow-field invalidation on open/close state transitions.
/// </summary>
public partial class DoorBase : WallBase
{
    [ExportGroup("Door State")]
    [Export] public bool IsOpen { get; private set; } = false;
    [Export] public float InteractionRange = 3.0f;

    private CollisionShape3D? _collision;
    private Node3D? _doorLeaf;

    public override void _Ready()
    {
        WallName = "Reinforced Door";
        WallTier = 1;
        MaxDurability = 220.0f;
        PressureThreshold = 8.0f;
        ObstacleRadius = 1.0f;
        BlocksVision = true;

        base._Ready();

        _collision = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
        _doorLeaf = GetNodeOrNull<Node3D>("DoorLeaf") ?? GetNodeOrNull<Node3D>("Mesh");

        AddToGroup("interactables");
    }

    /// <summary>
    /// Toggle door open / closed.
    /// </summary>
    public void ToggleDoor()
    {
        SetDoorOpen(!IsOpen);
    }

    /// <summary>
    /// Set door open or closed with full physics, navigation obstacle, and visual updates.
    /// </summary>
    public void SetDoorOpen(bool open)
    {
        if (IsOpen == open) return;
        IsOpen = open;

        if (_collision != null)
        {
            _collision.Disabled = IsOpen;
        }

        if (NavObstacle != null)
        {
            NavObstacle.AvoidanceEnabled = !IsOpen;
        }

        // Update vision occluder on layer 4
        SetCollisionLayerValue(4, !IsOpen);

        // Animate door leaf rotation
        if (_doorLeaf != null)
        {
            var tween = CreateTween();
            float targetAngle = IsOpen ? Mathf.Pi * 0.5f : 0f;
            tween.TweenProperty(_doorLeaf, "rotation:y", targetAngle, 0.25f);
        }

        // Fire navigation events to invalidate flow field
        if (IsOpen)
        {
            EventBus.Instance?.EmitNavObstacleRemoved(GlobalPosition);
            GD.Print($"[DoorBase] {WallName} opened at {GlobalPosition}.");
        }
        else
        {
            EventBus.Instance?.EmitNavObstacleAdded(GlobalPosition);
            GD.Print($"[DoorBase] {WallName} closed at {GlobalPosition}.");
        }
    }
}
