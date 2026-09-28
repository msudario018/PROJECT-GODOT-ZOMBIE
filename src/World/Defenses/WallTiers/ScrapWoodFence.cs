using Godot;
using ZombieApocalypse.Core.Data;

namespace ZombieApocalypse.World.Defenses.WallTiers;

/// <summary>
/// Tier 1: Scrap Wood Fence.
/// Cheap, improvised barricade. Low durability, flammable, blocks vision.
/// </summary>
public partial class ScrapWoodFence : WallBase
{
    public override void _Ready()
    {
        WallName = "Scrap Wood Fence";
        WallTier = 1;
        MaxDurability = 250.0f;
        PressureThreshold = 10.0f;
        ObstacleRadius = 1.1f;
        BlocksVision = true;

        base._Ready();

        // Fire weakness
        Health.ResistFire = 0.0f;
        Health.ResistBlunt = 0.1f;
        Health.ResistSlash = 0.2f;
    }
}
