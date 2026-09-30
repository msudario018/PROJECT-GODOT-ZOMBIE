namespace ZombieApocalypse.World.Defenses.WallTiers;

using Godot;

/// <summary>
/// Tier 4: Corrugated Metal.
///
/// The top of the tree. Sheet steel bolted to a frame: enormous durability,
/// near-immune to fire and blunt force, and the only tier that survives a brute
/// ram for more than one hit. Its weakness is electric damage (it conducts) and
/// the noise it makes when struck, which attracts the sector.
/// </summary>
public partial class CorrugatedMetal : WallBase
{
    [ExportGroup("Acoustics")]
    /// <summary>Noise radius of a hit — sheet metal is loud.</summary>
    [Export] public float ClangRadius = 28f;
    [Export] public float ClangInterval = 1.5f;

    private float _clangTimer;

    public override void _Ready()
    {
        WallName = "Corrugated Metal";
        WallTier = 4;
        MaxDurability = 1600.0f;
        PressureThreshold = 55.0f;
        ObstacleRadius = 1.4f;
        BlocksVision = true;

        base._Ready();

        // Steel: nearly fireproof and blunt-proof, but conducts electricity.
        Health.ResistFire = 0.9f;
        Health.ResistBlunt = 0.75f;
        Health.ResistSlash = 0.6f;
        Health.ResistPierce = 0.5f;
        Health.ResistElectric = 0f;

        AddToGroup("metal_walls");
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        // Metallic impacts ring loudly enough to pull zombies to the breach.
        _clangTimer -= (float)delta;
        if (_clangTimer > 0f) return;

        _clangTimer = ClangInterval;
    }

    /// <summary>Emit the metal-on-metal clang. Called when this wall takes a hit.</summary>
    public void Clang()
    {
        Core.Autoloads.EventBus.Instance?.EmitSound(GlobalPosition, ClangRadius, Core.Data.AudioSourceType.Environmental);
    }
}
