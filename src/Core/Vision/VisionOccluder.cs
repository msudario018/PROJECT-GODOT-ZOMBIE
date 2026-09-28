using Godot;

namespace ZombieApocalypse.Core.Vision;

/// <summary>
/// Marker component that registers its parent CollisionObject3D as a
/// vision-blocking obstacle for the fog-of-war system.
///
/// When attached as a child of a StaticBody3D (walls, buildings, obstacles),
/// it adds collision layer 4 to the parent so that <see cref="FieldOfView"/>
/// raycasts detect it as a line-of-sight blocker.
///
/// Usage: Add a VisionOccluder child node to any StaticBody3D that should
/// block the player's line of sight.
/// </summary>
public partial class VisionOccluder : Node3D
{
    /// <summary>
    /// The collision layer index (1-based) used for vision-blocking geometry.
    /// FieldOfView raycasts are masked to only detect this layer.
    /// </summary>
    public const int VisionBlockerLayerIndex = 4;

    public override void _Ready()
    {
        if (GetParent() is CollisionObject3D body)
        {
            body.SetCollisionLayerValue(VisionBlockerLayerIndex, true);
        }
        else
        {
            GD.PrintErr($"[VisionOccluder] Parent '{GetParent()?.Name ?? "null"}' is not a " +
                         "CollisionObject3D. Vision blocking will not work.");
        }
    }
}
