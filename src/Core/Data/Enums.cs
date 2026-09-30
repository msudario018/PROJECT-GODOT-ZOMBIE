namespace ZombieApocalypse.Core.Data;

/// <summary>
/// Classification of audio sources for the acoustic propagation system.
/// Determines how sound events are processed and how zombies react.
/// </summary>
public enum AudioSourceType
{
    Footstep,
    FootstepSprint,
    Gunshot,
    GunshotSuppressed,
    MeleeImpact,
    Explosion,
    VoiceHuman,
    VoiceZombie,
    Generator,
    Alarm,
    DoorBreak,
    Construction,
    Environmental
}

/// <summary>
/// Damage type classification for the combat system.
/// Each entity can have resistances and weaknesses to specific types.
/// </summary>
public enum DamageType
{
    Blunt,
    Slash,
    Pierce,
    Ballistic,
    Fire,
    Explosive,
    Toxic,
    Electric
}

/// <summary>
/// Item categories for inventory sorting and loot table filtering.
/// </summary>
public enum ItemCategory
{
    Weapon,
    Ammo,
    Medical,
    Provision,
    Crafting,
    Hardware,
    Electronics,
    Tool,
    Armor,
    Misc
}

/// <summary>
/// Top-level game state managed by GameManager.
/// </summary>
public enum GameState
{
    MainMenu,
    Loading,
    Playing,
    Paused,
    GameOver
}

/// <summary>
/// Seasonal cycle affecting weather, crop yields, battery efficiency, etc.
/// </summary>
public enum Season
{
    Spring,
    Summer,
    Autumn,
    Winter
}

/// <summary>
/// Weather states driven by the WeatherSystem. Each state changes how much
/// daylight reaches the ground, how fast the player dehydrates, and whether
/// rotting miasma is washed out or left to spread.
/// </summary>
public enum WeatherState
{
    Clear,
    Overcast,
    Rain,
    Storm,
    Fog
}

/// <summary>
/// Time-of-day phases for lighting, zombie behavior, and visibility.
/// </summary>
public enum TimeOfDay
{
    Dawn,
    Morning,
    Afternoon,
    Dusk,
    Night,
    Midnight
}

/// <summary>
/// Fog-of-war visibility states per grid cell.
/// </summary>
public enum VisibilityState
{
    /// <summary>Never seen — fully obscured.</summary>
    Hidden,
    /// <summary>Previously seen — terrain visible but entities hidden.</summary>
    Explored,
    /// <summary>Currently in player's field of view — full real-time info.</summary>
    Visible
}

/// <summary>
/// Priority tiers for power grid load shedding.
/// </summary>
public enum PowerPriority
{
    /// <summary>24/7 critical loads: refrigeration, water pumps, motion sensors.</summary>
    Critical,
    /// <summary>Switchable defense loads: fences, turrets, floodlights.</summary>
    Defensive,
    /// <summary>On-demand utility loads: workbenches, radios, tool chargers.</summary>
    Utility
}
