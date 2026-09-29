using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Core.Components;

namespace ZombieApocalypse.Systems.Crafting;

/// <summary>
/// One crafting recipe: a set of ingredient costs → an output item + quantity.
/// </summary>
public class CraftingRecipe
{
    public string RecipeName;
    public ItemData Output;
    public int OutputQty;
    public Dictionary<string, int> Ingredients;   // itemId → required quantity

    public CraftingRecipe(string name, ItemData output, int qty, Dictionary<string, int> ingredients)
    {
        RecipeName = name;
        Output     = output;
        OutputQty  = qty;
        Ingredients = ingredients;
    }
}

/// <summary>
/// Central crafting system. Validates ingredient availability in the player's
/// inventory and produces output items on success.
///
/// Five Phase-5 starter recipes:
///   1. Bandage         → 2× Cloth Strip
///   2. Rope (1m)       → 3× Cloth Strip
///   3. Wood Fence      → 4× Wood Plank + 2× Nails
///   4. Improvised Kit  → 3× Cloth + 1× Rope (→ Full Medical Kit)
///   5. Molotov Cocktail   → 1× Fuel Can + 1× Cloth (improvised incendiary weapon)
///
/// Attach as a child of the Player (or as an autoload if shared between survivors later).
/// </summary>
public partial class CraftingSystem : Node
{
    // ── Recipe Registry ──────────────────────────────────────────────
    public static readonly List<CraftingRecipe> Recipes = new()
    {
        new CraftingRecipe(
            "Bandage",
            ItemData.Bandage, 2,
            new() { { "cloth", 2 } }
        ),
        new CraftingRecipe(
            "Rope (1m)",
            ItemData.RopeLen, 1,
            new() { { "cloth", 3 } }
        ),
        new CraftingRecipe(
            "Improvised First Aid Kit",
            ItemData.MedKit, 1,
            new() { { "cloth", 3 }, { "rope", 1 } }
        ),
        new CraftingRecipe(
            "Reinforce Ammo Box (9mm)",
            ItemData.Ammo9mm, 2,
            new() { { "scrap", 4 } }
        ),
        new CraftingRecipe(
            "Molotov Cocktail",
            ItemData.Molotov, 1,
            new() { { "fuel_can", 1 }, { "cloth", 1 } }
        ),
    };

    // ── Signals ──────────────────────────────────────────────────────
    [Signal] public delegate void CraftSucceededEventHandler(string recipeName, string outputItemId, int qty);
    [Signal] public delegate void CraftFailedEventHandler(string recipeName, string reason);

    // ── References ───────────────────────────────────────────────────
    private InventoryComponent? _inventory;

    public override void _Ready()
    {
        _inventory = GetParent()?.GetNodeOrNull<InventoryComponent>("InventoryComponent");
        if (_inventory == null)
            GD.PrintErr("[CraftingSystem] No InventoryComponent found on parent.");
    }

    // ── Public API ───────────────────────────────────────────────────

    /// <summary>
    /// Attempt to craft a recipe by name.
    /// Returns true if crafting succeeded, false otherwise.
    /// </summary>
    public bool TryCraft(string recipeName)
    {
        if (_inventory == null)
        {
            EmitSignal(SignalName.CraftFailed, recipeName, "No inventory component.");
            return false;
        }

        var recipe = Recipes.Find(r => r.RecipeName == recipeName);
        if (recipe == null)
        {
            EmitSignal(SignalName.CraftFailed, recipeName, "Recipe not found.");
            return false;
        }

        // Check ingredients
        foreach (var (itemId, qty) in recipe.Ingredients)
        {
            if (!_inventory.Has(itemId, qty))
            {
                EmitSignal(SignalName.CraftFailed, recipeName,
                    $"Missing: {qty}× {itemId}");
                GD.Print($"[CraftingSystem] Cannot craft '{recipeName}': need {qty}× {itemId} (have {_inventory.CountOf(itemId)})");
                return false;
            }
        }

        // Consume ingredients
        foreach (var (itemId, qty) in recipe.Ingredients)
            _inventory.TryRemove(itemId, qty);

        // Add output
        _inventory.TryAdd(recipe.Output, recipe.OutputQty);

        EmitSignal(SignalName.CraftSucceeded, recipe.RecipeName, recipe.Output.ItemId, recipe.OutputQty);
        GD.Print($"[CraftingSystem] Crafted {recipe.OutputQty}× {recipe.Output.DisplayName}");
        return true;
    }

    /// <summary>Returns true if the player has all ingredients for a given recipe.</summary>
    public bool CanCraft(string recipeName)
    {
        if (_inventory == null) return false;
        var recipe = Recipes.Find(r => r.RecipeName == recipeName);
        if (recipe == null) return false;
        foreach (var (itemId, qty) in recipe.Ingredients)
            if (!_inventory.Has(itemId, qty)) return false;
        return true;
    }

    /// <summary>List all recipes the player can currently craft.</summary>
    public List<CraftingRecipe> GetAvailableRecipes()
    {
        var available = new List<CraftingRecipe>();
        foreach (var recipe in Recipes)
            if (CanCraft(recipe.RecipeName))
                available.Add(recipe);
        return available;
    }
}
