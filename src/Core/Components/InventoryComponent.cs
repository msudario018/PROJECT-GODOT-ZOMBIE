using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Entities.Player;

namespace ZombieApocalypse.Core.Components;

/// <summary>
/// Slot-based inventory component with weight/stack limits and item consumption.
///
/// Each slot holds one ItemData type and a quantity (up to ItemData.MaxStack).
/// Weight is tracked in real-time; exceeding MaxWeightKg reduces sprint speed.
///
/// Signals:
///   ItemAdded(itemId, quantity)
///   ItemRemoved(itemId, quantity)
///   InventoryChanged()
///   ItemUsed(itemId)
/// </summary>
public partial class InventoryComponent : Node
{
    // ── Configuration ────────────────────────────────────────────────
    [Export] public int SlotCount = 24;
    [Export] public float MaxWeightKg = 30.0f;
    [Export] public bool GrantStartingItems = true;

    // ── Signals ──────────────────────────────────────────────────────
    [Signal] public delegate void ItemAddedEventHandler(string itemId, int quantity);
    [Signal] public delegate void ItemRemovedEventHandler(string itemId, int quantity);
    [Signal] public delegate void InventoryChangedEventHandler();
    [Signal] public delegate void ItemUsedEventHandler(string itemId);

    // ── Internal ─────────────────────────────────────────────────────
    private ItemStack[] _slots = null!;
    public float CurrentWeightKg { get; private set; }

    /// <summary>True if carrying weight exceeds the limit (sprint penalised).</summary>
    public bool IsEncumbered => CurrentWeightKg > MaxWeightKg;

    public override void _Ready()
    {
        EnsureInitialized();

        if (GrantStartingItems && CurrentWeightKg <= 0.001f)
        {
            TryAdd(ItemData.Bandage, 2);
            TryAdd(ItemData.FoodCan, 2);
            TryAdd(ItemData.WaterBottle, 2);
            TryAdd(ItemData.Cloth, 3);
            TryAdd(ItemData.WoodPlank, 4);
            TryAdd(ItemData.Nails, 4);
            TryAdd(ItemData.Shovel, 1);
        }
    }

    private void EnsureInitialized()
    {
        if (_slots == null)
        {
            _slots = new ItemStack[SlotCount];
            for (int i = 0; i < SlotCount; i++)
                _slots[i] = new ItemStack();
        }
    }

    // ── Public API ───────────────────────────────────────────────────

    /// <summary>Attempt to add `quantity` of an item. Returns amount actually added.</summary>
    public int TryAdd(ItemData item, int quantity = 1)
    {
        EnsureInitialized();
        int remaining = quantity;

        // Fill existing stacks first
        for (int i = 0; i < SlotCount && remaining > 0; i++)
        {
            if (_slots[i].Item?.ItemId == item.ItemId && _slots[i].Quantity < item.MaxStack)
            {
                int canFit = item.MaxStack - _slots[i].Quantity;
                int adding = Mathf.Min(canFit, remaining);
                _slots[i].Quantity += adding;
                remaining -= adding;
                CurrentWeightKg += item.WeightKg * adding;
            }
        }

        // Fill empty slots
        for (int i = 0; i < SlotCount && remaining > 0; i++)
        {
            if (_slots[i].IsEmpty)
            {
                int adding = Mathf.Min(item.MaxStack, remaining);
                _slots[i].Set(item, adding);
                remaining -= adding;
                CurrentWeightKg += item.WeightKg * adding;
            }
        }

        int added = quantity - remaining;
        if (added > 0)
        {
            EmitSignal(SignalName.ItemAdded, item.ItemId, added);
            EmitSignal(SignalName.InventoryChanged);
        }
        return added;
    }

    /// <summary>Remove up to `quantity` of an item. Returns amount actually removed.</summary>
    public int TryRemove(string itemId, int quantity = 1)
    {
        EnsureInitialized();
        int remaining = quantity;
        for (int i = SlotCount - 1; i >= 0 && remaining > 0; i--)
        {
            if (_slots[i].Item?.ItemId == itemId)
            {
                int removing = Mathf.Min(_slots[i].Quantity, remaining);
                _slots[i].Quantity -= removing;
                CurrentWeightKg -= _slots[i].Item!.WeightKg * removing;
                if (_slots[i].Quantity <= 0)
                    _slots[i].Clear();
                remaining -= removing;
            }
        }

        int removed = quantity - remaining;
        if (removed > 0)
        {
            EmitSignal(SignalName.ItemRemoved, itemId, removed);
            EmitSignal(SignalName.InventoryChanged);
        }
        return removed;
    }

    /// <summary>
    /// Consume an item by its ID, applying its food, water, healing, or stamina effects.
    /// </summary>
    public bool TryUseItem(string itemId, PlayerStats? stats = null, HealthComponent? health = null)
    {
        EnsureInitialized();
        for (int i = 0; i < SlotCount; i++)
        {
            if (_slots[i].Item?.ItemId == itemId && _slots[i].Quantity > 0)
            {
                var item = _slots[i].Item!;

                // Apply effects
                if (health != null && item.HealAmount > 0f)
                    health.Heal(item.HealAmount);

                if (stats != null)
                {
                    if (item.FoodValue > 0f) stats.Eat(item.FoodValue);
                    if (item.WaterValue > 0f) stats.Drink(item.WaterValue);
                    if (item.StaminaBonus > 0f) stats.RestoreStamina(item.StaminaBonus);
                }

                // Remove 1 unit
                _slots[i].Quantity--;
                CurrentWeightKg -= item.WeightKg;
                if (_slots[i].Quantity <= 0)
                    _slots[i].Clear();

                EmitSignal(SignalName.ItemUsed, itemId);
                EmitSignal(SignalName.InventoryChanged);
                GD.Print($"[Inventory] Used 1× {item.DisplayName}");
                return true;
            }
        }
        return false;
    }

    /// <summary>Total quantity of a given item across all slots.</summary>
    public int CountOf(string itemId)
    {
        EnsureInitialized();
        int total = 0;
        for (int i = 0; i < SlotCount; i++)
            if (_slots[i].Item?.ItemId == itemId)
                total += _slots[i].Quantity;
        return total;
    }

    public bool Has(string itemId, int quantity = 1) => CountOf(itemId) >= quantity;

    /// <summary>Read-only snapshot of all slots for UI rendering.</summary>
    public IReadOnlyList<ItemStack> Slots
    {
        get
        {
            EnsureInitialized();
            return _slots;
        }
    }

    // ── Inner class ──────────────────────────────────────────────────

    public class ItemStack
    {
        public ItemData? Item     { get; private set; }
        public int       Quantity { get; set; }
        public bool IsEmpty => Item == null || Quantity <= 0;

        public void Set(ItemData item, int qty) { Item = item; Quantity = qty; }
        public void Clear() { Item = null; Quantity = 0; }
    }
}
