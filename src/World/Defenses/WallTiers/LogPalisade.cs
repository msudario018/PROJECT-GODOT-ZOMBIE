namespace ZombieApocalypse.World.Defenses.WallTiers;

using Godot;

/// <summary>
/// Tier 3: Log Palisade.
///
/// Heavy timber spikes driven into the ground. Doubles the durability of the
/// chain link tier and, crucially, has a spike mechanic: things that ram it
/// (see <see cref="Entities.Zombies.Archetypes.BruteTank"/>) take piercing
/// damage on top of the structural damage they deal.
/// </summary>
public partial class LogPalisade : WallBase
{
    /// <summary>How many corpses/pushers it can absorb before the first breach.</summary>
    public const int ExpectedBreachLoad = 40;

    /// <summary>Piercing damage reflected to anything that charges the wall.</summary>
    [Export] public float SpikeReflectDamage = 55f;

    public override void _Ready()
    {
        WallName = "Log Palisade";
        WallTier = 3;
        MaxDurability = 850.0f;
        PressureThreshold = 32.0f;
        ObstacleRadius = 1.3f;
        BlocksVision = true;

        base._Ready();

        // Timber: burns easily, but shrugs off slashes and blunt trauma.
        Health.ResistFire = 0.15f;
        Health.ResistBlunt = 0.65f;
        Health.ResistSlash = 0.7f;
        Health.ResistPierce = 0.55f;

        AddToGroup("spiked_walls");
    }

    /// <summary>
    /// Called by a charging brute: the palisade both takes the hit and wounds
    /// the attacker on the broken timber.
    /// </summary>
    public float ReflectSpikeDamage(Entities.Zombies.ZombieBase attacker)
    {
        if (attacker == null || !GodotObject.IsInstanceValid(attacker)) return 0f;
        return attacker.Health.TakeDamage(SpikeReflectDamage, Core.Data.DamageType.Pierce, this);
    }
}
