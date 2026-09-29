using Godot;
using ZombieApocalypse.Core.Data;

namespace ZombieApocalypse.Core.Data;

/// <summary>
/// Defines a single item type in the game world.
/// All instances of the same item share this definition (flyweight pattern).
/// Used both as a C# class and as a Godot Resource for editor-driven loot tables.
/// </summary>
[GlobalClass]
public partial class ItemData : Resource
{
    [Export] public string ItemId       = "unknown";
    [Export] public string DisplayName  = "Unknown Item";
    [Export] public string Description  = "";
    [Export] public ItemCategory Category = ItemCategory.Misc;

    /// <summary>Maximum stack size. 1 = unstackable (weapons, armor).</summary>
    [Export] public int MaxStack   = 1;

    /// <summary>Weight in kilograms per single unit.</summary>
    [Export] public float WeightKg = 0.5f;

    /// <summary>Colour tint used for the inventory icon background.</summary>
    [Export] public Color IconColor = new(0.6f, 0.6f, 0.6f);

    // ── Usage effects (interpreted by PlayerStats / CraftingSystem) ──────────
    /// <summary>HP restored when consumed (Medical / Provision).</summary>
    [Export] public float HealAmount    = 0f;
    /// <summary>Hunger reduction when consumed.</summary>
    [Export] public float FoodValue     = 0f;
    /// <summary>Thirst reduction when consumed.</summary>
    [Export] public float WaterValue    = 0f;
    /// <summary>Stamina bonus when consumed.</summary>
    [Export] public float StaminaBonus  = 0f;

    // ── Common item definitions (static registry shortcuts) ─────────────────
    public static ItemData Bandage      => _bandage;
    public static ItemData MedKit       => _medKit;
    public static ItemData FoodCan      => _foodCan;
    public static ItemData WaterBottle  => _waterBottle;
    public static ItemData Scrap        => _scrap;
    public static ItemData Cloth        => _cloth;
    public static ItemData WoodPlank    => _woodPlank;
    public static ItemData Nails        => _nails;
    public static ItemData RopeLen      => _rope;
    public static ItemData Ammo9mm      => _ammo9mm;
    public static ItemData Ammo12g      => _ammo12g;
    public static ItemData Shovel       => _shovel;
    public static ItemData FuelCan      => _fuelCan;
    public static ItemData Molotov      => _molotov;

    private static readonly ItemData _bandage = new()
    {
        ItemId = "bandage", DisplayName = "Bandage", Description = "Stops bleeding, restores minor HP.",
        Category = ItemCategory.Medical, MaxStack = 5, WeightKg = 0.1f,
        IconColor = new Color(0.9f, 0.9f, 0.9f), HealAmount = 20f
    };
    private static readonly ItemData _medKit = new()
    {
        ItemId = "medkit", DisplayName = "First Aid Kit", Description = "Full trauma kit. Restores significant HP.",
        Category = ItemCategory.Medical, MaxStack = 2, WeightKg = 0.5f,
        IconColor = new Color(1f, 0.3f, 0.3f), HealAmount = 60f
    };
    private static readonly ItemData _foodCan = new()
    {
        ItemId = "food_can", DisplayName = "Canned Food", Description = "Sustains hunger for several hours.",
        Category = ItemCategory.Provision, MaxStack = 6, WeightKg = 0.4f,
        IconColor = new Color(0.8f, 0.7f, 0.4f), FoodValue = 35f
    };
    private static readonly ItemData _waterBottle = new()
    {
        ItemId = "water_bottle", DisplayName = "Water Bottle", Description = "Clean potable water.",
        Category = ItemCategory.Provision, MaxStack = 4, WeightKg = 0.6f,
        IconColor = new Color(0.4f, 0.7f, 1f), WaterValue = 40f
    };
    private static readonly ItemData _scrap = new()
    {
        ItemId = "scrap", DisplayName = "Scrap Metal", Description = "Bent metal fragments. Used in construction.",
        Category = ItemCategory.Crafting, MaxStack = 20, WeightKg = 0.3f,
        IconColor = new Color(0.55f, 0.55f, 0.6f)
    };
    private static readonly ItemData _cloth = new()
    {
        ItemId = "cloth", DisplayName = "Cloth Strip", Description = "Torn fabric. Can craft bandages.",
        Category = ItemCategory.Crafting, MaxStack = 20, WeightKg = 0.05f,
        IconColor = new Color(0.8f, 0.75f, 0.6f)
    };
    private static readonly ItemData _woodPlank = new()
    {
        ItemId = "wood_plank", DisplayName = "Wood Plank", Description = "Rough-cut board for construction.",
        Category = ItemCategory.Crafting, MaxStack = 10, WeightKg = 1.2f,
        IconColor = new Color(0.7f, 0.5f, 0.3f)
    };
    private static readonly ItemData _nails = new()
    {
        ItemId = "nails", DisplayName = "Box of Nails", Description = "A handful of nails.",
        Category = ItemCategory.Crafting, MaxStack = 20, WeightKg = 0.2f,
        IconColor = new Color(0.6f, 0.6f, 0.65f)
    };
    private static readonly ItemData _rope = new()
    {
        ItemId = "rope", DisplayName = "Rope (1m)", Description = "Short length of braided rope.",
        Category = ItemCategory.Crafting, MaxStack = 10, WeightKg = 0.3f,
        IconColor = new Color(0.7f, 0.6f, 0.4f)
    };
    private static readonly ItemData _ammo9mm = new()
    {
        ItemId = "ammo_9mm", DisplayName = "9mm Rounds (15)", Description = "Standard pistol ammunition.",
        Category = ItemCategory.Ammo, MaxStack = 8, WeightKg = 0.2f,
        IconColor = new Color(1f, 0.85f, 0.3f)
    };
    private static readonly ItemData _ammo12g = new()
    {
        ItemId = "ammo_12g", DisplayName = "12G Shells (6)", Description = "Shotgun shells.",
        Category = ItemCategory.Ammo, MaxStack = 6, WeightKg = 0.3f,
        IconColor = new Color(1f, 0.6f, 0.2f)
    };
    private static readonly ItemData _shovel = new()
    {
        ItemId = "shovel", DisplayName = "Shovel", Description = "Entrenching tool. Required to bury corpses.",
        Category = ItemCategory.Tool, MaxStack = 1, WeightKg = 1.8f,
        IconColor = new Color(0.5f, 0.45f, 0.35f)
    };
    private static readonly ItemData _fuelCan = new()
    {
        ItemId = "fuel_can", DisplayName = "Fuel Canister", Description = "Gasoline. Needed for burning corpses or generators.",
        Category = ItemCategory.Hardware, MaxStack = 4, WeightKg = 2.5f,
        IconColor = new Color(1f, 0.4f, 0.1f)
    };
    private static readonly ItemData _molotov = new()
    {
        ItemId = "molotov", DisplayName = "Molotov Cocktail", Description = "Improvised firebomb. Crafted from fuel and cloth.",
        Category = ItemCategory.Weapon, MaxStack = 4, WeightKg = 0.8f,
        IconColor = new Color(1f, 0.55f, 0.15f)
    };
}
