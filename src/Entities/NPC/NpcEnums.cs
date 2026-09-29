namespace ZombieApocalypse.Entities.NPC;

/// <summary>Task priority for survivor AI. Lower numeric value = higher priority.</summary>
public enum SurvivorTask
{
    /// <summary>No assigned work. Survivor wanders near the camp anchor.</summary>
    Idle = 0,
    /// <summary>Engage zombies / bandits threatening the camp anchor.</summary>
    Defend = 1,
    /// <summary>Gather supplies from the nearest unsearched loot container.</summary>
    Scavenge = 2,
    /// <summary>Repair the most damaged wall segment.</summary>
    Repair = 3,
    /// <summary>Deliver a fuel canister to the nearest generator.</summary>
    RefuelPower = 4,
    /// <summary>Heal the most injured ally, including the player.</summary>
    Heal = 5,
}

/// <summary>Playable survivor archetype. Each maps to combat / support stat presets.</summary>
public enum SurvivorArchetype
{
    CombatMedic,
    CombatVeteran,
    ScavengerScout
}

/// <summary>Bandit tier. Higher tiers are tougher, better armed, and fight longer.</summary>
public enum BanditTier
{
    Scavenger = 1,
    Militia = 2,
    Warlord = 3
}

/// <summary>Behavioral role assigned by <see cref="Bandits.BanditSquad"/> during combat.</summary>
public enum SquadRole
{
    Advance,
    Suppress,
    Flank
}

/// <summary>Tactical sub-state of a <see cref="Bandits.BanditBase"/> in combat.</summary>
public enum BanditCombatState
{
    Advance,
    Suppress,
    Flank,
    Loot,
    Retreat,
    Dead
}
