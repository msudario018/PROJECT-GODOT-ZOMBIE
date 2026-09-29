using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Core.Components;

namespace ZombieApocalypse.Systems.Loot;

/// <summary>
/// A single weighted entry in a loot table.
/// </summary>
public class LootEntry
{
    public ItemData Item;
    public int MinQty;
    public int MaxQty;
    public float Weight;   // Relative probability weight

    public LootEntry(ItemData item, int minQty, int maxQty, float weight)
    {
        Item = item; MinQty = minQty; MaxQty = maxQty; Weight = weight;
    }
}

/// <summary>
/// Searchable loot container.
///
/// Attach to any StaticBody3D that is in the "interactables" group.
/// On first search (player presses [E] within range), rolls the loot table
/// and transfers items into the player's InventoryComponent.
///
/// Visual feedback:
///   - Mesh tints green/yellow/red based on searched state.
///   - Label above container shows search prompt when player is close.
///
/// Built-in loot table archetypes (set ContainerType in the inspector):
///   MedicalCabinet, AmmoCache, FoodLocker, ToolBox, GeneralJunk
/// </summary>
public partial class LootContainer : StaticBody3D
{
    public enum ContainerArchetype
    {
        MedicalCabinet,
        AmmoCache,
        FoodLocker,
        ToolBox,
        GeneralJunk
    }

    // ── Configuration ────────────────────────────────────────────────
    [Export] public ContainerArchetype ContainerType = ContainerArchetype.GeneralJunk;
    [Export] public string ContainerLabel = "Supply Crate";
    [Export] public int MinRolls = 1;
    [Export] public int MaxRolls = 3;
    [Export] public float SearchTime = 1.5f;       // seconds to search
    [Export] public float InteractionRange = 2.5f;

    // ── State ────────────────────────────────────────────────────────
    public bool IsSearched   { get; private set; }
    public bool IsSearching  { get; private set; }

    /// <summary>
    /// True for caches spawned by <see cref="World.WorldEvents.AirdropEvent"/>.
    /// Airdrop caches are reserved for the player: survivor scavengers and
    /// bandit looters skip them.
    /// </summary>
    [Export] public bool IsAirdropCache = false;
    private float _searchTimer;
    private InventoryComponent? _targetInventory;
    private MeshInstance3D? _mesh;
    private Label3D?        _label;

    /// <summary>
    /// Per-instance loot table. Set by <see cref="SetAirdropLoot"/> so airdrop
    /// caches grant an exact manifest without mutating the shared static tables.
    /// </summary>
    private List<LootEntry>? _overrideTable;

    // ── Loot table presets ───────────────────────────────────────────
    private static readonly Dictionary<ContainerArchetype, List<LootEntry>> _tables = new()
    {
        [ContainerArchetype.MedicalCabinet] = new List<LootEntry>
        {
            new(ItemData.Bandage,     2, 4,  6.0f),
            new(ItemData.MedKit,      1, 2,  2.0f),
            new(ItemData.Cloth,       2, 5,  4.0f),
        },
        [ContainerArchetype.AmmoCache] = new List<LootEntry>
        {
            new(ItemData.Ammo9mm,     1, 3,  5.0f),
            new(ItemData.Ammo12g,     1, 2,  3.5f),
            new(ItemData.Scrap,       1, 4,  2.0f),
        },
        [ContainerArchetype.FoodLocker] = new List<LootEntry>
        {
            new(ItemData.FoodCan,     1, 4,  6.0f),
            new(ItemData.WaterBottle, 1, 3,  5.0f),
            new(ItemData.Cloth,       1, 2,  2.0f),
        },
        [ContainerArchetype.ToolBox] = new List<LootEntry>
        {
            new(ItemData.Scrap,       2, 6,  5.0f),
            new(ItemData.Nails,       2, 5,  4.0f),
            new(ItemData.WoodPlank,   1, 3,  3.0f),
            new(ItemData.Shovel,      1, 1,  0.5f),
            new(ItemData.FuelCan,     1, 1,  1.0f),
        },
        [ContainerArchetype.GeneralJunk] = new List<LootEntry>
        {
            new(ItemData.Cloth,       1, 3,  4.0f),
            new(ItemData.Scrap,       1, 4,  4.0f),
            new(ItemData.Nails,       1, 3,  3.0f),
            new(ItemData.Bandage,     1, 2,  2.0f),
            new(ItemData.FoodCan,     1, 2,  2.0f),
            new(ItemData.RopeLen,     1, 2,  1.5f),
        }
    };

    public override void _Ready()
    {
        AddToGroup("interactables");
        AddToGroup("loot_containers");

        _mesh  = GetNodeOrNull<MeshInstance3D>("Mesh");
        if (_mesh == null)
        {
            _mesh = new MeshInstance3D();
            var box = new BoxMesh { Size = new Vector3(1.2f, 0.8f, 0.9f) };
            _mesh.Mesh = box;
            _mesh.Position = new Vector3(0, 0.4f, 0);

            Color crateColor = ContainerType switch
            {
                ContainerArchetype.MedicalCabinet => new Color(0.9f, 0.85f, 0.8f),
                ContainerArchetype.AmmoCache => new Color(0.25f, 0.35f, 0.22f),
                ContainerArchetype.FoodLocker => new Color(0.3f, 0.4f, 0.55f),
                ContainerArchetype.ToolBox => new Color(0.6f, 0.35f, 0.15f),
                _ => new Color(0.5f, 0.45f, 0.38f)
            };

            var mat = new StandardMaterial3D { AlbedoColor = crateColor, Roughness = 0.7f };
            _mesh.MaterialOverride = mat;
            AddChild(_mesh);
        }

        var col = GetNodeOrNull<CollisionShape3D>("Collision");
        if (col == null)
        {
            col = new CollisionShape3D();
            var boxShape = new BoxShape3D { Size = new Vector3(1.2f, 0.8f, 0.9f) };
            col.Shape = boxShape;
            col.Position = new Vector3(0, 0.4f, 0);
            AddChild(col);
        }

        _label = GetNodeOrNull<Label3D>("Label");
        if (_label == null)
        {
            _label = new Label3D();
            _label.FontSize = 22;
            AddChild(_label);
        }

        _label.Text = $"[E] Search {ContainerLabel}";
        _label.Position = new Vector3(0, 1.4f, 0);
        _label.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        _label.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (IsSearched || !IsSearching) return;

        _searchTimer += (float)delta;
        if (_searchTimer >= SearchTime)
        {
            CompleteSearch();
        }
    }

    // ── Called by PlayerInteraction ──────────────────────────────────

    /// <summary>Begin or complete searching this container.</summary>
    public void TrySearch(InventoryComponent playerInventory)
    {
        if (IsSearched) return;
        if (IsSearching) return;

        _targetInventory = playerInventory;
        IsSearching = true;
        _searchTimer = 0f;
        GD.Print($"[LootContainer] Searching {ContainerLabel}... ({SearchTime:F1}s)");
    }

    public void ShowPrompt(bool visible)
    {
        if (_label != null)
            _label.Visible = visible && !IsSearched;
    }

    /// <summary>
    /// Force-complete a search instantly into any inventory. Used by survivor
    /// scavengers, bandit looters and the headless self-test. Returns true if
    /// items were actually transferred.
    /// </summary>
    public bool ForceSearch(InventoryComponent inventory)
    {
        if (IsSearched) return false;
        _targetInventory = inventory;
        IsSearching = false;
        _searchTimer = 0f;
        int before = CountItems(inventory);
        CompleteSearch();
        return CountItems(inventory) > before;
    }

    /// <summary>
    /// Replace the rolled table with a fixed airdrop manifest and mark the
    /// container as an airdrop cache. The manifest is per-instance — the shared
    /// archetype tables stay untouched.
    /// </summary>
    public void SetAirdropLoot(int medKits, int ammoPacks, int foodCans, int cloth)
    {
        IsAirdropCache = true;
        _overrideTable = new List<LootEntry>
        {
            new(ItemData.MedKit,      medKits,   medKits,   1.0f),
            new(ItemData.Ammo9mm,     ammoPacks, ammoPacks, 1.0f),
            new(ItemData.FoodCan,     foodCans,  foodCans,  1.0f),
            new(ItemData.Cloth,       cloth,     cloth,     1.0f),
        };
    }

    private static int CountItems(InventoryComponent inventory)
    {
        int total = 0;
        foreach (var slot in inventory.Slots)
            total += slot.Quantity;
        return total;
    }

    // ── Internal ─────────────────────────────────────────────────────

    private void CompleteSearch()
    {
        IsSearching = false;
        IsSearched  = true;

        if (_overrideTable != null)
        {
            // Fixed manifest (airdrop): grant every entry at its exact quantity.
            foreach (var entry in _overrideTable)
            {
                int added = _targetInventory?.TryAdd(entry.Item, entry.MinQty) ?? 0;
                if (added > 0)
                    GD.Print($"[LootContainer] +{added}× {entry.Item.DisplayName}");
            }
        }
        else
        {
            var table = _tables.GetValueOrDefault(ContainerType) ?? _tables[ContainerArchetype.GeneralJunk];
            int rolls = GD.RandRange(MinRolls, MaxRolls);

            for (int r = 0; r < rolls; r++)
            {
                var entry = PickWeighted(table);
                if (entry == null) continue;
                int qty = GD.RandRange(entry.MinQty, entry.MaxQty);
                int added = _targetInventory?.TryAdd(entry.Item, qty) ?? 0;
                if (added > 0)
                    GD.Print($"[LootContainer] +{added}× {entry.Item.DisplayName}");
            }
        }

        // Visual: tint mesh grey to show "searched"
        if (_mesh?.GetActiveMaterial(0) is StandardMaterial3D mat)
            mat.AlbedoColor = new Color(0.35f, 0.35f, 0.35f);

        if (_label != null)
        {
            _label.Text = $"{ContainerLabel} (Empty)";
            _label.Modulate = new Color(0.5f, 0.5f, 0.5f);
        }

        GD.Print($"[LootContainer] {ContainerLabel} searched and empty.");
    }

    private static LootEntry? PickWeighted(List<LootEntry> table)
    {
        float totalWeight = 0f;
        foreach (var e in table) totalWeight += e.Weight;
        float roll = (float)GD.RandRange(0.0, totalWeight);
        float cumulative = 0f;
        foreach (var e in table)
        {
            cumulative += e.Weight;
            if (roll <= cumulative) return e;
        }
        return table.Count > 0 ? table[^1] : null;
    }
}
