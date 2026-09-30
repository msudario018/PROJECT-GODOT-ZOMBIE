namespace ZombieApocalypse.UI.HUD;

using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Systems.Crafting;

/// <summary>
/// Inventory &amp; Crafting window ([Tab] or [I]).
///
/// Replaces the wall of one-key hotkeys: the backpack is a proper grid and each
/// recipe lists its ingredients with a Craft button, so the main HUD can stay
/// down to vitals, weapon, ammo, time and power.
///
/// The window pauses the game while open (this is a survival game, not a
/// twin-stick) and refreshes live from the player's
/// <see cref="InventoryComponent"/> and <see cref="CraftingSystem"/>.
/// </summary>
public partial class TestArenaHUD
{
    private Control? _inventoryWindow;
    private GridContainer? _inventoryGrid;
    private VBoxContainer? _recipeList;
    private Label? _inventoryWeightLabel;

    private readonly List<SlotWidget> _slotWidgets = new();
    private InventoryComponent? _windowInventory;
    private CraftingSystem? _windowCrafting;

    /// <summary>True while the inventory window is open.</summary>
    internal bool InventoryVisible => _inventoryWindow != null && _inventoryWindow.Visible;

    /// <summary>One backpack slot: colour swatch, item name, quantity.</summary>
    private class SlotWidget
    {
        public ColorRect Swatch = null!;
        public Label Name = null!;
        public Label Quantity = null!;
    }

    /// <summary>Build the window (idempotent).</summary>
    internal void BuildInventoryWindow()
    {
        if (_inventoryWindow != null) return;

        _windowInventory = _playerInventory;
        _windowCrafting = _craftingSystem;

        _inventoryWindow = new PanelContainer
        {
            Name = "InventoryWindow",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _inventoryWindow.SetAnchorsPreset(Control.LayoutPreset.Center);
        _inventoryWindow.CustomMinimumSize = new Vector2(760, 460);
        _inventoryWindow.Position = new Vector2(-380, -240);

        _inventoryWindow.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.07f, 0.09f, 0.96f),
            BorderColor = new Color(0.30f, 0.45f, 0.55f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            ContentMarginLeft = 16,
            ContentMarginRight = 16,
            ContentMarginTop = 12,
            ContentMarginBottom = 12,
        });

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 10);
        _inventoryWindow.AddChild(root);

        var header = new Label { Text = "BACKPACK & CRAFTING" };
        header.AddThemeFontSizeOverride("font_size", 22);
        header.AddThemeColorOverride("font_color", new Color(0.6f, 0.85f, 1f));
        root.AddChild(header);

        var columns = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 18);
        root.AddChild(columns);

        // ── Left: the backpack grid ───────────────────────────────
        var left = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation", 6);
        columns.AddChild(left);
        left.AddChild(SectionHeading("Backpack"));

        _inventoryWeightLabel = new Label { Text = "0.0 / 30.0 kg" };
        left.AddChild(_inventoryWeightLabel);

        _inventoryGrid = new GridContainer { Columns = 6 };
        _inventoryGrid.AddThemeConstantOverride("h_separation", 6);
        _inventoryGrid.AddThemeConstantOverride("v_separation", 6);
        left.AddChild(_inventoryGrid);

        int rows = Mathf.CeilToInt((_windowInventory?.SlotCount ?? 24) / 6f);
        for (int i = 0; i < rows * 6; i++)
            _slotWidgets.Add(CreateSlotWidget());

        // ── Right: crafting ───────────────────────────────────────
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 6);
        columns.AddChild(right);
        right.AddChild(SectionHeading("Crafting"));

        _recipeList = new VBoxContainer();
        _recipeList.AddThemeConstantOverride("separation", 4);
        right.AddChild(_recipeList);

        var footer = new Label { Text = "[Tab] or [I] to close  ·  [Esc] pause menu" };
        footer.AddThemeFontSizeOverride("font_size", 11);
        footer.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        root.AddChild(footer);

        AddChild(_inventoryWindow);
        RefreshInventoryWindow();
    }

    private static Label SectionHeading(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.5f));
        return label;
    }

    private SlotWidget CreateSlotWidget()
    {
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(104, 74),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.11f, 0.12f, 0.14f),
            BorderColor = new Color(0.22f, 0.24f, 0.28f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
        });

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 1);
        panel.AddChild(box);

        // Icon stand-in: the item's own colour, so slots read at a glance
        // without shipping sprite sheets.
        var swatch = new ColorRect { CustomMinimumSize = new Vector2(96, 26) };
        box.AddChild(swatch);

        var name = new Label { Text = "", TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        name.AddThemeFontSizeOverride("font_size", 11);
        box.AddChild(name);

        var quantity = new Label { Text = "" };
        quantity.AddThemeFontSizeOverride("font_size", 11);
        quantity.AddThemeColorOverride("font_color", new Color(0.8f, 0.85f, 0.9f));
        box.AddChild(quantity);

        _inventoryGrid!.AddChild(panel);
        return new SlotWidget { Swatch = swatch, Name = name, Quantity = quantity };
    }

    /// <summary>Open or close the window, pausing the game while it is up.</summary>
    internal void ToggleInventoryWindow()
    {
        if (_inventoryWindow == null) return;

        bool open = !_inventoryWindow.Visible;
        _inventoryWindow.Visible = open;

        if (open)
        {
            RefreshInventoryWindow();
            GetTree().Paused = true;
            _statusLabel.Text = "Inventory open — press [Tab] to close.";
        }
        else
        {
            GetTree().Paused = false;
        }
    }

    /// <summary>Re-read the player's inventory and recipe availability.</summary>
    internal void RefreshInventoryWindow()
    {
        if (_inventoryWindow == null || !_inventoryWindow.Visible) return;

        var inventory = _windowInventory;
        if (inventory != null)
        {
            var slots = inventory.Slots;
            for (int i = 0; i < _slotWidgets.Count; i++)
            {
                var widget = _slotWidgets[i];
                var slot = i < slots.Count ? slots[i] : null;

                if (slot == null || slot.IsEmpty)
                {
                    widget.Swatch.Color = new Color(0.08f, 0.08f, 0.09f);
                    widget.Name.Text = "";
                    widget.Quantity.Text = "";
                    continue;
                }

                widget.Swatch.Color = slot.Item!.IconColor;
                widget.Name.Text = slot.Item.DisplayName;
                widget.Quantity.Text = $"×{slot.Quantity}";
            }

            if (_inventoryWeightLabel != null)
            {
                _inventoryWeightLabel.Text =
                    $"{inventory.CurrentWeightKg:F1} / {inventory.MaxWeightKg:F1} kg" +
                    (inventory.IsEncumbered ? "  (OVERENCUMBERED)" : "");
                _inventoryWeightLabel.AddThemeColorOverride("font_color",
                    inventory.IsEncumbered ? new Color(0.95f, 0.45f, 0.4f) : new Color(0.7f, 0.75f, 0.8f));
            }
        }

        RefreshRecipes();
    }

    /// <summary>Rebuild the recipe rows with live ingredient availability.</summary>
    private void RefreshRecipes()
    {
        if (_recipeList == null) return;

        foreach (var child in _recipeList.GetChildren())
            child.QueueFree();

        foreach (var recipe in CraftingSystem.Recipes)
        {
            bool canCraft = _windowCrafting != null && _windowCrafting.CanCraft(recipe.RecipeName);
            _recipeList.AddChild(BuildRecipeRow(recipe, canCraft));
        }
    }

    private Control BuildRecipeRow(CraftingRecipe recipe, bool canCraft)
    {
        var row = new PanelContainer { CustomMinimumSize = new Vector2(320, 0) };
        row.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = canCraft ? new Color(0.13f, 0.20f, 0.15f) : new Color(0.10f, 0.10f, 0.12f),
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 4,
            ContentMarginBottom = 4,
        });

        var box = new HBoxContainer();
        row.AddChild(box);

        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 0);
        box.AddChild(text);

        var title = new Label { Text = $"{recipe.RecipeName}  →  {recipe.OutputQty}× {recipe.Output.DisplayName}" };
        title.AddThemeColorOverride("font_color",
            canCraft ? new Color(0.75f, 1f, 0.8f) : new Color(0.6f, 0.6f, 0.6f));
        text.AddChild(title);

        // Ingredient line with have/need counts.
        var parts = new List<string>();
        foreach (var (itemId, needed) in recipe.Ingredients)
        {
            int have = _windowInventory?.CountOf(itemId) ?? 0;
            var item = ItemData.Find(itemId);
            parts.Add($"{item?.DisplayName ?? itemId} {have}/{needed}");
        }

        var ingredients = new Label { Text = string.Join("  ·  ", parts) };
        ingredients.AddThemeFontSizeOverride("font_size", 11);
        ingredients.AddThemeColorOverride("font_color", new Color(0.65f, 0.7f, 0.75f));
        text.AddChild(ingredients);

        string recipeName = recipe.RecipeName;
        var button = new Button
        {
            Text = "Craft",
            Disabled = !canCraft,
            CustomMinimumSize = new Vector2(80, 0),
        };
        button.Pressed += () =>
        {
            if (_windowCrafting?.TryCraft(recipeName) == true)
            {
                _statusLabel.Text = $"Crafted {recipeName}.";
                RefreshInventoryWindow();
            }
        };
        box.AddChild(button);

        return row;
    }
}
