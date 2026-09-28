using Godot;
using ZombieApocalypse.Core.Utilities;
using ZombieApocalypse.World.Defenses;

namespace ZombieApocalypse.Entities.Player;

/// <summary>
/// Handles player interaction with world entities (doors, containers, switches).
/// </summary>
public partial class PlayerInteraction : Node3D
{
    [Export] public float InteractionRange = 2.8f;

    private PlayerController _player = null!;

    public override void _Ready()
    {
        _player = GetOwner<PlayerController>() ?? (GetParent() as PlayerController)!;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("interact"))
        {
            TryInteract();
        }
    }

    private void TryInteract()
    {
        var interactables = GetTree().GetNodesInGroup("interactables");
        Vector3 playerPos = _player.GlobalPosition;
        float rangeSq = InteractionRange * InteractionRange;

        DoorBase? closestDoor = null;
        float closestDistSq = float.MaxValue;

        foreach (var node in interactables)
        {
            if (node is DoorBase door)
            {
                float distSq = MathUtils.DistanceSquaredXZ(playerPos, door.GlobalPosition);
                if (distSq <= rangeSq && distSq < closestDistSq)
                {
                    closestDistSq = distSq;
                    closestDoor = door;
                }
            }
        }

        if (closestDoor != null)
        {
            closestDoor.ToggleDoor();
        }
    }
}
