using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Core.Vision;
using ZombieApocalypse.Entities.NPC;
using ZombieApocalypse.Entities.Player;
using ZombieApocalypse.Entities.Survivors;
using ZombieApocalypse.Systems.Building;
using ZombieApocalypse.Systems.Loot;
using ZombieApocalypse.World.Defenses;
using ZombieApocalypse.World.Environment;
using ZombieApocalypse.World.Power;
using ZombieApocalypse.World.Power.Distribution;
using ZombieApocalypse.World.Power.Generation;
using ZombieApocalypse.World.Power.Storage;
using ZombieApocalypse.World.WorldEvents;

namespace ZombieApocalypse.Core.Persistence;

/// <summary>
/// Pushes a <see cref="SaveData"/> snapshot back into the live scene.
///
/// Restores are defensive by design: a node that no longer exists in the scene
/// is skipped and logged instead of aborting the load, so old saves keep working
/// as the scene evolves.
/// </summary>
public static class SaveApply
{
    private static SceneTree? Tree => Engine.GetMainLoop() as SceneTree;

    /// <summary>Resolve (itemId, quantity) pairs into ItemData stacks.</summary>
    public static List<(ItemData item, int quantity)> ResolveSlots(IEnumerable<SlotData> slots)
    {
        var stacks = new List<(ItemData, int)>();
        foreach (var slot in slots)
        {
            if (slot == null || slot.Quantity <= 0) continue;
            if (ItemData.Find(slot.ItemId) is not { } item) continue;
            stacks.Add((item, slot.Quantity));
        }
        return stacks;
    }

    // ── Clock ────────────────────────────────────────────────────────

    public static bool ApplyClock(ClockData data)
    {
        if (TimeManager.Instance == null) return false;
        TimeManager.Instance.RestoreState(data.DayCount, data.Hour, data.IsPaused);
        return true;
    }

    // ── Player ───────────────────────────────────────────────────────

    public static bool ApplyPlayer(PlayerData data)
    {
        var player = Tree?.GetFirstNodeInGroup("player") as Node3D;
        if (player == null) return false;

        player.GlobalPosition = data.Position;
        player.RotationDegrees = new Vector3(0f, data.YawDegrees, 0f);

        var health = SaveCapture.FindChild<HealthComponent>(player);
        if (health != null)
        {
            health.SetMaxHealth(data.MaxHealth);
            health.SetHealth(data.Health);
        }

        SaveCapture.FindChild<PlayerStats>(player)?.Restore(data.Hunger, data.Thirst, data.Stamina);

        if (SaveCapture.FindChild<FieldOfView>(player) is { } vision)
            vision.IsFlashlightActive = data.FlashlightOn;

        SaveCapture.FindChild<InventoryComponent>(player)?.RestoreContents(ResolveSlots(data.Slots));
        return true;
    }

    // ── Power grid ───────────────────────────────────────────────────

    public static bool ApplyPower(PowerData data)
    {
        if (Tree == null) return false;
        bool applied = false;

        foreach (var node in Tree.GetNodesInGroup("power_nodes"))
        {
            if (node is CombustionGenerator generator)
            {
                generator.Running = data.GeneratorRunning;
                generator.FuelCanisters = Mathf.Clamp(data.GeneratorFuel, 0f, generator.MaxCanisters);
                applied = true;
            }
            else if (node is BatteryBank battery)
            {
                battery.SetChargeWattHours(Mathf.Min(data.BatteryChargeWattHours, battery.CapacityWattHours));
                applied = true;
            }
        }

        // Cables are re-connected first, then the saved sever set is re-applied,
        // so a save taken mid-repair comes back exactly as it was.
        foreach (var node in Tree.GetNodesInGroup("power_wires"))
            if (node is WiringSegment cable && cable.IsSevered)
                cable.Repair();

        foreach (var name in data.SeveredCables)
        {
            var cable = FindByName<WiringSegment>(Tree.GetNodesInGroup("power_wires"), name);
            if (cable == null)
            {
                GD.PushWarning($"[SaveApply] Saved cable '{name}' is missing from the scene; skipped.");
                continue;
            }
            cable.Sever();
        }

        PowerGrid.Instance?.RequestRescan();
        return applied;
    }

    private static T? FindByName<T>(Godot.Collections.Array<Node> nodes, string name) where T : Node
    {
        foreach (var node in nodes)
            if (node is T typed && node.Name.ToString() == name)
                return typed;
        return null;
    }

    // ── Camp ─────────────────────────────────────────────────────────

    public static bool ApplyCamp(CampData data)
    {
        MoraleSystem.Instance?.SetMorale(data.Morale);

        // Rebuild the stockpile manifest from the saved stacks.
        var stockpile = SaveCapture.FindStockpile();
        if (stockpile != null)
        {
            var entries = new List<LootEntry>();
            foreach (var (item, quantity) in ResolveSlots(data.Stockpile))
                entries.Add(new LootEntry(item, quantity, quantity, 1f));

            if (entries.Count > 0)
                stockpile.SetFixedManifest(entries, restrictedForNpcs: true);
        }

        if (Tree?.GetFirstNodeInGroup("airdrop_events") is AirdropEvent airdrop)
            airdrop.RestoreState(data.AirdropDropped, data.AirdropPosition);

        // Weather/season follow the restored day counter, so a save made on day 9
        // comes back in the right season rather than a random one.
        if (TimeManager.Instance is { } time)
            WeatherSystem.Instance?.EvaluateForDay(time.DayCount, announce: false);

        ApplySurvivors(data.Survivors);
        return true;
    }

    public static int ApplySurvivors(List<SurvivorData> survivors)
    {
        if (Tree == null) return 0;
        int applied = 0;

        foreach (var node in Tree.GetNodesInGroup("survivors"))
        {
            if (node is not SurvivorBase survivor) continue;

            // Survivors are matched by name first, then by group order.
            var match = survivors.Find(s => s.Name == survivor.SurvivorName);
            if (match == null && applied < survivors.Count)
                match = survivors[applied];

            if (match == null) continue;

            survivor.GlobalPosition = match.Position;
            survivor.TaskMode = (SurvivorTask)match.TaskMode;
            survivor.Health?.SetHealth(match.Health);
            survivor.Inventory?.RestoreContents(ResolveSlots(match.Slots));

            if (!match.Alive && survivor.IsAlive)
                survivor.Health?.Kill();

            applied++;
        }

        return applied;
    }

    // ── World state ──────────────────────────────────────────────────

    public static int ApplyWorld(WorldData data)
    {
        if (Tree == null) return 0;
        int applied = 0;

        // 1. Containers: mark the searched ones empty (missing ones are skipped).
        foreach (var entry in data.Containers)
        {
            var container = Tree.Root.GetNodeOrNull<LootContainer>(entry.Path);
            if (container == null)
                continue;
            container.SetSearchedState(entry.Searched);
            applied++;
        }

        // 2. Corpses: clear the world, then re-create the saved ones.
        foreach (var node in Tree.GetNodesInGroup("corpses"))
            if (GodotObject.IsInstanceValid(node))
                node.QueueFree();

        CorpseManager.Instance?.ClearCorpses();
        if (CorpseManager.Instance is { } manager)
        {
            foreach (var entry in data.Corpses)
                manager.SpawnCorpse(entry.Position)?.RestoreState(
                    entry.Stage, entry.AgeSeconds, entry.Searched, entry.Buried);
        }

        // 3. Structures: wipe runtime builds and re-inst them from the save.
        SaveCapture.FindChild<BuildingSystem>(Tree.CurrentScene)?.ClearPlayerStructures();
        if (SaveCapture.FindChild<BuildingSystem>(Tree.CurrentScene) is { } builder)
        {
            foreach (var entry in data.Structures)
            {
                var instance = builder.SpawnStructure(entry.BuildType, entry.Position, entry.YawDegrees);
                if (instance == null) continue;

                SaveCapture.FindChild<HealthComponent>(instance)?.SetHealth(entry.Health);
                applied++;
            }
        }

        // 4. Fog of war: replay the explored cells.
        if (FogOfWarSystem.Instance is { } fog && !string.IsNullOrEmpty(data.ExploredFog))
            applied += fog.ImportExplored(data.ExploredFog) > 0 ? 1 : 0;

        return applied;
    }
}
