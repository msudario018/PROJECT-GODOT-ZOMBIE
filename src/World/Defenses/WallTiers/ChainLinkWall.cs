using Godot;

namespace ZombieApocalypse.World.Defenses.WallTiers;

/// <summary>
/// Tier 2: Chain Link Wall.
/// Industrial perimeter security. High durability, non-flammable, transparent to line of sight.
/// Allows players to shoot through and spot zombies before they breach.
/// </summary>
public partial class ChainLinkWall : WallBase
{
    public override void _Ready()
    {
        WallName = "Chain Link Wall";
        WallTier = 2;
        MaxDurability = 500.0f;
        PressureThreshold = 20.0f;
        ObstacleRadius = 1.1f;
        BlocksVision = false; // Does NOT block line of sight or fog of war

        base._Ready();

        Health.ResistFire = 0.95f;
        Health.ResistBlunt = 0.35f;
        Health.ResistSlash = 0.5f;
    }
}
