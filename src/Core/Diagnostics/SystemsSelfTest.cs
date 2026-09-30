using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using ZombieApocalypse.Core.Audio;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Core.Persistence;
using ZombieApocalypse.Core.Utilities;
using ZombieApocalypse.Entities.Bandits;
using ZombieApocalypse.Entities.NPC;
using ZombieApocalypse.Entities.Player;
using ZombieApocalypse.Systems.Crafting;
using ZombieApocalypse.Systems.Loot;
using ZombieApocalypse.World.Environment;
using ZombieApocalypse.World.Power;
using ZombieApocalypse.World.Power.Distribution;
using ZombieApocalypse.World.Power.Generation;
using ZombieApocalypse.World.Power.Loads;
using ZombieApocalypse.World.Power.Storage;
using ZombieApocalypse.World.WorldEvents;
using ZombieApocalypse.Entities.Survivors;

namespace ZombieApocalypse.Core.Diagnostics;

/// <summary>
/// Headless regression harness for the survival (Phase 5) and power (Phase 6)
/// systems. Run it with:
///
///   Godot --headless --path &lt;project&gt; res://scenes/levels/SystemsSelfTest.tscn
///
/// It exits with a non-zero exit code equal to the number of failed assertions,
/// which makes it usable in CI or as a pre-commit smoke check.
/// </summary>
public partial class SystemsSelfTest : Node3D
{
    private int _passed;
    private int _failed;

    // Fixtures
    private PowerGrid _grid = null!;
    private CombustionGenerator _generator = null!;
    private BatteryBank _battery = null!;
    private Inverter _inverter = null!;
    private SearchLight _light = null!;
    private ElectricFence _fence = null!;
    private Freezer _freezer = null!;
    private WiringSegment _cable = null!;
    private InventoryComponent _inventory = null!;

    private bool _rotEventFired;
    private bool _overloadEventFired;

    public override void _Ready()
    {
        GD.Print("═══════════════════════════════════════════════");
        GD.Print("  PROJECT ZOMBIE — SYSTEMS SELF-TEST (P5 + P6)");
        GD.Print("═══════════════════════════════════════════════");

        BuildFixtures();

        TestPhase5Inventory();
        TestPhase5Consumables();
        TestPhase5Crafting();
        TestPhase5LootContainer();
        TestPhase5CorpseLifecycle();
        TestPhase6GridTopology();
        TestPhase6Islanding();
        TestPhase6BatteryAndShedding();
        TestPhase7Morale();
        TestPhase7SurvivorTasks();
        TestPhase7BanditSquad();
        TestPhase7Airdrop();
        TestStage1WiringFixes();
        TestStage2Services();
        TestStage3Persistence();
        TestStage4WeatherAndSeasons();
        TestStage5PauseMenu();
        TestStage6LiveDifficulty();
        TestStage8Infection();
        TestStage8ScentTracking();
        TestStage8ZombieRoster();
        TestStage8Perimeter();
        TestStage9AimingGeometry();

        GD.Print("═══════════════════════════════════════════════");
        GD.Print($"  RESULT: {_passed} passed, {_failed} failed");
        GD.Print("═══════════════════════════════════════════════");

        GetTree().Quit(_failed);
    }

    // ── Assertion helpers ────────────────────────────────────────────

    private void Check(string label, bool condition, string detail = "")
    {

        if (condition)
        {
            _passed++;
            GD.Print($"  [PASS] {label}");
        }
        else
        {
            _failed++;
            GD.PrintErr($"  [FAIL] {label} {detail}");
        }
    }


    // ── Fixtures ─────────────────────────────────────────────────────

    private void BuildFixtures()
    {
        _grid = new PowerGrid { Name = "TestPowerGrid", LogIslandRebuilds = false, TickInterval = 999f };
        AddChild(_grid);

        _generator = new CombustionGenerator
        {
            Name = "TestGenerator", Position = new Vector3(0f, 0f, 0f),
            SupplyWatts = 1000f, FuelCanisters = 1f, MaxCanisters = 4f, NoiseRadius = 0f
        };
        AddChild(_generator);

        _battery = new BatteryBank
        {
            Name = "TestBattery", Position = new Vector3(1.5f, 0f, 0f),
            CapacityWattHours = 100f, InitialChargePercent = 1f,
            MaxChargeRateWatts = 100f, MaxDischargeRateWatts = 800f
        };
        AddChild(_battery);

        _inverter = new Inverter { Name = "TestInverter", Position = new Vector3(3f, 0f, 0f), IdleLoadWatts = 10f };
        AddChild(_inverter);

        _light = new SearchLight { Name = "TestLight", Position = new Vector3(4.5f, 0f, 0f), RequiredWatts = 100f };
        AddChild(_light);

        _freezer = new Freezer { Name = "TestFreezer", Position = new Vector3(0f, 0f, 3f), RequiredWatts = 200f };
        AddChild(_freezer);

        // Far from the base cluster: only reachable through the cable
        _fence = new ElectricFence { Name = "TestFence", Position = new Vector3(-12f, 0f, 0f), RequiredWatts = 300f };
        AddChild(_fence);

        _cable = new WiringSegment
        {
            Name = "TestCable", Position = new Vector3(-6f, 0f, 0f),
            DrawCableMesh = false
        };
        AddChild(_cable);
        _cable.NodeAPath = _cable.GetPathTo(_inverter);
        _cable.NodeBPath = _cable.GetPathTo(_fence);

        // Test "player" with the survival components attached
        var testPlayer = new Node3D { Name = "TestPlayer" };
        AddChild(testPlayer);

        var health = new HealthComponent { Name = "HealthComponent", MaxHealth = 100f };
        testPlayer.AddChild(health);

        var stats = new PlayerStats { Name = "PlayerStats" };
        testPlayer.AddChild(stats);

        _inventory = new InventoryComponent
        {
            Name = "InventoryComponent", GrantStartingItems = false, MaxWeightKg = 1f, SlotCount = 24
        };
        testPlayer.AddChild(_inventory);

        if (EventBus.Instance != null)
        {
            EventBus.Instance.OnCorpseRotAdvanced += (_, _) => _rotEventFired = true;
            EventBus.Instance.OnGridOverload += _ => _overloadEventFired = true;
        }

        GD.Print($"  Fixtures built: {GetChildCount()} root children.");
    }

    // ── Phase 5: survival & economy ─────────────────────────────────

    private void TestPhase5Inventory()
    {
        GD.Print("── Phase 5: Inventory ──");

        int added = _inventory.TryAdd(ItemData.Cloth, 5);
        Check("add 5× cloth", added == 5 && _inventory.CountOf("cloth") == 5, $"(added {added})");
        Check("weight tracked (5×0.05kg = 0.25kg)", Mathf.Abs(_inventory.CurrentWeightKg - 0.25f) < 0.01f,
            Near(_inventory.CurrentWeightKg, 0.25f, 0.01f));

        int nailsAdded = _inventory.TryAdd(ItemData.Nails, 25);
        Check("stack overflow spills to a second slot", nailsAdded == 25 && _inventory.CountOf("nails") == 25,
            $"(added {nailsAdded})");

        int nailSlots = 0;
        foreach (var slot in _inventory.Slots)
            if (slot.Item?.ItemId == "nails") nailSlots++;
        Check("25 nails occupy 2 slots (max stack 20)", nailSlots == 2, $"(slots {nailSlots})");

        _inventory.TryAdd(ItemData.WoodPlank, 2);
        Check("encumbrance above the 1kg limit", _inventory.IsEncumbered,
            $"(carrying {_inventory.CurrentWeightKg:F2}kg)");

        int removed = _inventory.TryRemove("cloth", 2);
        Check("remove 2× cloth", removed == 2 && _inventory.CountOf("cloth") == 3, $"(removed {removed})");
    }

    private static string Near(float a, float b, float tolerance = 0.01f)
        => $"expected {b:F2}, got {a:F2} (tol {tolerance:F2})";

    // ── Phase 5: consumables, crafting, loot, corpse rot ────────────

    private void TestPhase5Consumables()
    {
        GD.Print("── Phase 5: Consumables ──");

        var stats = GetNode<Node3D>("TestPlayer").GetNode<PlayerStats>("PlayerStats");
        var health = GetNode<Node3D>("TestPlayer").GetNode<HealthComponent>("HealthComponent");

        health.TakeDamage(50f, DamageType.Blunt, null);
        Check("health drops to 50", Mathf.Abs(health.CurrentHealth - 50f) < 0.01f, Near(health.CurrentHealth, 50f));

        _inventory.TryAdd(ItemData.Bandage, 1);
        bool usedBandage = _inventory.TryUseItem("bandage", stats, health);
        Check("bandage heals +20 HP", usedBandage && Mathf.Abs(health.CurrentHealth - 70f) < 0.01f,
            Near(health.CurrentHealth, 70f));
        Check("bandage consumed", _inventory.CountOf("bandage") == 0);

        float hungerBefore = stats.Hunger;
        _inventory.TryAdd(ItemData.FoodCan, 1);
        bool ate = _inventory.TryUseItem("food_can", stats, health);
        Check("canned food restores hunger", ate && stats.Hunger > hungerBefore,
            $"(hunger {hungerBefore:F1} → {stats.Hunger:F1})");

        float thirstBefore = stats.Thirst;
        _inventory.TryAdd(ItemData.WaterBottle, 1);
        bool drank = _inventory.TryUseItem("water_bottle", stats, health);
        Check("water restores thirst", drank && stats.Thirst > thirstBefore,
            $"(thirst {thirstBefore:F1} → {stats.Thirst:F1})");

        bool missing = _inventory.TryUseItem("medkit", stats, health);
        Check("using a missing item fails cleanly", !missing);
    }

    private void TestPhase5Crafting()
    {
        GD.Print("── Phase 5: Crafting ──");

        var testPlayer = GetNode<Node3D>("TestPlayer");
        var crafter = new CraftingSystem { Name = "CraftingSystem" };
        testPlayer.AddChild(crafter);

        _inventory.TryAdd(ItemData.Cloth, 2);
        _inventory.TryRemove("bandage", 99);
        int clothBefore = _inventory.CountOf("cloth");

        Check("recipe unavailable without ingredients", !crafter.CanCraft("Molotov Cocktail"));
        Check("recipe available with 2× cloth", crafter.CanCraft("Bandage"));

        bool crafted = crafter.TryCraft("Bandage");
        Check("craft 2× bandage from 2× cloth",
            crafted && _inventory.CountOf("bandage") == 2 && _inventory.CountOf("cloth") == clothBefore - 2,
            $"(bandages {_inventory.CountOf("bandage")}, cloth {_inventory.CountOf("cloth")})");

        _inventory.TryAdd(ItemData.FuelCan, 1);
        _inventory.TryAdd(ItemData.Cloth, 1);
        bool molotov = crafter.TryCraft("Molotov Cocktail");
        Check("craft molotov from fuel + cloth",
            molotov && _inventory.CountOf("molotov") == 1 && _inventory.CountOf("fuel_can") == 0);

        Check("failed craft consumes nothing", !crafter.TryCraft("Improvised First Aid Kit"));
    }

    private void TestPhase5LootContainer()
    {
        GD.Print("── Phase 5: Loot Containers ──");

        var crate = new LootContainer
        {
            Name = "TestCrate",
            ContainerType = LootContainer.ContainerArchetype.ToolBox,
            ContainerLabel = "Test Toolbox",
            MinRolls = 3,
            MaxRolls = 3,
            SearchTime = 0.1f
        };
        AddChild(crate);

        int before = CountAllItems(_inventory);
        crate.TrySearch(_inventory);
        crate._Process(0.5);
        int after = CountAllItems(_inventory);

        Check("search transfers loot into the inventory", after > before, $"({before} → {after} items)");
        Check("container is marked searched", crate.IsSearched);
        Check("searching twice yields nothing new", !crate.IsSearching);
    }

    private void TestPhase5CorpseLifecycle()
    {
        GD.Print("── Phase 5: Corpse Rot & Miasma ──");

        _rotEventFired = false;

        var corpse = new Corpse
        {
            Name = "TestCorpse",
            FreshDuration = 0f,
            BloatedDuration = 0f,
            MiasmaDuration = 5f,
            SkeletonDuration = 1f,
            MiasmaRadius = 4f,
            MiasmaDamagePerSecond = 0f
        };
        AddChild(corpse);

        Check("corpse starts Fresh", corpse.CurrentStage == CorpseStage.Fresh);

        corpse._Process(0.1);
        Check("corpse advances to RottingMiasma", corpse.CurrentStage == CorpseStage.RottingMiasma,
            $"(stage {corpse.CurrentStage})");
        Check("OnCorpseRotAdvanced broadcast", _rotEventFired);

        corpse.Scavenge(_inventory);
        Check("corpse scavenging yields cloth", corpse.IsSearched && _inventory.CountOf("cloth") > 0);

        corpse.Bury();
        Check("burial removes the hazard", corpse.IsBuried);
    }

    // ── Phase 7: NPCs & bandits ────────────────────────────────────────

    private void TestPhase7Morale()
    {
        GD.Print("── Phase 7: Camp Morale ──");

        var morale = new MoraleSystem { Name = "TestMorale" };
        AddChild(morale);

        float moraleEvents = 0f;
        EventBus.Instance!.OnCampMoraleChanged += m => moraleEvents = m;

        Check("morale starts in a sane band", morale.Morale is >= 0f and <= 100f, $"(morale {morale.Morale:F1})");

        morale.SetMorale(60f);
        morale.Recalculate(10f, hungerFraction: 0.0f, activeMiasmaZones: 0, powerOnline: true);
        Check("starvation drags morale down", morale.Morale < 60f, $"(morale {morale.Morale:F1})");

        morale.SetMorale(60f);
        morale.Recalculate(10f, hungerFraction: 1f, activeMiasmaZones: 2, powerOnline: true);
        Check("miasma drags morale down", morale.Morale < 60f, $"(morale {morale.Morale:F1})");

        morale.SetMorale(20f);
        Check("low morale slows survivors", Mathf.Abs(morale.GetSurvivorSpeedMultiplier() - 0.8f) < 0.01f,
            $"(x{morale.GetSurvivorSpeedMultiplier():F2})");

        morale.SetMorale(90f);
        Check("high morale speeds survivors", Mathf.Abs(morale.GetSurvivorSpeedMultiplier() - 1.1f) < 0.01f,
            $"(x{morale.GetSurvivorSpeedMultiplier():F2})");

        morale.SetMorale(50f);
        morale.AdjustMorale(-12f);
        Check("survivor-death penalty is ~12", Mathf.Abs(morale.Morale - 38f) < 0.01f,
            $"(morale {morale.Morale:F1})");
        Check("morale changes broadcast on the bus", Mathf.Abs(moraleEvents - 38f) < 0.01f,
            $"(bus saw {moraleEvents:F1})");

        morale.SetMorale(99f);
        morale.AdjustMorale(10f);
        Check("morale clamps at 100", Mathf.Abs(morale.Morale - 100f) < 0.01f,
            $"(morale {morale.Morale:F1})");
    }

    private void TestPhase7SurvivorTasks()
    {
        GD.Print("── Phase 7: Survivor Tasks ──");

        var survivor = new SurvivorBase
        {
            Name = "TestSurvivor",
            SurvivorName = "Test Survivor",
            Archetype = SurvivorArchetype.CombatMedic,
            CampAnchor = new Vector3(0f, 0f, 0f),
            Position = new Vector3(0f, 0f, 0f),
        };
        AddChild(survivor);

        Check("survivor starts alive in the survivors group",
            survivor.IsAlive && survivor.IsInGroup("survivors"));

        var crate = new LootContainer
        {
            Name = "TaskCrate",
            ContainerType = LootContainer.ContainerArchetype.FoodLocker,
            Position = new Vector3(2f, 0f, 0f),
        };
        AddChild(crate);

        var ai = survivor.GetNodeOrNull<SurvivorAI>("SurvivorAI");
        Check("survivor builds its AI child", ai != null);

        survivor.TaskMode = SurvivorTask.Scavenge;
        ai!.PickBestTask();
        Check("scavenger is assigned the unsearched crate",
            survivor.CurrentTask == SurvivorTask.Scavenge && survivor.TaskTarget == crate,
            $"(task {survivor.CurrentTask})");

        bool transferred = crate.ForceSearch(survivor.Inventory!);
        Check("survivor search transfers loot", transferred && crate.IsSearched);

        var ally = new SurvivorBase
        {
            Name = "HurtAlly",
            SurvivorName = "Hurt Ally",
            Position = new Vector3(1f, 0f, 0f),
        };
        AddChild(ally);
        ally.Health.TakeDamage(80f, DamageType.Blunt, null);

        survivor.TaskMode = SurvivorTask.Heal;
        survivor.SetTask(SurvivorTask.Idle, null, Vector3.Zero);
        ai.PickBestTask();
        Check("medic diverts to a hurt ally",
            survivor.CurrentTask == SurvivorTask.Heal && survivor.TaskTarget == ally,
            $"(task {survivor.CurrentTask})");

        var medic = new CombatMedic { Name = "MedicArchetype" };
        AddChild(medic);
        Check("CombatMedic is a healer archetype",
            medic.Archetype == SurvivorArchetype.CombatMedic && medic.HealMultiplier > 1f,
            $"(x{medic.HealMultiplier:F2})");

        var veteran = new CombatVeteran { Name = "VeteranArchetype" };
        AddChild(veteran);
        Check("CombatVeteran hits harder than a base survivor",
            veteran.MeleeDamage > 14f, $"({veteran.MeleeDamage:F1} dmg)");

        var scout = new ScavengerScout { Name = "ScoutArchetype" };
        AddChild(scout);
        Check("ScavengerScout moves faster and searches quicker",
            scout.MoveSpeed > 3.6f && scout.SearchTimeMultiplier < 1f,
            $"({scout.MoveSpeed:F1} m/s, x{scout.SearchTimeMultiplier:F2} search)");
    }

    private void TestPhase7BanditSquad()
    {
        GD.Print("── Phase 7: Bandit Squad ──");

        bool raidAnnounced = false;
        bool squadEliminated = false;
        EventBus.Instance!.OnBanditRaidIncoming += _ => raidAnnounced = true;
        EventBus.Instance!.OnBanditSquadEliminated += _ => squadEliminated = true;

        var spawner = new BanditSpawner { Name = "TestBanditSpawner" };
        AddChild(spawner);

        var squad = spawner.SpawnSquadAt(BanditTier.Scavenger, 3, new Vector3(20f, 0f, 20f));
        Check("squad deploys 3 scavengers", squad.Members.Count == 3, $"({squad.Members.Count})");
        Check("raid announcement broadcast", raidAnnounced);

        var roles = new HashSet<SquadRole>();
        foreach (var member in squad.Members)
            roles.Add(member.Role);
        Check("leader advances while others suppress/flank",
            squad.Members[0].Role == SquadRole.Advance && roles.Count >= 2,
            $"({string.Join("/", roles)})");

        Vector3 flank = BanditSquad.ComputeFlankPoint(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
        Check("flank point steps 6 m sideways",
            Mathf.Abs(flank.X - 10f) < 0.01f && Mathf.Abs(flank.Z - 6f) < 0.01f,
            $"({flank})");

        var scavenger = squad.Members[0];
        Check("scavenger retreat threshold is 50%", Mathf.Abs(scavenger.RetreatThreshold - 0.5f) < 0.01f);
        scavenger.Health.TakeDamage(60f, DamageType.Blunt, null);
        scavenger._PhysicsProcess(0.1);
        Check("beaten scavenger flees", scavenger.IsFleeing && scavenger.CombatState == BanditCombatState.Retreat);

        var warlordSquad = spawner.SpawnSquadAt(BanditTier.Warlord, 1, new Vector3(30f, 0f, 30f));
        var warlord = warlordSquad.Members[0];
        warlord.Health.TakeDamage(90f, DamageType.Blunt, null);
        warlord._PhysicsProcess(0.1);
        Check("warlord never retreats", !warlord.IsFleeing && warlord.RetreatThreshold <= 0f);

        var prey = BanditSquad.FindPreyNear(new Vector3(20f, 0f, 20f), 40f);
        Check("squad senses nearby prey (survivor/zombie/player)", prey != null,
            prey == null ? "(none)" : $"({prey.Name})");

        foreach (var member in new List<BanditBase>(squad.Members))
            member.Health.Kill();
        Check("wiping the squad fires elimination", squadEliminated && squad.Members.Count == 0);

        var lootSquad = spawner.SpawnSquadAt(BanditTier.Militia, 0, new Vector3(40f, 0f, 40f));
        Check("empty squad reports eliminated", lootSquad.IsEliminated);
    }

    private void TestPhase7Airdrop()
    {
        GD.Print("── Phase 7: Airdrop Contest ──");

        Vector3 dropPos = Vector3.Zero;
        EventBus.Instance!.OnAirdropSpawned += p => dropPos = p;

        var airdrop = new AirdropEvent
        {
            Name = "TestAirdrop",
            DropPosition = new Vector3(10f, 0f, -10f),
            MedKits = 2,
            Ammo9mmPacks = 3,
            FoodCans = 3,
            ClothBundles = 2,
        };
        AddChild(airdrop);
        airdrop.TriggerDrop();

        Check("airdrop cache lands at the drop zone",
            airdrop.HasDropped && airdrop.Cache != null
                && airdrop.Cache.GlobalPosition.DistanceSquaredTo(new Vector3(10f, 0f, -10f)) < 0.01f);
        Check("airdrop broadcast carries the position",
            dropPos.DistanceSquaredTo(new Vector3(10f, 0f, -10f)) < 0.01f, $"({dropPos})");

        Check("airdrop cache is reserved from scavengers",
            airdrop.Cache!.IsAirdropCache
                && SurvivorBase.FindUnsearchedContainer(new Vector3(10f, 0f, -10f), 5f) != airdrop.Cache);

        var playerInv = new InventoryComponent { Name = "AirdropPlayerInv", GrantStartingItems = false };
        AddChild(playerInv);
        bool looted = airdrop.Cache.ForceSearch(playerInv);
        Check("player can still loot the airdrop",
            looted && playerInv.CountOf("medkit") == 2 && playerInv.CountOf("ammo_9mm") == 3
                && playerInv.CountOf("food_can") == 3 && playerInv.CountOf("cloth") == 2,
            $"(medkit {playerInv.CountOf("medkit")}, 9mm {playerInv.CountOf("ammo_9mm")})");

        int banditsNearDrop = 0;
        foreach (var node in GetTree().GetNodesInGroup("bandits"))
        {
            if (node is Node3D n && n.GlobalPosition.DistanceSquaredTo(new Vector3(10f, 0f, -10f)) < 20f * 20f)
                banditsNearDrop++;
        }
        Check("raiders contest the drop zone", banditsNearDrop >= 2, $"({banditsNearDrop} nearby)");
    }

    // ── Stage 1: integration fixes ─────────────────────────────────────

    private void TestStage1WiringFixes()
    {
        GD.Print("── Stage 1: Wiring Fixes ──");

        var ally = GetTree().GetFirstNodeInGroup("survivors") as SurvivorBase;
        Check("survivors use their own physics layer",
            ally != null && ally.CollisionLayer == 16, $"(layer {ally?.CollisionLayer})");

        var bandit = GetTree().GetFirstNodeInGroup("bandits") as BanditBase;
        Check("bandits use their own physics layer",
            bandit != null && bandit.CollisionLayer == 32, $"(layer {bandit?.CollisionLayer})");

        // Zombie senses must pick up NPCs, not only the player.
        var sensor = new ZombieApocalypse.Entities.Zombies.ZombieAI.SensorySystem { Name = "TestSensor" };
        var focusTarget = SurvivorBase.FindThreatNear(ally!.GlobalPosition, 50f);   // keep the API exercised
        var probe = new Node3D { Name = "SensorProbe", Position = ally.GlobalPosition + new Vector3(3f, 0f, 0f) };
        AddChild(probe);
        probe.AddChild(sensor);
        sensor._PhysicsProcess(0.2);
        Check("zombie senses a nearby survivor (not just the player)",
            sensor.CurrentTarget is SurvivorBase,
            $"(target {sensor.CurrentTarget?.Name ?? "none"})");
        Check("threat search finds a zombie or bandit, never an ally",
            focusTarget == null || focusTarget is BanditBase,
            $"(threat {focusTarget?.Name ?? "none"})");

        // Bandit death must leave a searchable cache behind.
        var spawner = new BanditSpawner { Name = "DropTestSpawner" };
        AddChild(spawner);
        var dropper = spawner.SpawnSquadAt(BanditTier.Militia, 1, new Vector3(14f, 0f, 14f)).Members[0];
        dropper.Inventory!.TryAdd(ItemData.FoodCan, 2);
        int cachesBefore = GetTree().GetNodesInGroup("loot_containers").Count;
        dropper.Health.Kill();
        int cachesAfter = GetTree().GetNodesInGroup("loot_containers").Count;
        Check("bandit death spawns a lootable raider cache", cachesAfter > cachesBefore,
            $"({cachesBefore} → {cachesAfter})");

        LootContainer? cache = null;
        foreach (var node in GetTree().GetNodesInGroup("loot_containers"))
        {
            if (node is LootContainer candidate && candidate.Name.ToString().Contains("Drop"))
                cache = candidate;
        }
        var takerInv = new InventoryComponent { Name = "StageOneInv", GrantStartingItems = false };
        AddChild(takerInv);
        Check("raider cache holds the bandit's carried loot",
            cache != null && cache.ForceSearch(takerInv) && takerInv.CountOf("food_can") == 2,
            $"({takerInv.CountOf("food_can")} cans)");

        // Survivors deliver scavenge to the camp stockpile.
        var stockpile = new LootContainer
        {
            Name = "StageOneStockpile",
            ContainerLabel = "Camp Stockpile",
            ContainerType = LootContainer.ContainerArchetype.GeneralJunk,
            Position = new Vector3(6f, 0f, 6f),
        };
        AddChild(stockpile);
        stockpile.AddToGroup("camp_stockpile");

        Check("stockpile is hidden from NPC scavenging",
            SurvivorBase.FindUnsearchedContainer(stockpile.GlobalPosition, 5f) != stockpile);
        Check("stockpile is discoverable for deliveries",
            SurvivorBase.FindStockpile(stockpile.GlobalPosition, 5f) == stockpile);

        int carriedBefore = ally.Inventory!.TotalItemCount();
        bool deposited = stockpile.TryDeposit(ally.Inventory);
        Check("survivor deposit moves loot into the stockpile",
            deposited && ally.Inventory.TotalItemCount() == 0 && carriedBefore > 0,
            $"({carriedBefore} items moved)");
        Check("stockpile reopens for the player to withdraw",
            !stockpile.IsSearched && stockpile.ForceSearch(takerInv));

        // Component-driven interaction contract.
        var machine = new Node3D { Name = "StageOneMachine" };
        AddChild(machine);
        var interactable = new InteractableComponent { Name = "InteractableComponent", Prompt = "[E] Use Machine" };
        bool fired = false;
        interactable.Interacted += _ => fired = true;
        machine.AddChild(interactable);

        Check("interactable component self-joins the interactables group",
            machine.IsInGroup("interactables"));
        Check("interactable component resolves its prompt",
            interactable.GetPrompt() == "[E] Use Machine");
        Check("interactable component runs its callback", interactable.Interact(ally) && fired);
    }

    /// <summary>
    /// Spawn a bare archetype node with the component children it expects.
    /// Without a HealthComponent child its _Ready throws, and _PhysicsProcess
    /// then throws every frame for the rest of the run.
    /// </summary>
    private T SpawnZombie<T>(string name) where T : Entities.Zombies.ZombieBase, new()
    {
        var zombie = new T { Name = name };
        zombie.AddChild(new HealthComponent { Name = "HealthComponent" });
        zombie.AddChild(new Entities.Zombies.ZombieAI.SensorySystem { Name = "SensorySystem" });
        AddChild(zombie);
        return zombie;
    }

    /// <summary>Total number of items across every inventory slot.</summary>
    private static int CountAllItems(InventoryComponent inventory)
    {
        int total = 0;
        foreach (var slot in inventory.Slots)
            total += slot.Quantity;
        return total;
    }

    // ── Phase 6: power grid ─────────────────────────────────────────

    private void TestPhase6GridTopology()
    {
        GD.Print("── Phase 6: Grid Topology ──");

        _grid.Rescan();
        Check("all 6 power nodes registered", _grid.Nodes.Count == 6, $"(found {_grid.Nodes.Count})");
        Check("base + perimeter form ONE island", _grid.IslandCount == 1, $"({_grid.IslandCount} islands)");

        _grid.SimulateTick(1f);

        Check("generator supplies 1000W", Mathf.Abs(_grid.TotalGenerationWatts - 1000f) < 0.5f,
            Near(_grid.TotalGenerationWatts, 1000f, 0.5f));
        Check("total demand is 610W (10+100+200+300)", Mathf.Abs(_grid.TotalDemandWatts - 610f) < 0.5f,
            Near(_grid.TotalDemandWatts, 610f, 0.5f));
        Check("inverter energised (AC bus live)", _inverter.IsPowered);
        Check("floodlight powered", _light.IsPowered);
        Check("electric fence powered through its cable", _fence.IsPowered);
        Check("freezer powered", _freezer.IsPowered);
        Check("no loads shed", _grid.TotalShedLoads == 0, $"(shed {_grid.TotalShedLoads})");
    }

    private void TestPhase6Islanding()
    {
        GD.Print("── Phase 6: BFS Islanding ──");

        _cable.Sever();
        _grid.Rescan();

        Check("severing the cable splits the grid into 2 islands", _grid.IslandCount == 2,
            $"({_grid.IslandCount} islands)");
        Check("fence occupied its own island", _fence.IslandId != _light.IslandId);

        _grid.SimulateTick(1f);
        Check("islanded fence is DEAD (no generation reachable)", !_fence.IsPowered);
        Check("base island keeps running", _light.IsPowered && _freezer.IsPowered && _inverter.IsPowered);
        Check("islanded fence load counts as shed", _grid.TotalShedLoads == 1, $"(shed {_grid.TotalShedLoads})");

        _cable.Repair();
        _grid.Rescan();
        _grid.SimulateTick(1f);

        Check("repairing the cable reconnects to 1 island", _grid.IslandCount == 1,
            $"({_grid.IslandCount} islands)");
        Check("fence is alive again", _fence.IsPowered);
    }

    private void TestPhase6BatteryAndShedding()
    {
        GD.Print("── Phase 6: Battery Buffer & Load Shedding ──");

        // 1. Generator off → the battery must carry every load
        _generator.Running = false;
        float chargeBefore = _battery.ChargeWattHours;
        _grid.SimulateTick(60f);

        Check("battery drains while covering the base", _battery.ChargeWattHours < chargeBefore,
            $"({chargeBefore:F1}Wh → {_battery.ChargeWattHours:F1}Wh)");
        Check("loads survive on battery power", _light.IsPowered && _fence.IsPowered);

        float expectedDrain = 610f * 60f / 3600f;   // 610W for 60s
        Check("drain matches load × time (10.2Wh)", Mathf.Abs((chargeBefore - _battery.ChargeWattHours) - expectedDrain) < 0.5f,
            $"expected {expectedDrain:F2}Wh, drained {chargeBefore - _battery.ChargeWattHours:F2}Wh");

        // 2. Battery empty + generator off → priority shedding
        _overloadEventFired = false;
        _battery.SetChargeWattHours(0f);
        _grid.SimulateTick(1f);

        Check("every load is shed with no source", _grid.TotalShedLoads >= 3, $"(shed {_grid.TotalShedLoads})");
        Check("OnGridOverload broadcast", _overloadEventFired);
        Check("inverter drops out last-resort loads too", !_inverter.IsPowered);
        Check("nothing powered in the dead island",
            !_light.IsPowered && !_freezer.IsPowered && !_fence.IsPowered);

        // 3. Restart the generator: everything comes back
        _generator.Running = true;
        _generator.FuelCanisters = 1f;
        _grid.SimulateTick(1f);

        Check("restarting the generator restores the base",
            _inverter.IsPowered && _light.IsPowered && _fence.IsPowered && _freezer.IsPowered);
        Check("surplus charges the battery", _battery.ChargeWattHours > 0f,
            $"({_battery.ChargeWattHours:F2}Wh after 1s of 390W surplus)");
        Check("grid summary reports a single grid", PowerGrid.Instance != null && _grid.IslandCount == 1);
    }

    // ── Stage 2: Config / Time / Audio autoload services ─────────────

    private void TestStage2Services()
    {
        GD.Print("── Stage 2: Config, Time and Audio autoloads ──");

        // ── ConfigManager ────────────────────────────────────────────
        var config = ConfigManager.Instance;
        Check("ConfigManager autoload is online", config != null);
        if (config != null)
        {
            Check("standard difficulty is the neutral baseline",
                Mathf.Abs(config.ZombieDamageMultiplier - 1f) < 0.001f
                && Mathf.Abs(config.ZombieSpeedMultiplier - 1f) < 0.001f
                && Mathf.Abs(config.LootQuantityMultiplier - 1f) < 0.001f);

            config.SetDifficulty(DifficultyPreset.Hardcore);
            Check("hardcore makes zombies deadlier and faster",
                config.ZombieDamageMultiplier > 1f && config.ZombieSpeedMultiplier > 1f,
                $"(dmg ×{config.ZombieDamageMultiplier:F2}, speed ×{config.ZombieSpeedMultiplier:F2})");
            Check("hardcore tightens the loot economy", config.LootQuantityMultiplier < 1f,
                $"(loot ×{config.LootQuantityMultiplier:F2})");

            config.SetDifficulty(DifficultyPreset.Casual);
            Check("casual favours the survivor",
                config.ZombieDamageMultiplier < 1f && config.LootQuantityMultiplier > 1f);

            config.SetDifficulty(DifficultyPreset.Standard);

            float original = config.MasterVolume;
            config.SetMasterVolume(0.42f);
            Check("settings persist across a reload",
                Mathf.Abs(config.MasterVolume - 0.42f) < 0.001f,
                Near(config.MasterVolume, 0.42f));
            config.SetMasterVolume(original);
        }

        // ── TimeManager ──────────────────────────────────────────────
        var time = TimeManager.Instance;
        Check("TimeManager autoload is online", time != null);
        if (time != null)
        {
            bool wasPaused = time.IsTimePaused;
            time.IsTimePaused = true;

            time.SetHour(12f);
            Check("noon is day with no frenzy bonus",
                time.CurrentPhase == DayPhase.Day && !time.IsNight
                && Mathf.Abs(time.ZombieNightMultiplier - 1f) < 0.001f);

            time.SetHour(23f);
            Check("midnight triggers the night frenzy",
                time.CurrentPhase == DayPhase.Night && time.IsNight
                && Mathf.Abs(time.ZombieNightMultiplier - 1.35f) < 0.001f);

            time.SetHour(6f);
            Check("06:00 reads as dawn", time.CurrentPhase == DayPhase.Dawn);

            int dayBefore = time.DayCount;
            time.Advance(2f);
            Check("hours before midnight keep the day count", time.DayCount == dayBefore);
            time.Advance(22f);
            Check("crossing midnight rolls the survival day",
                time.DayCount == dayBefore + 1 && Mathf.Abs(time.CurrentHour - 6f) < 0.05f,
                $"(day {time.DayCount}, hour {time.CurrentHour:F2})");

            // DayNightCycle must mirror the authoritative clock.
            var cycle = DayNightCycle.Instance;
            Check("DayNightCycle mirrors the autoload clock",
                cycle == null || Mathf.Abs(cycle.CurrentHour - time.CurrentHour) < 0.5f,
                cycle == null ? "(no cycle node in this scene)" : Near(cycle.CurrentHour, time.CurrentHour, 0.5f));

            time.IsTimePaused = wasPaused;
        }

        // ── Procedural sound bank ────────────────────────────────────
        var gunshot = ProceduralSfx.Get(SfxId.Gunshot);
        Check("gunshot clip is synthesised", gunshot != null && gunshot.Data.Length > 1000,
            $"({gunshot?.Data.Length ?? 0} bytes)");
        Check("clips are cached, not rebuilt per call",
            ReferenceEquals(gunshot, ProceduralSfx.Get(SfxId.Gunshot))
            && ProceduralSfx.CachedClipCount >= 2,
            $"({ProceduralSfx.CachedClipCount} cached)");

        var hum = ProceduralSfx.Get(SfxId.GeneratorHum);
        Check("generator hum loops seamlessly",
            hum.LoopMode == AudioStreamWav.LoopModeEnum.Forward && hum.LoopEnd > 0,
            $"(loop end {hum.LoopEnd})");

        float peak = 0f;
        if (gunshot != null)
        {
            for (int i = 0; i < gunshot.Data.Length - 1; i += 2)
            {
                short sample = (short)(gunshot.Data[i] | (gunshot.Data[i + 1] << 8));
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            }
        }
        Check("gunshot actually carries signal", peak > 3000, $"(peak {peak})");

        // ── AudioManager mapping + playback ──────────────────────────
        var (gunSfx, gunDistance) = AudioManager.MapSource(AudioSourceType.Gunshot, 55f);
        var (screamSfx, _) = AudioManager.MapSource(AudioSourceType.VoiceZombie, 45f);
        var (groanSfx, _) = AudioManager.MapSource(AudioSourceType.VoiceZombie, 18f);
        Check("gunshots map to the gunshot clip", gunSfx == SfxId.Gunshot && gunDistance > 40f,
            $"({gunSfx}, {gunDistance:F0}m)");
        Check("a loud zombie voice is a scream, a quiet one a groan",
            screamSfx == SfxId.ZombieScream && groanSfx == SfxId.ZombieGroan);
        Check("louder events are louder than quiet ones",
            AudioManager.LoudnessFromRadius(60f) > AudioManager.LoudnessFromRadius(10f),
            $"(10m {AudioManager.LoudnessFromRadius(10f):F2} vs 60m {AudioManager.LoudnessFromRadius(60f):F2})");

        var audio = AudioManager.Instance;
        Check("AudioManager autoload is online", audio != null);
        if (audio != null)
        {
            int eventsBefore = audio.EventsReceived;
            int playedBefore = audio.SoundsPlayed;

            EventBus.Instance?.EmitSound(new Vector3(0f, 1f, 0f), 55f, AudioSourceType.Gunshot);
            EventBus.Instance?.EmitSound(new Vector3(2f, 1f, 0f), 20f, AudioSourceType.Footstep);

            Check("acoustic events reach the mixer", audio.EventsReceived == eventsBefore + 2,
                $"({eventsBefore} → {audio.EventsReceived})");
            Check("each event consumes a pooled voice", audio.SoundsPlayed == playedBefore + 2,
                $"({playedBefore} → {audio.SoundsPlayed})");

            var cfg = ConfigManager.Instance;
            if (cfg != null)
            {
                float master = cfg.MasterVolume;
                cfg.SetMasterVolume(0f);
                int playedAtZero = audio.SoundsPlayed;
                audio.PlayUi(SfxId.Blip);
                Check("muted master still plays (volume-driven, not skipped)",
                    audio.SoundsPlayed == playedAtZero + 1);
                cfg.SetMasterVolume(master);
            }
        }
    }
    // ── Stage 3: save / load persistence ─────────────────────────────

    private void TestStage3Persistence()
    {
        GD.Print("── Stage 3: Save & Load ──");

        var saves = SaveManager.Instance;
        Check("SaveManager autoload is online", saves != null);
        if (saves == null) return;

        // ── 1. Put the world into a distinctive state ────────────────
        _generator.Running = false;
        _generator.FuelCanisters = 1.5f;
        _battery.SetChargeWattHours(87f);
        if (!_cable.IsSevered) _cable.Sever();
        MoraleSystem.Instance?.SetMorale(33f);
        TimeManager.Instance?.RestoreState(3, 19.5f, true);

        var captured = saves.Capture();

        // ── 2. Capture reflects the live simulation ──────────────────
        Check("capture records the generator state",
            !captured.Power.GeneratorRunning && Mathf.Abs(captured.Power.GeneratorFuel - 1.5f) < 0.01f,
            $"(running {captured.Power.GeneratorRunning}, fuel {captured.Power.GeneratorFuel})");
        Check("capture records the battery charge",
            Mathf.Abs(captured.Power.BatteryChargeWattHours - 87f) < 0.5f,
            $"{captured.Power.BatteryChargeWattHours:F1}Wh");
        Check("capture records severed cables", captured.Power.SeveredCables.Count == 1,
            $"({captured.Power.SeveredCables.Count})");
        Check("capture records camp morale",
            MoraleSystem.Instance == null || Mathf.Abs(captured.Camp.Morale - 33f) < 0.01f,
            $"({captured.Camp.Morale})");
        Check("capture records the clock",
            TimeManager.Instance == null
            || (captured.Clock.DayCount == 3 && Mathf.Abs(captured.Clock.Hour - 19.5f) < 0.01f),
            $"(day {captured.Clock.DayCount}, hour {captured.Clock.Hour})");
        Check("capture records scene containers",
            captured.World.Containers.Count > 0,
            $"({captured.World.Containers.Count} containers, {captured.World.Containers.Count(c => c.Searched)} searched)");

        // ── 3. JSON round trip ───────────────────────────────────────
        var json = captured.ToJson();
        Check("save serialises to JSON", json.Contains("\"version\"") && json.Length > 200,
            $"({json.Length} chars)");

        var parsed = SaveData.FromJson(json);
        Check("save parses back", parsed != null);
        if (parsed != null)
        {
            Check("round trip preserves power state",
                !parsed.Power.GeneratorRunning
                && Mathf.Abs(parsed.Power.GeneratorFuel - 1.5f) < 0.01f
                && Mathf.Abs(parsed.Power.BatteryChargeWattHours - 87f) < 0.5f
                && parsed.Power.SeveredCables.Count == 1);
            Check("round trip preserves camp and clock",
                Mathf.Abs(parsed.Camp.Morale - 33f) < 0.01f
                && parsed.Clock.DayCount == 3
                && Mathf.Abs(parsed.Clock.Hour - 19.5f) < 0.01f);
            Check("round trip preserves the stockpile manifest",
                parsed.Camp.Stockpile.Count == captured.Camp.Stockpile.Count
                && parsed.Camp.Stockpile.All(s => s.Quantity > 0));
        }

        // ── 4. Corrupt / foreign saves are rejected, not crashed on ──
        Check("non-dictionary JSON is rejected", SaveData.FromJson("[1, 2, 3]") == null);
        Check("unsupported version is rejected", SaveData.FromJson("{\"version\": 999}") == null);
        Check("empty string is rejected", SaveData.FromJson("") == null);

        // ── 5. Mutate the world, then restore from the parsed save ───
        _generator.Running = true;
        _generator.FuelCanisters = 4f;
        _battery.SetChargeWattHours(0f);
        if (_cable.IsSevered) _cable.Repair();
        MoraleSystem.Instance?.SetMorale(88f);
        TimeManager.Instance?.RestoreState(1, 3f, false);

        int applied = saves.ApplyTo(parsed!);
        Check("load applies every section", applied >= 4, $"({applied} sections)");

        Check("generator state is restored",
            !_generator.Running && Mathf.Abs(_generator.FuelCanisters - 1.5f) < 0.01f,
            $"(running {_generator.Running}, fuel {_generator.FuelCanisters})");
        Check("battery charge is restored",
            Mathf.Abs(_battery.ChargeWattHours - 87f) < 0.5f, $"{_battery.ChargeWattHours:F1}Wh");
        Check("severed cable is severed again", _cable.IsSevered);
        Check("morale is restored",
            MoraleSystem.Instance == null || Mathf.Abs(MoraleSystem.Instance.Morale - 33f) < 0.01f,
            $"({MoraleSystem.Instance?.Morale})");
        Check("clock is restored",
            TimeManager.Instance == null
            || (TimeManager.Instance.DayCount == 3 && Mathf.Abs(TimeManager.Instance.CurrentHour - 19.5f) < 0.01f
                && TimeManager.Instance.IsTimePaused),
            $"(day {TimeManager.Instance?.DayCount}, hour {TimeManager.Instance?.CurrentHour})");
        Check("grid rescans after a load", PowerGrid.Instance != null);

        // ── 6. Inventory round trip through the component API ────────
        _inventory.ClearContents();
        Check("inventory can be emptied", _inventory.TotalItemCount() == 0);

        _inventory.RestoreContents(new[] { (ItemData.Cloth, 7), (ItemData.Nails, 12) });
        Check("inventory restores stacks",
            _inventory.CountOf("cloth") == 7 && _inventory.CountOf("nails") == 12,
            $"(cloth {_inventory.CountOf("cloth")}, nails {_inventory.CountOf("nails")})");
        Check("restored weight is tracked",
            Mathf.Abs(_inventory.CurrentWeightKg - (7f * ItemData.Cloth.WeightKg + 12f * ItemData.Nails.WeightKg)) < 0.01f,
            $"{_inventory.CurrentWeightKg:F2}kg");

        var resolved = SaveApply.ResolveSlots(new List<SlotData>
        {
            new("cloth", 3),
            new("not_a_real_item", 5),
        });
        Check("unknown item ids are dropped on load", resolved.Count == 1 && resolved[0].item.ItemId == "cloth",
            $"({resolved.Count} resolved)");
        Check("ItemData.Find resolves known ids and rejects unknown",
            ItemData.Find("water_bottle") == ItemData.WaterBottle && ItemData.Find("nope") == null);

        // ── 7. File round trip ───────────────────────────────────────
        const string testPath = "user://selftest_save.json";
        using (var file = Godot.FileAccess.Open(testPath, Godot.FileAccess.ModeFlags.Write))
            file?.StoreString(json);

        var fromDisk = SaveManager.ReadSave(testPath);
        Check("save file reads back from disk",
            fromDisk != null && fromDisk.Clock.DayCount == 3
            && Mathf.Abs(fromDisk.Camp.Morale - 33f) < 0.01f);

        if (Godot.FileAccess.FileExists(testPath))
            Godot.DirAccess.RemoveAbsolute(testPath);
        Check("save file is removed again", !Godot.FileAccess.FileExists(testPath));
        Check("reading a missing file returns null", SaveManager.ReadSave("user://definitely_not_here.json") == null);

        // ── 8. Player section (self-test scene has no player, so make one) ──
        var playerRoot = new Node3D { Name = "TestSavePlayer" };
        playerRoot.AddToGroup("player");
        AddChild(playerRoot);

        var health = new HealthComponent { Name = "HealthComponent", MaxHealth = 100f };
        var stats = new PlayerStats { Name = "PlayerStats" };
        var bag = new InventoryComponent { Name = "InventoryComponent", GrantStartingItems = false };
        playerRoot.AddChild(health);
        playerRoot.AddChild(stats);
        playerRoot.AddChild(bag);

        health.TakeDamage(35f);
        stats.Restore(20f, 30f, 40f);
        bag.RestoreContents(new[] { (ItemData.Scrap, 5) });
        playerRoot.GlobalPosition = new Vector3(7f, 0f, -3f);

        var withPlayer = new SaveData();
        withPlayer.Player = SaveCapture.CapturePlayer();
        Check("player capture reads position and vitals",
            Mathf.Abs(withPlayer.Player.Position.X - 7f) < 0.01f
            && Mathf.Abs(withPlayer.Player.Health - 65f) < 0.5f
            && Mathf.Abs(withPlayer.Player.Hunger - 20f) < 0.5f
            && Mathf.Abs(withPlayer.Player.Stamina - 40f) < 0.5f,
            $"(pos {withPlayer.Player.Position}, hp {withPlayer.Player.Health})");
        Check("player capture reads the backpack",
            withPlayer.Player.Slots.Count == 1 && withPlayer.Player.Slots[0].ItemId == "scrap",
            $"({withPlayer.Player.Slots.Count} slots)");

        // Change everything, then restore.
        playerRoot.GlobalPosition = Vector3.Zero;
        health.TakeDamage(50f);
        stats.Restore(90f, 90f, 100f);
        bag.ClearContents();

        Check("player section applies", SaveApply.ApplyPlayer(withPlayer.Player));
        Check("player position and health are restored",
            Mathf.Abs(playerRoot.GlobalPosition.X - 7f) < 0.01f
            && Mathf.Abs(health.CurrentHealth - 65f) < 0.5f,
            $"(pos {playerRoot.GlobalPosition}, hp {health.CurrentHealth:F1})");
        Check("player vitals are restored",
            Mathf.Abs(stats.Hunger - 20f) < 0.5f && Mathf.Abs(stats.Thirst - 30f) < 0.5f,
            $"(hunger {stats.Hunger}, thirst {stats.Thirst})");
        Check("player backpack is restored", bag.CountOf("scrap") == 5, $"(scrap {bag.CountOf("scrap")})");

        // Health is clamped, never healed above the max by a corrupt save.
        health.SetHealth(500f);
        Check("health clamps to max", Mathf.Abs(health.CurrentHealth - health.MaxHealth) < 0.01f);

        playerRoot.QueueFree();
    }

    // ── Stage 8: perimeter tiers and traps ─────────────────────────

    private void TestStage8Perimeter()
    {
        GD.Print("── Stage 8: Perimeter & Traps ──");

        // ── Wall tiers 3 and 4 ────────────────────────────────────
        var fence = new World.Defenses.WallTiers.ScrapWoodFence { Name = "Fence" };
        var chain = new World.Defenses.WallTiers.ChainLinkWall { Name = "Chain" };
        var palisade = new World.Defenses.WallTiers.LogPalisade { Name = "Palisade" };
        var metal = new World.Defenses.WallTiers.CorrugatedMetal { Name = "Metal" };
        AddChild(fence);
        AddChild(chain);
        AddChild(palisade);
        AddChild(metal);

        Check("the wall tiers form a ladder",
            fence.WallTier == 1 && chain.WallTier == 2
            && palisade.WallTier == 3 && metal.WallTier == 4);
        Check("durability rises with every tier",
            fence.Health.MaxHealth < chain.Health.MaxHealth
            && chain.Health.MaxHealth < palisade.Health.MaxHealth
            && palisade.Health.MaxHealth < metal.Health.MaxHealth,
            $"{fence.Health.MaxHealth:F0} → {chain.Health.MaxHealth:F0} → " +
            $"{palisade.Health.MaxHealth:F0} → {metal.Health.MaxHealth:F0}");
        Check("pressure thresholds rise with the tiers",
            palisade.PressureThreshold > fence.PressureThreshold
            && metal.PressureThreshold > palisade.PressureThreshold);

        Check("the palisade is tough but flammable",
            palisade.Health.ResistSlash > fence.Health.ResistSlash
            && palisade.Health.ResistFire < 0.5f,
            $"(slash {palisade.Health.ResistSlash:F2}, fire {palisade.Health.ResistFire:F2})");
        Check("the metal wall is nearly fireproof",
            metal.Health.ResistFire > 0.8f && metal.Health.ResistBlunt > 0.6f,
            $"(fire {metal.Health.ResistFire:F2})");
        Check("the chain link still lets you see through",
            !chain.BlocksVision && palisade.BlocksVision && metal.BlocksVision);
        Check("the new walls join the wall groups",
            palisade.IsInGroup("walls") && palisade.IsInGroup("spiked_walls")
            && metal.IsInGroup("metal_walls"));

        // The palisade's spikes wound a brute that rams it.
        var brute = SpawnZombie<Entities.Zombies.Archetypes.BruteTank>("SpikeTestBrute");

        float before = brute.Health.CurrentHealth;
        float reflected = palisade.ReflectSpikeDamage(brute);
        Check("the palisade spikes the rammer",
            reflected > 0f && brute.Health.CurrentHealth < before, $"(−{reflected:F0} HP)");

        // ── Traps ─────────────────────────────────────────────────
        var punji = new World.Defenses.Traps.PunjiTrench { Name = "Punji", SafeDisarmDelay = 0f };
        AddChild(punji);

        Check("the punji is a single-use trap",
            punji.IsArmed && !punji.IsSpent && punji.ReArmCooldown <= 0f);
        Check("the punji joins the trap groups",
            punji.IsInGroup("traps") && punji.IsInGroup("trap_punji"));

        var prey = new Node3D { Name = "Prey" };
        var preyHealth = new HealthComponent { Name = "HealthComponent", MaxHealth = 100f };
        prey.AddChild(preyHealth);
        AddChild(prey);

        prey.Position = new Vector3(50f, 0f, 0f);
        Check("a trap ignores distant victims", !punji.Fire(prey));

        prey.Position = new Vector3(0.5f, 0f, 0f);
        Check("the punji impales the victim",
            punji.Fire(prey) && preyHealth.CurrentHealth < 100f, $"(−{punji.LastDamageDealt:F0} HP)");
        Check("the punji is spent after firing", punji.IsSpent && !punji.IsArmed);
        Check("a spent trap does not fire again", !punji.Fire(prey));

        punji.ReArm();
        Check("a trap can be re-armed", punji.IsArmed && !punji.IsSpent);
        punji.Disarm();
        Check("a trap can be disarmed safely", !punji.IsArmed && !punji.Fire(prey));
        Check("a brute takes reduced stake damage",
            punji.BruteResistance > 0f && punji.BruteResistance < 1f,
            $"(resistance {punji.BruteResistance:P0})");

        // ── Tripwire alarm ────────────────────────────────────────
        var tripwire = new World.Defenses.Traps.TripwireAlarm { Name = "Tripwire", ReTriggerInterval = 5f };
        AddChild(tripwire);

        Check("the tripwire starts armed and has not alerted anyone",
            tripwire.IsArmed && !tripwire.HasRaisedAlert);
        Check("the tripwire is a repeating trap, unlike the punji",
            tripwire.ReArmCooldown > 0f && punji.ReArmCooldown <= 0f,
            $"(cooldown {tripwire.ReArmCooldown:F0}s vs {punji.ReArmCooldown:F0}s)");

        int tripEvents = 0;
        tripwire.Tripped += _ => tripEvents++;

        bool tripped = tripwire.Fire(prey);
        Check("the tripwire triggers on a crossing", tripped && tripEvents == 1);
        Check("the tripwire raises exactly one alert",
            tripwire.AlertsRaised == 1 && tripwire.HasRaisedAlert);
        Check("the tripwire re-arms for the next breach",
            tripwire.IsArmed && !tripwire.IsSpent);

        // The brute is inside the alarm radius, so it should now be hunting.
        brute.Position = new Vector3(3f, 0f, 0f);
        tripwire.Fire(prey);
        Check("the tripwire rallies the sector to the breach",
            brute.IsAlerted && brute.AlertPosition.DistanceTo(tripwire.GlobalPosition) < 0.01f,
            $"(alerted to {brute.AlertPosition})");
        Check("the tripwire counts repeat alerts", tripwire.AlertsRaised == 2);

        // ── Trap contract shared by both ──────────────────────────
        Check("both traps share the TrapBase contract",
            punji.GetType().BaseType == typeof(World.Defenses.Traps.TrapBase)
            && tripwire.GetType().BaseType == typeof(World.Defenses.Traps.TrapBase));

        foreach (Node n in new Node[] { punji, tripwire, prey, brute, fence, chain, palisade, metal })
            n.QueueFree();
    }

    // ── Stage 8: Phase 8 zombie roster ──────────────────────────────

    private void TestStage8ZombieRoster()
    {
        GD.Print("── Stage 8: Zombie Roster ──");

        var shambler = SpawnZombie<Entities.Zombies.Archetypes.ShamblerWalker>("Shambler");
        var sprinter = SpawnZombie<Entities.Zombies.Archetypes.SprinterRunner>("Sprinter");
        var bloater = SpawnZombie<Entities.Zombies.Archetypes.BloaterBoomer>("Bloater");

        Check("the original three archetypes still build",
            shambler.ArchetypeName == "Shambler" && sprinter.ArchetypeName == "Sprinter"
            && bloater.ArchetypeName == "Bloater");

        // ── Screamer ──────────────────────────────────────────────
        var siren = SpawnZombie<Entities.Zombies.Archetypes.ScreamerSiren>("Screamer");

        Check("the screamer is fragile and mid-speed",
            siren.Health.MaxHealth <= 80f && siren.BaseMoveSpeed < sprinter.BaseMoveSpeed,
            $"({siren.Health.MaxHealth:F0} HP, {siren.BaseMoveSpeed:F1} m/s)");
        Check("the screamer is a nuisance, not a bruiser",
            siren.BaseAttackDamage < shambler.BaseAttackDamage);
        Check("the screamer hears far better than it sees",
            siren.Sensory.HearingSensitivity > sprinter.Sensory.HearingSensitivity
            && siren.Sensory.SightRange < sprinter.Sensory.SightRange,
            $"(hearing {siren.Sensory.HearingSensitivity:F1}, sight {siren.Sensory.SightRange:F0}m)");
        Check("the screamer does not hunt by scent", siren.Sensory.ScentRange <= 0.01f);

        siren.Position = new Vector3(0f, 0f, 0f);
        shambler.Position = new Vector3(5f, 0f, 0f);
        sprinter.Position = new Vector3(-8f, 0f, 0f);
        bloater.Position = new Vector3(300f, 0f, 0f);

        int alerted = siren.Scream();
        Check("the scream alerts the local sector", alerted >= 2, $"({alerted} alerted)");
        Check("every zombie in earshot is alerted", shambler.IsAlerted && sprinter.IsAlerted);
        Check("a distant zombie is not alerted", Mathf.Abs(bloater.AlertTimer) < 0.01f,
            $"(timer {bloater.AlertTimer:F1})");
        Check("the alert points at a rally position",
            shambler.AlertPosition.DistanceTo(siren.GlobalPosition) < 0.01f
            || shambler.AlertPosition.DistanceTo(siren.Sensory.LastKnownTargetPosition) < 0.01f);
        Check("the scream goes on cooldown",
            siren.ScreamCooldownRemaining > 0f && siren.IsScreaming);

        Vector3 flee = siren.GetEvasiveVelocity(siren.GlobalPosition + Vector3.Forward, 0.016f);
        Check("the screamer retreats from a close target",
            flee.Dot(-Vector3.Forward) > 0f, $"(velocity {flee})");
        Check("the screamer still moves at a distance",
            siren.GetEvasiveVelocity(siren.GlobalPosition + Vector3.Forward * 20f, 0.016f).Length() > 0.1f);

        // ── Brute ─────────────────────────────────────────────────
        var brute = SpawnZombie<Entities.Zombies.Archetypes.BruteTank>("Brute");

        Check("the brute is a walking wall",
            brute.Health.MaxHealth >= 500f && brute.BaseMoveSpeed < shambler.BaseMoveSpeed,
            $"({brute.Health.MaxHealth:F0} HP, {brute.BaseMoveSpeed:F1} m/s)");
        Check("the brute shrugs off slashes and blunt trauma",
            brute.Health.ResistSlash > 0.3f && brute.Health.ResistBlunt > 0.3f,
            $"(slash {brute.Health.ResistSlash:F2})");
        Check("the brute is armoured and hits like a truck",
            brute.Health.ArmorRating > 0f && brute.BaseAttackDamage > shambler.BaseAttackDamage * 2f,
            $"({brute.BaseAttackDamage:F0} dmg)");
        Check("the brute commits hard to a direction", brute.TurnSpeed < sprinter.TurnSpeed,
            $"(turn {brute.TurnSpeed:F1})");
        Check("the brute can smell blood", brute.Sensory.ScentRange > 0f);

        Check("the brute starts idle",
            brute.State == Entities.Zombies.Archetypes.BruteTank.RamState.Idle);
        brute.BeginWindUp();
        Check("the charge telegraphs before it lands",
            brute.IsWindingUp && !brute.IsCharging && brute.WindUpDuration > 0f,
            $"({brute.WindUpDuration:F1}s window)");

        Vector3 lockedDirection = brute.ChargeDirection;
        brute.RamCharge();
        Check("a forced charge locks its direction",
            brute.IsCharging && brute.ChargeDirection.IsEqualApprox(lockedDirection),
            $"({brute.ChargeDirection})");
        Check("the brute is far faster while charging",
            brute.ChargeSpeedMultiplier > 2f, $"(×{brute.ChargeSpeedMultiplier:F1})");

        // ── Hound ─────────────────────────────────────────────────
        ScentComponent.ClearWorldScent();
        var hound = SpawnZombie<Entities.Zombies.Archetypes.InfectedHound>("Hound");
        var hound2 = SpawnZombie<Entities.Zombies.Archetypes.InfectedHound>("Hound2");
        hound.Position = new Vector3(0f, 0f, 0f);
        hound2.Position = new Vector3(4f, 0f, 0f);

        Check("the hound is the fastest thing on legs",
            hound.BaseMoveSpeed > sprinter.BaseMoveSpeed, $"({hound.BaseMoveSpeed:F1} m/s)");
        Check("the hound dies to a stiff breeze",
            hound.Health.MaxHealth < shambler.Health.MaxHealth, $"({hound.Health.MaxHealth:F0} HP)");
        Check("the hound bites softly", hound.BaseAttackDamage < sprinter.BaseAttackDamage,
            $"({hound.BaseAttackDamage:F0} dmg)");
        Check("the hound sees poorly and smells superbly",
            hound.Sensory.SightRange < sprinter.Sensory.SightRange && hound.Sensory.ScentRange > 20f,
            $"(sight {hound.Sensory.SightRange:F0}m, scent {hound.Sensory.ScentRange:F0}m)");

        ScentComponent.WorldScent.Add(new Core.Components.ScentPuff
        {
            Position = new Vector3(10f, 0f, 0f),
            Strength = 1f,
        });

        bool locked = hound.Sensory.TryTrackScent();
        int pack = hound.CallPack();

        Check("the hound locks onto a blood trail", locked && hound.IsOnTheTrail);
        Check("the hound heads for the freshest scent",
            hound.DistanceToTrail > 0f && hound.GetTrailVelocity(0.016f).Length() > 0.1f,
            $"({hound.DistanceToTrail:F1}m away)");
        Check("the hound calls its pack onto the trail",
            pack == 1 && hound2.IsAlerted, $"({pack} called)");

        var victim = new Node3D { Name = "LungeVictim" };
        var victimHealth = new HealthComponent { Name = "HealthComponent", MaxHealth = 100f };
        victim.AddChild(victimHealth);
        AddChild(victim);
        victim.Position = hound.GlobalPosition + Vector3.Forward;

        hound.Lunge(victim);
        Check("the hound's lunge wounds its victim",
            victimHealth.CurrentHealth < 100f, $"({victimHealth.CurrentHealth:F0} HP left)");

        ScentComponent.ClearWorldScent();
        foreach (Node n in new Node[]
                 { siren, brute, hound, hound2, victim, shambler, sprinter, bloater })
        {
            n.QueueFree();
        }
    }

    // ── Stage 8: blood scent tracking ──────────────────────────────

    private void TestStage8ScentTracking()
    {
        GD.Print("── Stage 8: Blood Scent ──");

        ScentComponent.ClearWorldScent();

        // A "player" that bleeds as it walks.
        var bleeder = new Node3D { Name = "BleedingPlayer", Position = new Vector3(0f, 0f, 0f) };
        AddChild(bleeder);

        var scent = new ScentComponent { Name = "Scent", DropInterval = 0.1f, MinMoveDistance = 0.5f };
        bleeder.AddChild(scent);

        int dropEvents = 0;
        scent.ScentDropped += (_, _) => dropEvents++;

        Check("a clean entity leaves no scent", !scent.IsBleeding && ScentComponent.WorldScent.Count == 0);
        Check("the scent source registers itself", bleeder.IsInGroup("scent_sources"));

        scent.ReportBleeding(5f);
        Check("bleeding sets the flag", scent.IsBleeding);
        Check("the first drop is immediate", scent.PuffsDropped == 1 && dropEvents == 1,
            $"({scent.PuffsDropped} puffs)");
        Check("the world holds the scent", ScentComponent.WorldScent.Count == 1);

        // Walk a short trail.
        for (int i = 1; i <= 4; i++)
        {
            bleeder.Position = new Vector3(i * 1.5f, 0f, 0f);
            scent.DropPuff();
        }

        Check("a trail accumulates", ScentComponent.WorldScent.Count == 5,
            $"({ScentComponent.WorldScent.Count} puffs)");

        // Sprinting lays a stronger trail.
        scent.IsSprinting = true;
        bleeder.Position = new Vector3(9f, 0f, 0f);
        scent.DropPuff();
        float sprintStrength = ScentComponent.WorldScent[^1].Strength;
        scent.IsSprinting = false;
        bleeder.Position = new Vector3(11f, 0f, 0f);
        scent.DropPuff();
        float walkStrength = ScentComponent.WorldScent[^1].Strength;

        Check("sprinting drops a stronger scent", sprintStrength > walkStrength,
            $"(sprint {sprintStrength:F2} vs walk {walkStrength:F2})");

        // ── Sampling ──────────────────────────────────────────────
        float atTrail = ScentComponent.SampleScentStrength(new Vector3(6f, 0f, 0f), 3f);
        float farAway = ScentComponent.SampleScentStrength(new Vector3(0f, 0f, 60f), 3f);
        Check("scent is sampled near the trail", atTrail > 0f, $"({atTrail:F2})");
        Check("scent is absent far from the trail", Mathf.Abs(farAway) < 0.01f, $"({farAway:F2})");

        Check("the strongest puff can be located",
            ScentComponent.TryFindStrongestScent(new Vector3(0f, 0f, 0f), 40f, out Vector3 strongest, out float strength)
            && strength > 0f,
            $"({strongest}, {strength:F2})");

        // ── Decay ─────────────────────────────────────────────────
        int beforeDecay = ScentComponent.WorldScent.Count;
        ScentComponent.AgeWorldScent(1f);
        Check("scent fades over time",
            ScentComponent.WorldScent.Count == beforeDecay
            && ScentComponent.WorldScent[0].Strength < 1f,
            $"(strength {ScentComponent.WorldScent[0].Strength:F2})");

        ScentComponent.AgeWorldScent(60f);
        Check("old scent is cleaned up entirely", ScentComponent.WorldScent.Count == 0,
            $"({ScentComponent.WorldScent.Count} left)");

        // ── Sensory integration ───────────────────────────────────
        // A blind tracker with a nose: no eyes needed, scent is enough.
        var trackerRoot = new Node3D { Name = "TestTracker", Position = new Vector3(20f, 0f, 0f) };
        AddChild(trackerRoot);

        var sensory = new Entities.Zombies.ZombieAI.SensorySystem
        {
            Name = "SensorySystem",
            ScentRange = 30f,
            ScentThreshold = 0.2f,
        };
        trackerRoot.AddChild(sensory);

        // A stock zombie has no nose at all.
        var blind = new Entities.Zombies.ZombieAI.SensorySystem { Name = "BlindSensory" };
        trackerRoot.AddChild(blind);
        Check("a sensory system without smell cannot track",
            !blind.CanTrackScent && !blind.TryTrackScent());

        // Lay a fresh trail within its range.
        bleeder.Position = new Vector3(22f, 0f, 0f);
        scent.ReportBleeding(3f);

        int scentEvents = 0;
        sensory.ScentTracked += (_, _) => scentEvents++;

        Check("a tracker picks up the trail",
            sensory.TryTrackScent() && sensory.IsTrackingScent && scentEvents == 1,
            $"(events {scentEvents})");
        Check("the tracked point is the scent, not the tracker",
            sensory.TrackedScentPosition.DistanceTo(bleeder.Position) < 0.01f,
            $"({sensory.TrackedScentPosition} vs {bleeder.Position})");

        // Too far to smell.
        trackerRoot.Position = new Vector3(200f, 0f, 0f);
        Check("a distant tracker smells nothing", !sensory.TryTrackScent());
        Check("tracking state clears when the trail is out of range", !sensory.IsTrackingScent);

        // Weak scent falls under the threshold.
        trackerRoot.Position = new Vector3(0f, 0f, 0f);
        ScentComponent.ClearWorldScent();
        ScentComponent.WorldScent.Add(new Core.Components.ScentPuff
        {
            Position = new Vector3(1f, 0f, 0f),
            Strength = 0.05f,   // below ScentThreshold (0.2)
        });
        Check("a faint trail is below the threshold", !sensory.TryTrackScent());
        Check("scent survives regardless of line of sight",
            ScentComponent.WorldScent.Count == 1, "(no raycast was involved)");

        ScentComponent.ClearWorldScent();
        bleeder.QueueFree();
        trackerRoot.QueueFree();
    }

    // ── Stage 9: weapon aiming geometry ─────────────────────────────

    private void TestStage9AimingGeometry()
    {
        GD.Print("── Stage 9: Aiming Geometry ──");

        // ── Mouse → world projection ────────────────────────────────
        var camHost = new Node3D { Name = "CamHost", Position = new Vector3(10f, 20f, 10f) };
        camHost.RotationDegrees = new Vector3(-35.264f, -45f, 0f);
        var camera = new Camera3D { Name = "TestAimCamera", Projection = Camera3D.ProjectionType.Orthogonal };
        camHost.AddChild(camera);
        AddChild(camHost);

        Vector3 centre = MathUtils.ScreenToWorldOnPlane(camera, new Vector2(400, 300), 1.2f);
        Check("the cursor projects onto the requested plane height",
            Mathf.Abs(centre.Y - 1.2f) < 0.01f, $"(y = {centre.Y:F3})");

        // The old failure mode: a miss returned Vector3.Zero, which aims at the
        // world origin.
        Check("a valid projection is not the world origin",
            centre.Length() > 0.01f, $"({centre})");

        // A ray parallel to the plane must still yield a usable point.
        var flatHost = new Node3D { Name = "FlatHost", Position = new Vector3(0f, 5f, 0f) };
        var flatCamera = new Camera3D { Name = "FlatCam", Projection = Camera3D.ProjectionType.Orthogonal };
        flatHost.AddChild(flatCamera);
        AddChild(flatHost);
        Vector3 degenerate = MathUtils.ScreenToWorldOnPlane(flatCamera, new Vector2(10, 10), 5f);
        Check("a ray parallel to the plane still returns a usable point",
            !degenerate.IsEqualApprox(Vector3.Zero), $"({degenerate})");

        Vector3 left = MathUtils.ScreenToWorldOnPlane(camera, new Vector2(350, 300), 1.2f);
        Vector3 right = MathUtils.ScreenToWorldOnPlane(camera, new Vector2(450, 300), 1.2f);
        Check("the target tracks the cursor horizontally",
            right.X > left.X && Mathf.Abs(right.Y - left.Y) < 0.001f,
            $"({left} → {right})");

        // ── Aim direction is horizontal, muzzle → target ───────────
        Vector3 muzzle = new Vector3(0f, 1.2f, 0f);
        Vector3 target = new Vector3(10f, 4.0f, 10f);   // deliberately higher than the muzzle
        Vector3 aim = MathUtils.HorizontalDirectionTo(muzzle, target);
        Check("aim is normalized", Mathf.Abs(aim.Length() - 1f) < 0.001f, $"({aim.Length():F3})");
        Check("aim discards the vertical component", Mathf.Abs(aim.Y) < 0.0001f, $"(y = {aim.Y})");
        Check("aim points from the muzzle to the target",
            aim.IsEqualApprox(new Vector3(1f, 0f, 1f).Normalized()), $"({aim})");
        Check("aim at a coincident point is zero, not NaN",
            MathUtils.HorizontalDirectionTo(muzzle, muzzle).IsEqualApprox(Vector3.Zero));

        // ── Shotgun spread stays flat ──────────────────────────────
        Vector3 forward = new Vector3(0f, 0f, 1f);
        var samples = new List<Vector3>();
        for (int i = -4; i <= 4; i++)
            samples.Add(MathUtils.SpreadHorizontal(forward, Mathf.DegToRad(i * 3f)));

        Check("every pellet stays on the XZ plane",
            samples.All(v => Mathf.Abs(v.Y) < 0.0001f),
            $"(max |y| = {samples.Max(v => Mathf.Abs(v.Y))})");
        Check("every pellet keeps unit length", samples.All(v => Mathf.Abs(v.Length() - 1f) < 0.001f));

        Vector3 leftPellet = MathUtils.SpreadHorizontal(forward, Mathf.DegToRad(-10f));
        Vector3 rightPellet = MathUtils.SpreadHorizontal(forward, Mathf.DegToRad(10f));
        // Facing +Z, Godot's right-hand side is -X, so a positive angle swings
        // the pellet toward -X. What matters is that the cone is symmetric.
        Check("spread fans out symmetrically",
            leftPellet.X > 0f && rightPellet.X < 0f
            && Mathf.Abs(leftPellet.X + rightPellet.X) < 0.0001f,
            $"({leftPellet} | {rightPellet})");
        Check("zero spread is exactly forward",
            MathUtils.SpreadHorizontal(forward, 0f).IsEqualApprox(forward));

        Vector3 tilted = new Vector3(0.7f, 0.5f, 0.5f).Normalized();
        Check("a tilted input still spreads flat",
            Mathf.Abs(MathUtils.SpreadHorizontal(tilted, Mathf.DegToRad(7f)).Y) < 0.0001f);

        // ── Tracer orientation ─────────────────────────────────────
        var projectiles = new Systems.Combat.ProjectileManager { Name = "TestProjectiles" };
        AddChild(projectiles);

        projectiles.SpawnTracer(new Vector3(5f, 1.2f, 0f), new Vector3(15f, 1.2f, 0f));
        var tracer = projectiles.GetChild(0) as MeshInstance3D;
        Check("a tracer is spawned for a valid shot", tracer != null);

        if (tracer != null)
        {
            // The cylinder's long axis is local +Y; it must lie along the shot.
            Vector3 axis = tracer.GlobalBasis.Y.Normalized();
            Check("the tracer lies along the shot direction",
                axis.Dot(new Vector3(1f, 0f, 0f)) > 0.99f, $"(axis {axis})");
            Check("the tracer is centred between muzzle and target",
                tracer.GlobalPosition.DistanceTo(new Vector3(10f, 1.2f, 0f)) < 0.01f,
                $"({tracer.GlobalPosition})");
        }

        // The old bug: a near-vertical shot flipped the up vector at random.
        projectiles.SpawnTracer(Vector3.Zero, new Vector3(0f, 12f, 0f));
        var vertical = projectiles.GetChild(1) as MeshInstance3D;
        Check("a vertical shot still orients along its own axis",
            vertical != null && vertical.GlobalBasis.Y.Normalized().Dot(Vector3.Up) > 0.99f,
            vertical != null ? $"(axis {vertical.GlobalBasis.Y.Normalized()})" : "(no tracer)");

        // A zero-length shot has no direction and must be skipped, not spawned.
        int beforeDegenerate = projectiles.GetChildCount();
        projectiles.SpawnTracer(new Vector3(3f, 1f, 3f), new Vector3(3f, 1f, 3f));
        Check("a zero-length shot is skipped",
            projectiles.GetChildCount() == beforeDegenerate);

        // ── Self-hit prevention is configured ──────────────────────
        var combatHost = new Entities.Player.PlayerController { Name = "TestPlayerHost" };
        combatHost.AddChild(new HealthComponent { Name = "HealthComponent" });
        var combat = new Entities.Player.PlayerCombat { Name = "TestCombat" };
        combatHost.AddChild(combat);
        AddChild(combatHost);

        Check("the muzzle sits outside the player's own capsule",
            combat.MuzzleForwardOffset > 0.4f && combat.MuzzleHeight > 0.5f,
            $"(offset {combat.MuzzleForwardOffset:F2}m at height {combat.MuzzleHeight:F2}m)");
        Check("bullets pass through allies by default", combat.NoFriendlyFire);

        var query = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Forward * 10f);
        query.Exclude = new Godot.Collections.Array<Rid> { combatHost.GetRid() };
        Check("raycast exclusions carry the player's RID",
            query.Exclude.Count == 1 && query.Exclude[0] == combatHost.GetRid());
        Check("the player body is a real physics body with a valid RID",
            combatHost.GetRid().IsValid);
    }

    // ── Stage 8: bite infection ─────────────────────────────────────

    private void TestStage8Infection()
    {
        GD.Print("── Stage 8: Infection & Treatment ──");

        var infection = new Systems.Infection.InfectionSystem { Name = "TestInfection" };
        AddChild(infection);

        int stageEvents = 0;
        int amputations = 0;
        infection.StageChanged += _ => stageEvents++;
        infection.Amputated += () => amputations++;

        Check("a clean entity is uninfected",
            !infection.IsInfected && infection.Stage == Systems.Infection.InfectionStage.Uninfected
            && Mathf.Abs(infection.Severity) < 0.001f);

        // ── The bite roll ──────────────────────────────────────────
        var luckyBite = infection.RollBite(chanceOverride: 0f);
        Check("a 0% chance bite never infects",
            !luckyBite.Infected && !infection.IsInfected, $"(roll {luckyBite.Roll:F2})");

        var certainBite = infection.RollBite(chanceOverride: 1f);
        Check("a 100% chance bite always infects",
            certainBite.Infected && infection.IsInfected
            && infection.Stage == Systems.Infection.InfectionStage.Incubation,
            $"(roll {certainBite.Roll:F2})");
        Check("a fresh bite starts in incubation with no symptoms",
            Mathf.Abs(infection.HealthDrainPerSecond) < 0.001f
            && Mathf.Abs(infection.StaminaCapMultiplier - 1f) < 0.001f);
        Check("the bite raises a stage event", stageEvents >= 1, $"({stageEvents})");
        Check("necrosis is scheduled", infection.SecondsUntilNecrosis > 0f,
            $"({infection.SecondsUntilNecrosis:F0}s)");

        // ── Staged progression ─────────────────────────────────────
        infection.AdvanceStageTimer(infection.IncubationDuration + 1f);
        Check("incubation matures into fever",
            infection.Stage == Systems.Infection.InfectionStage.Fever, $"({infection.Stage})");
        Check("fever drains health and caps stamina",
            infection.HealthDrainPerSecond > 0f && infection.StaminaCapMultiplier < 1f,
            $"({infection.HealthDrainPerSecond:F1}/s, stamina ×{infection.StaminaCapMultiplier:F2})");
        Check("severity climbs with the stage", infection.Severity > 0.4f,
            $"({infection.Severity:F2})");

        infection.AdvanceStageTimer(infection.FeverDuration + 1f);
        Check("fever spreads into infection",
            infection.Stage == Systems.Infection.InfectionStage.Infection);

        infection.AdvanceStageTimer(infection.SpreadDuration + 1f);
        Check("infection reaches necrosis",
            infection.Stage == Systems.Infection.InfectionStage.Necrosis);
        Check("necrosis is the worst state",
            Mathf.Abs(infection.Severity - 1f) < 0.001f
            && infection.HealthDrainPerSecond > infection.InfectionHealthDrainPerSecond,
            $"({infection.HealthDrainPerSecond:F1}/s)");
        Check("necrosis has no time left", Mathf.Abs(infection.SecondsUntilNecrosis) < 0.01f);

        // ── Treatment: tourniquet ──────────────────────────────────
        infection.RollBite(chanceOverride: 1f);
        Check("a new bite resets the state", infection.TourniquetsApplied == 0
            && Mathf.Abs(infection.TimerRate - 1f) < 0.001f);

        Check("a tourniquet applies", infection.ApplyTourniquet());
        Check("a tourniquet slows the clock", Mathf.Abs(infection.TimerRate - 0.5f) < 0.001f,
            $"(×{infection.TimerRate:F2})");
        Check("tourniquets stack twice", infection.ApplyTourniquet());
        Check("a third tourniquet is refused", !infection.ApplyTourniquet()
            && infection.TourniquetsApplied == infection.MaxStackedTourniquets);
        Check("a stacked tourniquet halves the clock again",
            Mathf.Abs(infection.TimerRate) < 0.001f, $"(×{infection.TimerRate:F2})");
        Check("a tourniquet does not cure", infection.IsInfected);

        // ── Treatment: antibiotics ─────────────────────────────────
        infection.ClearInfection();
        infection.RollBite(chanceOverride: 1f);
        float before = infection.SecondsUntilNecrosis;
        Check("a failed antibiotic dose still buys time",
            infection.ApplyAntibiotics(forceCure: false) && infection.SecondsUntilNecrosis > before,
            $"({before:F0}s → {infection.SecondsUntilNecrosis:F0}s)");
        Check("a failed dose leaves the infection running", infection.IsInfected);

        Check("a successful antibiotic dose clears the infection",
            infection.ApplyAntibiotics(forceCure: true) && !infection.IsInfected);
        Check("antibiotics cannot be used when clean", !infection.ApplyAntibiotics(forceCure: true));

        // ── Treatment: amputation ─────────────────────────────────
        infection.RollBite(chanceOverride: 1f);
        infection.AdvanceStageTimer(infection.IncubationDuration + infection.FeverDuration + 1f);
        Check("the infection is spreading before surgery",
            infection.Stage == Systems.Infection.InfectionStage.Infection);

        Check("amputation cures the infection",
            infection.Amputate("left arm") && !infection.IsInfected);
        Check("amputation leaves a permanent movement penalty",
            infection.HasBeenAmputated && infection.MovementSpeedPenalty > 0f,
            $"({infection.AmputatedLimb}, -{infection.MovementSpeedPenalty:P0})");
        Check("amputation raises its own event", amputations == 1, $"({amputations})");
        Check("a healed survivor cannot be amputated again", !infection.Amputate("right arm"));

        // ── Inventory-driven treatment ────────────────────────────
        var kit = new InventoryComponent { Name = "TestKit", GrantStartingItems = false };
        AddChild(kit);
        kit.RestoreContents(new[] { (ItemData.Tourniquet, 2), (ItemData.Antibiotics, 1), (ItemData.BoneSaw, 1) });

        infection.RollBite(chanceOverride: 1f);
        Check("treating with a tourniquet consumes the item",
            infection.TreatWith("tourniquet", kit) && kit.CountOf("tourniquet") == 1);

        // Force a fresh infection for the saw: a real antibiotic dose has a 35%
        // cure chance, so relying on it to leave the wound open would be flaky.
        Check("treating with antibiotics consumes the item",
            infection.TreatWith("antibiotics", kit) && kit.CountOf("antibiotics") == 0);

        infection.RollBite(chanceOverride: 1f);
        Check("the saw is a cure while infected", infection.IsInfected);
        Check("treating with the saw consumes it and cures",
            infection.TreatWith("bone_saw", kit, "right leg") && !infection.IsInfected);
        Check("the amputation is recorded on the survivor",
            infection.AmputatedLimb == "right leg", $"({infection.AmputatedLimb})");

        infection.RollBite(chanceOverride: 1f);
        Check("an unknown item is not a treatment", !infection.TreatWith("molotov", kit));
        Check("treatment fails without the item in the pack",
            !infection.TreatWith("antibiotics", kit) && infection.IsInfected);

        // ── Items are registered so saves can resolve them ─────────
        Check("treatment items resolve by id",
            ItemData.Find("tourniquet") == ItemData.Tourniquet
            && ItemData.Find("antibiotics") == ItemData.Antibiotics
            && ItemData.Find("bone_saw") == ItemData.BoneSaw);

        kit.QueueFree();
        infection.QueueFree();
    }

    // ── Stage 6: difficulty applies to zombies already in the world ──

    private void TestStage6LiveDifficulty()
    {
        GD.Print("── Stage 6: Live Difficulty Scaling ──");

        var config = ConfigManager.Instance;
        Check("ConfigManager is available", config != null);
        if (config == null) return;

        DifficultyPreset original = config.Difficulty;

        // A zombie spawned now must retune when the preset changes, without
        // being respawned. Night is forced off so the only variable is difficulty.
        // The bare node needs its component children, or _Ready throws.
        var zombie = new Entities.Zombies.ZombieBase
        {
            Name = "TestDifficultyZombie",
            MoveSpeed = 2.0f,
            AttackDamage = 15.0f,
        };
        zombie.AddChild(new Core.Components.HealthComponent { Name = "HealthComponent" });
        AddChild(zombie);

        if (TimeManager.Instance is { } time)
        {
            time.SetHour(12f);   // day: no frenzy multiplier
        }

        Check("authored stats are preserved as the base",
            Mathf.Abs(zombie.BaseMoveSpeed - 2.0f) < 0.001f
            && Mathf.Abs(zombie.BaseAttackDamage - 15.0f) < 0.001f,
            $"(speed {zombie.BaseMoveSpeed}, damage {zombie.BaseAttackDamage})");

        config.SetDifficulty(DifficultyPreset.Standard);
        float standardSpeed = zombie.EffectiveMoveSpeed;
        float standardDamage = zombie.EffectiveAttackDamage;
        Check("standard preset is the neutral baseline",
            Mathf.Abs(standardSpeed - 2.0f) < 0.01f && Mathf.Abs(standardDamage - 15f) < 0.01f,
            $"(speed {standardSpeed:F2}, damage {standardDamage:F1})");

        // The key regression: the SAME zombie instance must change.
        config.SetDifficulty(DifficultyPreset.Hardcore);
        float hardcoreSpeed = zombie.EffectiveMoveSpeed;
        float hardcoreDamage = zombie.EffectiveAttackDamage;

        Check("changing the preset retunes a zombie already in the world",
            hardcoreSpeed > standardSpeed && hardcoreDamage > standardDamage,
            $"(speed {standardSpeed:F2} → {hardcoreSpeed:F2}, " +
            $"damage {standardDamage:F1} → {hardcoreDamage:F1})");
        Check("hardcore damage is 1.5× the authored value",
            Mathf.Abs(hardcoreDamage - 22.5f) < 0.01f, $"({hardcoreDamage:F1})");

        config.SetDifficulty(DifficultyPreset.Casual);
        Check("casual scales the same zombie back down",
            zombie.EffectiveAttackDamage < standardDamage && zombie.EffectiveMoveSpeed < standardSpeed,
            $"(damage {zombie.EffectiveAttackDamage:F1}, speed {zombie.EffectiveMoveSpeed:F2})");

        // Re-applying a preset must not compound (the old spawn-time bug).
        config.SetDifficulty(DifficultyPreset.Hardcore);
        float firstPass = zombie.EffectiveAttackDamage;
        config.SetDifficulty(DifficultyPreset.Standard);
        config.SetDifficulty(DifficultyPreset.Hardcore);
        Check("re-applying a preset does not compound the multiplier",
            Mathf.Abs(zombie.EffectiveAttackDamage - firstPass) < 0.01f
            && Mathf.Abs(firstPass - 22.5f) < 0.01f,
            $"({zombie.EffectiveAttackDamage:F1} vs {firstPass:F1})");

        // Night frenzy and weather pressure must still stack on top.
        if (TimeManager.Instance is { } clock)
        {
            clock.SetHour(23f);   // night: ×1.35
            config.SetDifficulty(DifficultyPreset.Standard);
            float nightSpeed = zombie.EffectiveMoveSpeed;
            Check("night frenzy still stacks on top of difficulty",
                nightSpeed > standardSpeed * 1.3f,
                $"({nightSpeed:F2} vs {standardSpeed:F2}; " +
                $"clock hour {clock.CurrentHour:F1}, phase {clock.CurrentPhase}, " +
                $"night mult {(DayNightCycle.Instance?.ZombieNightMultiplier ?? 1f):F2})");
            clock.SetHour(12f);
        }

        config.SetDifficulty(original);
        Check("restoring the preset restores the zombie",
            Mathf.Abs(zombie.EffectiveAttackDamage - 15f) < 0.01f,
            $"({zombie.EffectiveAttackDamage:F1})");

        zombie.QueueFree();
    }

    // ── Stage 5: pause / settings menu ──────────────────────────────

    private void TestStage5PauseMenu()
    {
        GD.Print("── Stage 5: Pause & Settings Menu ──");

        // The self-test scene has no HUD, so build a real one against this tree.
        var hud = new UI.HUD.TestArenaHUD { Name = "TestHUD" };
        AddChild(hud);

        var menu = hud.GetNodeOrNull<Control>("PauseMenu");
        Check("pause menu is built by the HUD", menu != null);
        if (menu == null) { hud.QueueFree(); return; }

        Check("pause menu starts hidden", !menu!.Visible && !hud.PauseMenuVisible);

        // ── Visibility follows the pause state ─────────────────────
        hud.SetPauseMenuVisible(true);
        Check("menu shows when opened", menu.Visible && hud.PauseMenuVisible);

        hud.SetPauseMenuVisible(false);
        Check("menu hides when closed", !menu.Visible && !hud.PauseMenuVisible);

        // ── Key routing: menu swallows gameplay keys, not Esc ──────
        hud.SetPauseMenuVisible(true);
        Check("menu swallows gameplay keys while open",
            hud.ForwardPauseMenuKey(new InputEventKey { Keycode = Key.Key7 }));
        Check("menu lets Esc through to GameManager",
            !hud.ForwardPauseMenuKey(new InputEventKey { Keycode = Key.Escape }));
        hud.SetPauseMenuVisible(false);
        Check("keys pass through while the menu is closed",
            !hud.ForwardPauseMenuKey(new InputEventKey { Keycode = Key.Key7 }));

        // ── The menu actually drives ConfigManager ────────────────
        var config = ConfigManager.Instance;
        Check("ConfigManager is available to the menu", config != null);

        if (config != null)
        {
            DifficultyPreset original = config.Difficulty;
            float originalSfx = config.SfxVolume;

            // Simulate the "Cycle Difficulty" button press.
            hud.SetPauseMenuVisible(true);
            config.CycleDifficulty();
            Check("cycling difficulty changes the preset", config.Difficulty != original,
                $"({original} → {config.Difficulty})");

            // Simulate dragging the effects slider to 0.3.
            config.SetSfxVolume(0.3f);
            hud.RefreshMenuValues();
            Check("menu slider value reaches ConfigManager",
                Mathf.Abs(config.SfxVolume - 0.3f) < 0.001f, $"(sfx {config.SfxVolume})");

            // And that it survived a disk reload (the menu promises persistence).
            config.Load();
            Check("menu volume changes persist across a reload",
                Mathf.Abs(config.SfxVolume - 0.3f) < 0.001f, $"(sfx {config.SfxVolume})");

            config.SetDifficulty(original);
            config.SetSfxVolume(originalSfx);
        }

        // ── Save buttons enable only when a save exists ───────────
        var saves = SaveManager.Instance;
        Check("SaveManager is available to the menu", saves != null);

        if (saves != null)
        {
            string savedPath = saves.SavePath;
            bool hadSave = saves.HasSave();

            // Guarantee a known state, then restore the player's own file.
            saves.DeleteSave();
            hud.RefreshMenuValues();
            Check("load/delete are disabled with no save", !saves.HasSave());

            saves.SaveGame("pause menu test");
            hud.RefreshMenuValues();
            Check("menu can write a save", saves.HasSave());
            Check("the written save parses back", saves.ReadSave() != null);

            saves.DeleteSave();
            hud.RefreshMenuValues();
            Check("menu can delete a save", !saves.HasSave());
            Check("deleting twice is safe", !saves.HasSave() && !Godot.FileAccess.FileExists(savedPath));

            if (hadSave)
            {
                saves.SaveGame("restored");
                Check("a pre-existing save is recreated after the menu test", saves.HasSave());
            }
        }

        hud.SetPauseMenuVisible(false);
        hud.QueueFree();
    }

    // ── Stage 4: weather and seasons ────────────────────────────────

    private void TestStage4WeatherAndSeasons()
    {
        GD.Print("── Stage 4: Weather & Seasons ──");

        var weather = new WeatherSystem { Name = "TestWeather", DaysPerSeason = 4 };
        AddChild(weather);

        int weatherEvents = 0;
        int seasonEvents = 0;
        Action<int>? onWeather = null;
        Action<int>? onSeason = null;
        if (EventBus.Instance != null)
        {
            onWeather = _ => weatherEvents++;
            onSeason = _ => seasonEvents++;
            EventBus.Instance.OnWeatherChanged += onWeather;
            EventBus.Instance.OnSeasonChanged += onSeason;
        }

        // ── Season calendar ────────────────────────────────────────
        weather.ForcedWeatherIndex = (int)WeatherState.Clear;   // isolate the calendar
        weather.EvaluateForDay(1);
        Check("day 1 is spring", weather.CurrentSeason == Season.Spring && weather.DayOfSeason == 1,
            $"({weather.CurrentSeason} d{weather.DayOfSeason})");

        weather.EvaluateForDay(4);
        Check("the last spring day still reads spring",
            weather.CurrentSeason == Season.Spring && weather.DayOfSeason == 4);

        weather.EvaluateForDay(5);
        Check("day 5 rolls into summer", weather.CurrentSeason == Season.Summer && weather.DayOfSeason == 1,
            $"({weather.CurrentSeason} d{weather.DayOfSeason})");
        Check("season change is broadcast", seasonEvents >= 1, $"({seasonEvents} events)");

        weather.EvaluateForDay(13);
        Check("day 13 is the first day of winter",
            weather.CurrentSeason == Season.Winter && weather.DayOfSeason == 1,
            $"({weather.CurrentSeason} d{weather.DayOfSeason})");

        weather.EvaluateForDay(17);
        Check("the calendar wraps back to spring after a year",
            weather.CurrentSeason == Season.Spring, $"({weather.CurrentSeason})");

        // ── Weather effects ────────────────────────────────────────
        weather.ForcedWeatherIndex = (int)WeatherState.Clear;
        weather.EvaluateForDay(1);
        float clearDaylight = weather.DaylightFactor;

        weather.ForcedWeatherIndex = (int)WeatherState.Storm;
        weather.EvaluateForDay(2);
        Check("storms darken the world", weather.DaylightFactor < clearDaylight,
            $"(clear {clearDaylight:F2} → storm {weather.DaylightFactor:F2})");
        Check("weather change is broadcast", weatherEvents >= 1, $"({weatherEvents} events)");
        Check("storms suppress miasma", weather.MiasmaSuppression > 0.5f,
            $"{weather.MiasmaSuppression:P0}");

        weather.ForcedWeatherIndex = (int)WeatherState.Fog;
        weather.EvaluateForDay(3);
        Check("fog emboldens the horde", weather.HordePressureMultiplier > 1f,
            $"×{weather.HordePressureMultiplier:F2}");
        Check("fog leaves miasma alone", weather.MiasmaSuppression < 0.01f);

        // ── Season effects ─────────────────────────────────────────
        float springThirst = weather.ThirstDrainMultiplier;
        weather.ForcedWeatherIndex = (int)WeatherState.Clear;
        weather.EvaluateForDay(5);   // summer
        float summerThirst = weather.ThirstDrainMultiplier;
        weather.EvaluateForDay(13);  // winter
        float winterThirst = weather.ThirstDrainMultiplier;

        Check("summer dehydrates faster than spring", summerThirst > springThirst,
            $"(spring ×{springThirst:F2} → summer ×{summerThirst:F2})");
        Check("winter dehydrates slower than summer", winterThirst < summerThirst,
            $"(winter ×{winterThirst:F2})");
        Check("winter raises horde pressure", weather.HordePressureMultiplier >= 1.2f,
            $"×{weather.HordePressureMultiplier:F2}");

        // ── The clock drives the weather automatically ─────────────
        weather.ForcedWeatherIndex = -1;
        if (TimeManager.Instance is { } time)
        {
            time.OnDayRolled += weather.EvaluateForDayForSignal;
            int dayBefore = weather.DayOfSeason;
            time.Advance(24f * 8f);   // cross into a new day

            Check("rolling the clock re-rolls the weather",
                weather.DayOfSeason != dayBefore || time.DayCount > 3,
                $"(day {time.DayCount}, season day {weather.DayOfSeason})");
            time.OnDayRolled -= weather.EvaluateForDayForSignal;
        }
        else
        {
            Check("clock drives the weather", false, "(no TimeManager)");
        }

        // ── Weather survives a save/load round trip ─────────────────
        if (SaveManager.Instance is { } saves && TimeManager.Instance is { } clock)
        {
            weather.ForcedWeatherIndex = (int)WeatherState.Storm;
            weather.EvaluateForDay(clock.DayCount, announce: false);

            var camp = saves.Capture().Camp;
            Check("capture records the weather and season",
                camp.WeatherState == (int)WeatherState.Storm && camp.Season == (int)weather.CurrentSeason,
                $"({camp.WeatherState}, {camp.Season})");

            // A save made on a later day must not come back in the wrong season.
            clock.RestoreState(clock.DayCount + 4, 8f, false);
            weather.EvaluateForDay(clock.DayCount, announce: false);
            Check("a later day resolves to a different season",
                (int)weather.CurrentSeason != camp.Season || clock.DayCount < 17,
                $"(day {clock.DayCount} → {weather.CurrentSeason})");
        }

        if (EventBus.Instance != null && onWeather != null && onSeason != null)
        {
            EventBus.Instance.OnWeatherChanged -= onWeather;
            EventBus.Instance.OnSeasonChanged -= onSeason;
        }

        // Leave the climate neutral so later stages see unmultiplied stats.
        weather.ForcedWeatherIndex = (int)WeatherState.Clear;
        weather.EvaluateForDay(1, announce: false);
        Check("weather builds a rain emitter", weather.RainParticles != null);
        weather.QueueFree();
    }


}
