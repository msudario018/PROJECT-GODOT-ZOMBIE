using Godot;
using ZombieApocalypse.Entities.NPC;

namespace ZombieApocalypse.Entities.Survivors;

/// <summary>Combat medic: heals 50% more, fights at base strength.</summary>
public partial class CombatMedic : SurvivorBase
{
    /// <summary>Multiplier applied to <see cref="SurvivorBase.HealAmount"/>.</summary>
    [Export] public float HealMultiplier = 1.5f;

    public override void _EnterTree()
    {
        Archetype = SurvivorArchetype.CombatMedic;
        MaxHealth = 90f;
        MeleeDamage = 12.0f;
        MoveSpeed = 3.6f;
        base._EnterTree();
    }
}

/// <summary>Combat veteran: tougher and harder-hitting than a base survivor.</summary>
public partial class CombatVeteran : SurvivorBase
{
    public override void _EnterTree()
    {
        Archetype = SurvivorArchetype.CombatVeteran;
        MaxHealth = 140f;
        ArmorRating = 2f;
        MeleeDamage = 22.0f;
        MoveSpeed = 3.6f;
        base._EnterTree();
    }
}

/// <summary>Scavenger scout: fast mover, quick searcher, fragile in a fight.</summary>
public partial class ScavengerScout : SurvivorBase
{
    /// <summary>Multiplier applied to container search time.</summary>
    [Export] public float SearchTimeMultiplier = 0.6f;

    public override void _EnterTree()
    {
        Archetype = SurvivorArchetype.ScavengerScout;
        MaxHealth = 80f;
        MeleeDamage = 10.0f;
        MoveSpeed = 4.4f;
        base._EnterTree();
    }
}
