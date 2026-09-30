using Godot;
using System;
using System.Collections.Generic;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Core.Vision;
using ZombieApocalypse.Entities.Bandits;
using ZombieApocalypse.Entities.NPC;
using ZombieApocalypse.Entities.Player;
using ZombieApocalypse.Entities.Survivors;
using ZombieApocalypse.Entities.Zombies;
using ZombieApocalypse.Systems.Building;
using ZombieApocalypse.Systems.Loot;
using ZombieApocalypse.World.Defenses;
using ZombieApocalypse.World.Environment;
using ZombieApocalypse.World.Power.Distribution;
using ZombieApocalypse.World.Power.Generation;
using ZombieApocalypse.World.Power.Storage;
using ZombieApocalypse.World.WorldEvents;

namespace ZombieApocalypse.Core.Persistence;

/// <summary>
/// Reads a snapshot of the live simulation out of the scene tree.
///
/// Everything is read through groups (player / survivors / power_nodes / ...),
/// never through hard-coded node paths, so the same capture code works in the
/// arena and in the headless self-test.
/// </summary>
public static class SaveCapture
{
    /// <summary>Snapshot the whole simulation, including the run statistics.</summary>
    public static SaveData CaptureAll(StatsData stats)
    {
        var data = new SaveData
        {
            SavedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ScenePath = CurrentScenePath(),
            Stats = stats,
        };

        data.Clock = CaptureClock();
        data.Player = CapturePlayer();
        data.Power = CapturePower();
        data.Camp = CaptureCamp();
        data.Camp.Survivors = CaptureSurvivors();
        data.World = CaptureWorld();
        return data;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static SceneTree? Tree => Engine.GetMainLoop() as SceneTree;

    public static string CurrentScenePath() => Tree?.CurrentScene?.SceneFilePath ?? "";

    /// <summary>Depth-first search for a component anywhere under a node.</summary>
    public static T? FindChild<T>(Node? root) where T : Node
    {
        if (root == null) return null;
        if (root is T match) return match;

        foreach (var child in root.GetChildren())
        {
            var found = FindChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>Flatten an inventory into (itemId, quantity) pairs.</summary>
    public static List<SlotData> CaptureSlots(InventoryComponent? inventory)
    {
        var slots = new List<SlotData>();
        if (inventory == null) return slots;

        foreach (var slot in inventory.Slots)
            if (slot.Item != null && slot.Quantity > 0)
                slots.Add(new SlotData(slot.Item.ItemId, slot.Quantity));

        return slots;
    }

    // ── Clock ────────────────────────────────────────────────────────

    public static ClockData CaptureClock()
    {
        var time = TimeManager.Instance;
        return new ClockData
        {
            Hour = time?.CurrentHour ?? 12f,
            DayCount = time?.DayCount ?? 1,
            IsPaused = time?.IsTimePaused ?? false,
            TimeScale = time?.TimeScale ?? 1f,
        };
    }

    // ── Player ───────────────────────────────────────────────────────

    public static PlayerData CapturePlayer()
    {
        var player = Tree?.GetFirstNodeInGroup("player") as Node3D;
        var data = new PlayerData();
        if (player == null) return data;

        data.Position = player.GlobalPosition;
        data.YawDegrees = player.RotationDegrees.Y;

        var health = FindChild<HealthComponent>(player);
        if (health != null)
        {
            data.Health = health.CurrentHealth;
            data.MaxHealth = health.MaxHealth;
        }

        var stats = FindChild<PlayerStats>(player);
        if (stats != null)
        {
            data.Hunger = stats.Hunger;
            data.Thirst = stats.Thirst;
            data.Stamina = stats.Stamina;
        }

        var vision = FindChild<FieldOfView>(player);
        if (vision != null)
            data.FlashlightOn = vision.IsFlashlightActive;

        data.Slots = CaptureSlots(FindChild<InventoryComponent>(player));
        return data;
    }

    // ── Power grid ───────────────────────────────────────────────────

    public static PowerData CapturePower()
    {
        var data = new PowerData();
        if (Tree == null) return data;

        foreach (var node in Tree.GetNodesInGroup("power_nodes"))
        {
            if (node is CombustionGenerator generator)
            {
                data.GeneratorRunning = generator.Running;
                data.GeneratorFuel = generator.FuelCanisters;
            }
            else if (node is BatteryBank battery)
            {
                data.BatteryChargeWattHours = battery.ChargeWattHours;
            }
        }

        foreach (var node in Tree.GetNodesInGroup("power_wires"))
            if (node is WiringSegment cable && cable.IsSevered)
                data.SeveredCables.Add(cable.Name.ToString());

        return data;
    }

    // ── Camp ─────────────────────────────────────────────────────────

    public static CampData CaptureCamp()
    {
        var data = new CampData
        {
            Morale = MoraleSystem.Instance?.Morale ?? 50f,
            WeatherState = (int)(World.Environment.WeatherSystem.Instance?.CurrentWeather
                                 ?? Core.Data.WeatherState.Clear),
            Season = (int)(World.Environment.WeatherSystem.Instance?.CurrentSeason
                           ?? Core.Data.Season.Spring),
        };

        // The camp stockpile is a container with a fixed manifest.
        if (FindStockpile() is { } stockpile)
        {
            foreach (var entry in stockpile.Contents)
            {
                if (entry.Item == null || entry.MinQty <= 0) continue;
                data.Stockpile.Add(new SlotData(entry.Item.ItemId, entry.MinQty));
            }
        }

        if (Tree?.GetFirstNodeInGroup("airdrop_events") is AirdropEvent airdrop)
        {
            data.AirdropDropped = airdrop.HasDropped;
            data.AirdropPosition = airdrop.DropPosition;
        }

        return data;
    }

    /// <summary>The camp stockpile container, or null when the scene has none.</summary>
    public static LootContainer? FindStockpile()
    {
        if (Tree == null) return null;

        var tagged = Tree.GetFirstNodeInGroup("camp_stockpile") as LootContainer;
        if (tagged != null) return tagged;

        // Fall back to a container explicitly labelled as the stockpile.
        foreach (var node in Tree.GetNodesInGroup("loot_containers"))
            if (node is LootContainer container
                && container.ContainerLabel.Contains("Stockpile", StringComparison.OrdinalIgnoreCase))
                return container;

        return null;
    }

    // ── Survivors ────────────────────────────────────────────────────

    public static List<SurvivorData> CaptureSurvivors()
    {
        var list = new List<SurvivorData>();
        if (Tree == null) return list;

        foreach (var node in Tree.GetNodesInGroup("survivors"))
        {
            if (node is not SurvivorBase survivor) continue;

            list.Add(new SurvivorData
            {
                Name = survivor.SurvivorName,
                Archetype = (int)survivor.Archetype,
                Position = survivor.GlobalPosition,
                Health = survivor.Health?.CurrentHealth ?? 0f,
                Alive = survivor.IsAlive,
                Task = (int)survivor.CurrentTask,
                TaskMode = (int)survivor.TaskMode,
                Slots = CaptureSlots(survivor.Inventory),
            });
        }

        return list;
    }

    // ── World state ──────────────────────────────────────────────────

    public static WorldData CaptureWorld()
    {
        var data = new WorldData();
        if (Tree == null) return data;

        // Scene-placed containers: keyed by node path so a load can find them
        // again. Runtime caches (airdrop/bandit loot) are re-created instead.
        foreach (var node in Tree.GetNodesInGroup("loot_containers"))
        {
            if (node is not LootContainer container) continue;
            if (container.IsAirdropCache || container == FindStockpile()) continue;

            data.Containers.Add(new ContainerData
            {
                Path = container.GetPath().ToString(),
                Searched = container.IsSearched,
            });
        }

        foreach (var node in Tree.GetNodesInGroup("corpses"))
        {
            if (node is not Corpse corpse) continue;

            data.Corpses.Add(new CorpseData
            {
                Position = corpse.GlobalPosition,
                Stage = (int)corpse.CurrentStage,
                AgeSeconds = corpse.AgeSeconds,
                Searched = corpse.IsSearched,
                Buried = corpse.IsBuried,
            });
        }

        // Player-built walls/doors are runtime instances, so they are stored as
        // build-type indices and re-instanced by BuildingSystem on load.
        foreach (var node in Tree.GetNodesInGroup("player_structures"))
        {
            int buildType = node switch
            {
                DoorBase => 3,
                WallBase wall => wall.WallTier >= 2 ? 2 : 1,
                _ => 1,
            };

            float health = FindChild<HealthComponent>(node)?.CurrentHealth ?? 0f;
            data.Structures.Add(new StructureData
            {
                BuildType = buildType,
                Position = (node as Node3D)?.GlobalPosition ?? Vector3.Zero,
                YawDegrees = (node as Node3D)?.RotationDegrees.Y ?? 0f,
                Health = health,
            });
        }

        data.ExploredFog = FogOfWarSystem.Instance?.ExportExplored() ?? "";

        return data;
    }
}
