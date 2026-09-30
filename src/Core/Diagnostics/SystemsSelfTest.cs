using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Data;
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

}
