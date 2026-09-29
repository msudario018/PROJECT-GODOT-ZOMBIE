# Project Zombie Post-Apocalyptic

[![Engine](https://img.shields.io/badge/Godot-4.7_Forward%2B-blue.svg)](https://godotengine.org/)
[![Framework](https://img.shields.io/badge/.NET-8.0_C%23-purple.svg)](https://dotnet.microsoft.com/)
[![Physics](https://img.shields.io/badge/Physics-Jolt_3D-green.svg)](https://github.com/godot-jolt/godot-jolt)
[![Status](https://img.shields.io/badge/Status-Phase_7_Complete-success.svg)](#current-progress--phase-status)

A hardcore, mechanically rich post-apocalyptic zombie survival sandbox built in **Godot 4.7 (.NET / C#)** with true 2.5D isometric perspective, Jolt Physics, and Forward+ D3D12 rendering.

Every system—from structural physics and horde flow-fields to acoustic propagation, ballistics, and off-grid electrical networks—is engineered as an interconnected, event-driven simulation.

---

## 🎮 Current Controls

| Key / Input | Action | Mechanical Effect |
| :--- | :--- | :--- |
| **`WASD`** | Move (Isometric) | Smooth acceleration/friction physics aligned with camera diamond grid. |
| **`Shift`** | Sprint | Increases speed by $1.6\times$; emits louder $12\text{ m}$ footstep acoustic radius. |
| **`4`** | Equip Crowbar | Slot 1: Melee weapon ($35\text{ HP}$ blunt damage, $2.4\text{ m}$ reach, quiet $6\text{ m}$ noise radius). |
| **`5`** | Equip M9 Pistol | Slot 2: Ranged 9mm handgun ($42\text{ HP}$ ballistic damage, 15-round mag, $45\text{ m}$ gunshot acoustic signature). |
| **`6`** | Equip Shotgun | Slot 3: Remington 870 ($8\text{ pellets} \times 13 = 104\text{ HP}$ max, $12^\circ$ spread, $65\text{ m}$ massive gunshot noise). |
| **`Q`** | Cycle Weapon | Cycles sequentially through equipped melee and ranged arsenal. |
| **`LMB`** | Attack / Fire | Performs melee swing or fires ranged weapon toward mouse cursor. |
| **`R`** | Reload / Rotate | Reloads equipped firearm (or rotates structure preview if building mode active). |
| **`F`** | Flashlight | Toggles $52^\circ$ wide, $25\text{ m}$ piercing beam & SpotLight3D with ground puddle (consumes battery). |
| **`E`** | Interact | Opens or closes doors, searches loot containers, scavenges/buries/burns corpses, and refuels the generator from a carried fuel canister. |
| **`7`** | Eat Canned Food | Consumes 1× Canned Food for $+35$ Hunger. |
| **`8`** | Drink Water | Consumes 1× Water Bottle for $+40$ Thirst. |
| **`9`** | Apply Bandage | Consumes 1× Bandage for $+20\text{ HP}$. |
| **`C`** | Craft Bandage | $2\times$ Cloth Strip → $2\times$ Bandage. |
| **`V`** | Craft First Aid Kit | $3\times$ Cloth + $1\times$ Rope → $1\times$ First Aid Kit. |
| **`B`** | Craft Ammo Box | $4\times$ Scrap Metal → $2\times$ 9mm ammo packs (each pack is a full magazine when reloading). |
| **`X`** | Sever / Repair Cable | Debug: cuts or reconnects the nearest power cable, instantly splitting or merging grid islands. |
| **`G`** | Generator On/Off | Debug: starts or stops the combustion generator to test battery buffering and load shedding. |
| **`H`** | Spawn Raid | Debug: deploys a 3-raider bandit squad near the player (Advance/Suppress/Flank roles). |
| **`J`** | Trigger Airdrop | Debug: drops a military supply cache contested by a 2-raider squad. |
| **`K`** | Cycle Survivor Task | Debug: rotates a camp survivor between Idle/Defend/Scavenge/Repair/Refuel/Heal. |
| **`L`** | Spawn Survivor | Debug: recruits another survivor (CombatMedic / CombatVeteran / ScavengerScout). |
| **`1`** | Build Scrap Fence | Selects Tier 1 Scrap Wood Fence ($250\text{ HP}$, opaque, flammable). |
| **`2`** | Build Chain Link Wall | Selects Tier 2 Chain Link Wall ($500\text{ HP}$, see-through steel wire mesh). |
| **`3`** | Build Reinforced Door | Selects interactive wooden doorway with swinging leaf and locking support. |
| **`RMB / Esc`** | Cancel Build | Cancels active building placement mode. |
| **`F1`** | Toggle Fog-of-War | Instantly toggles FoW overlay ON / OFF for debugging and scene inspection. |

---

## 🚀 Current Progress & Phase Status

### ✅ Phase 1: Foundation — COMPLETED
- **Engine Scaffolding:** Configured Godot 4.7 .NET 8.0 project with Jolt Physics and Forward+ rendering.
- **Global EventBus:** Decoupled event hub (`EventBus.cs`) for acoustic signals, damage events, structural alerts, and navigation invalidation.
- **Hierarchical State Machine:** Modular FSM framework (`StateMachine.cs`, `State.cs`) for entity behaviors.
- **Spatial Partitioning:** Uniform sparse `SpatialGrid.cs` for $O(1)$ broad-phase queries (density, acoustic, proximity).
- **True Isometric Camera:** Orthographic `Camera3D` locked at mathematically exact pitch ($-35.264^\circ$) and yaw ($-45^\circ$) with ground-focus trigonometry.
- **Field of View & Fog-of-War:** 3-state visibility model (Hidden, Explored, Visible) driven by GPU shader (`fog_of_war.gdshader`) with raycasted line-of-sight occlusion on collision Layer 4.
- **Component System:** Reusable `HealthComponent.cs` with flat armor reduction, type resistances, and death broadcasts.

### ✅ Phase 2: Zombies & Navigation — COMPLETED
- **Flow-Field Mass Horde Navigation:** `FlowFieldNavigator.cs` precomputes a Dijkstra direction field across the world grid for hundreds of swarming zombies in $O(1)$ time without per-agent A* bottlenecks.
- **Acoustic Simulation:** `AudioEmitterComponent.cs` broadcasts sound events (walk, sprint, melee swing, gunshot, zombie voice) attenuated by `AcousticPropagation.cs` based on weather and indoor geometry.
- **AI Sensory Perception:** `SensorySystem.cs` simulates a $110^\circ$ vision cone ($14\text{ m}$), line-of-sight raycasts through obstacles, hearing, and close personal space ($2.5\text{ m}$) proximity awareness.
- **Hierarchical Zombie FSM:** 7 distinct behavioral states: `Idle` $\to$ `Alert` (investigating acoustic coords) $\to$ `Chase` (two-tier flow-field or `NavigationAgent3D`) $\to$ `Attack` (windup, bite/slash damage, cooldown) $\to$ `Hurt` (stagger) $\to$ `Dead` (ragdoll sinking).
- **Shambler / Walker Archetype:** Implemented master spec archetype: $80\text{ HP}$, $1.8\text{ m/s}$ dragging gait, $12\text{ damage}$, $1.3\text{ m}$ attack reach, forms dense hordes.
- **Melee Combat:** Mouse-directed melee swing with visual slash sweep, knockback impulse, and acoustic alert emission.

### ✅ Phase 3: Base Building & Dynamic NavObstacles — COMPLETED
- **Grid-Snapped Construction:** `BuildingSystem.cs` with $2\text{ m}$ grid snapping and real-time collision validation.
- **Visual Placement Ghost:** `BuildingGhost.cs` provides translucent preview (green = valid, red = obstructed).
- **Dynamic NavObstacles:** `WallBase.cs` automatically registers a `NavigationObstacle3D` on placement and deregisters on destruction, firing `EventBus.OnFlowFieldInvalidated` to update zombie paths instantly without thread-locking NavigationRegion rebakes.
- **Tier 1 & Tier 2 Walls:**
  - **Tier 1 (Scrap Wood Fence):** $250\text{ HP}$, opaque vision blocker, susceptible to fire.
  - **Tier 2 (Chain Link Wall):** $500\text{ HP}$, durable wire mesh, see-through (transparent to line of sight and FoW).
- **Interactive Doorways:** `DoorBase.cs` allows opening/closing with **E**. Toggles physical collision, vision occlusion, and dynamic nav obstacles so zombies and players can pass when open, but are blocked when closed.
- **Structural Integrity & Barricade Degradation:** Zombie horde pressure accumulation:
  $$\text{Accumulated Pressure} = \sum \text{force} \cdot (1 + 0.05 \cdot N)$$
  Exceeding `PressureThreshold` delivers structural burst damage to walls until collapse.

### ✅ Phase 4: Combat & Visibility Depth — COMPLETED
- **Multi-Weapon Combat System (`PlayerCombat.cs` & `WeaponData.cs`):**
  - **Rusty Crowbar (Melee):** $35\text{ HP}$ blunt damage, $2.4\text{ m}$ range, $7.0\text{ m/s}$ knockback, $6\text{ m}$ silent acoustic radius.
  - **M9 Service Pistol (9mm Ballistic):** $42\text{ HP}$ per round, 15-round magazine, 45 reserve, $1.6\text{ s}$ reload, $45\text{ m}$ acoustic gunshot footprint.
  - **Remington 870 Shotgun (12G Ballistic):** 8 pellets $\times 13\text{ HP}$ ($104\text{ HP}$ point-blank maximum), $12^\circ$ spread cone, 6-round tube, 24 reserve, $65\text{ m}$ gunshot acoustic signature.
- **Ballistics & Visual FX (`ProjectileManager.cs`):** High-speed visual bullet tracer rendering via cylinder mesh beam tweening and procedural surface impact sparks.
- **Sprinter / Runner Zombie Archetype (`SprinterRunner.cs`, `Sprinter.tscn`):**
  - Fast, erratic sprint speed ($4.8\text{ m/s}$).
  - Fragile durability ($60\text{ HP}$), aggressive sensory sight cone ($22\text{ m}$, $120^\circ$).
  - Rapid $18\text{ damage}$ strikes with $1.0\text{ s}$ cooldown; immediately homes in on gunshots and footsteps.
- **Bloater / Boomer Zombie Archetype (`BloaterBoomer.cs`, `Bloater.tscn`):**
  - High durability ($160\text{ HP}$), slow gait ($1.2\text{ m/s}$), heavy structural push ($2.5\text{ force}$).
  - Suicide detonation fuse triggering when within $1.8\text{ m}$ proximity of targets or upon death.
  - Generates a toxic explosive blast ($50\text{ damage}$, $4.5\text{ m}$ AoE) that shatters nearby walls, players, and fellow zombies.
  - Emits an extreme $65\text{ m}$ acoustic explosion shockwave alerting the surrounding area.
- **Dynamic Flashlight & Battery Simulation (`FieldOfView.cs` & `SpotLight3D`):**
  - Real-time battery drain ($2\%/\text{s}$ while active) with automatic shutoff at $0\%$ and passive trickle recharge ($0.5\%/\text{s}$).
  - Rotates both the 3D dynamic `SpotLight3D` and the narrow line-of-sight FoW cone toward the mouse aim vector.
- **Comprehensive Debug HUD (`TestArenaHUD.cs`):** Real-time monitoring of HP, equipped weapon, current magazine / reserve ammunition, reload completion progress, flashlight battery percentage, active zombie counts, and acoustic event log.

### ✅ Phase 5: Survival & Economy — COMPLETED
- **Slot-Based Inventory (`InventoryComponent.cs`, `ItemData.cs`):** 24 slots with per-item stack limits, live weight tracking in kg, and an encumbrance penalty ($-25\%$ move speed) once the weight limit is exceeded.
- **Item Registry & Consumables (`ItemData.cs`):** bandages ($+20\text{ HP}$), first aid kits ($+60\text{ HP}$), canned food ($+35\text{ Hunger}$), water ($+40\text{ Thirst}$), crafting materials (cloth, scrap, planks, nails, rope), ammo packs, shovel and fuel canisters.
- **Survival Needs (`PlayerStats.cs`):** hunger, thirst and stamina simulation with starvation/dehydration HP drain and sprint lockout when stamina hits $0$.
- **Crafting (`CraftingSystem.cs`):** five starter recipes — bandage, rope, improvised first aid kit, 9mm ammo box and molotov cocktail — with ingredient validation and consumption.
- **Searchable Loot Containers (`LootContainer.cs`):** medical cabinet, ammo cache, food locker, toolbox and general junk archetypes with weighted loot tables and a timed $1.5\text{ s}$ search.
- **Corpse Lifecycle & Miasma (`Corpse.cs`, `CorpseManager.cs`):** Fresh → Bloated → Rotting Miasma → Skeleton decay with a $3.5\text{ HP/s}$ toxic aura, plus scavenge, bury (needs shovel) and burn (needs fuel) disposal.
- **Day/Night Cycle (`DayNightCycle.cs`):** 24-hour cycle driving sun angle/energy, ambient light, and night-frenzy multipliers ($\times 1.35$ zombie speed, sight range and hearing).

### ✅ Phase 6: Power Grid & Islanding — COMPLETED
- **BFS Islanding (`PowerGrid.cs`, `PowerIsland.cs`):** the grid flood-fills connected nodes into islands whenever the topology changes — a severed cable instantly creates independent sub-grids that each balance their own generation, storage and load.
- **Combustion Generator (`CombustionGenerator.cs`):** $1200\text{ W}$ DC source that burns fuel canisters over time and emits a $55\text{ m}$ mechanical hum dragging the horde toward the base.
- **Battery Bank & Inverter (`BatteryBank.cs`, `Inverter.cs`):** $1200\text{ Wh}$ buffer with depth-of-discharge degradation, behind a DC→AC inverter gate ($92\%$ efficiency) that AC-only appliances depend on.
- **Wiring Cables (`WiringSegment.cs`):** long-distance connections that can be severed or repaired, firing an immediate BFS island rebuild.
- **Powered Loads:** searchlight ($150\text{ W}$, auto-on at dusk), electric fence ($320\text{ W}$ shock field that kills zombies but crackles loudly) and freezer ($200\text{ W}$, halts corpse decay so miasma never forms near the base).
- **Priority Load Shedding:** Critical → Defensive → Utility tiers with battery buffering, deficit detection and `OnGridOverload` / `OnGridIslanded` / `OnIslandReconnected` broadcasts.

### ✅ Phase 7: NPCs & Hostile Bandits — COMPLETED
- **Survivor AI (`SurvivorBase.cs`, `SurvivorAI.cs`):** priority task loop — **Defend** (melee threats near the camp anchor) → **Heal** (injured ally or player) → **Scavenge** (unsearched containers) — plus **Repair** (damaged walls), **RefuelPower** (generator fuel runs) and **Idle** wander. NavigationAgent3D pathing with direct-steering fallback.
- **Survivor Archetypes (`SurvivorArchetypes.cs`):** CombatMedic (×1.5 heal, 90 HP), CombatVeteran (140 HP, +2 armor, 22 melee) and ScavengerScout (4.4 m/s, 0.6× search time, 80 HP).
- **Camp Morale (`MoraleSystem.cs`):** 0–100 morale driven by starvation, miasma zones, power status, survivor losses, bandit kills and burials; broadcasts `OnCampMoraleChanged` and modulates survivor task speed (×0.8 below 30, ×1.1 above 75).
- **Bandit Squads (`BanditBase.cs`, `BanditSquad.cs`):** tiered raiders (Scavenger retreats at 50% HP, Militia at 30%, Warlord never retreats) with squad roles — leader **Advances**, others **Suppress** (hold ground, ranged) or **Flank** (6 m side-step) — squad morale collapse triggers mass retreat, and losing the leader drops morale by 45%.
- **Bandit Spawner (`BanditSpawner.cs`):** timed or debug-triggered raids spawning on a ring around the player; `OnBanditRaidIncoming` / `OnBanditSquadEliminated` broadcasts.
- **Airdrop Contests (`AirdropEvent.cs`):** fixed-manifest military cache (medkits, ammo, food, cloth) reserved from survivor scavengers but lootable by the player and contested by a spawned raider squad.
- **NPC Corpses:** dead survivors and bandits now feed the Phase 5 corpse-rot/miasma pipeline through `CorpseManager`.
- **HUD:** camp morale bar, survivor/bandit counters, airdrop banner, plus `[H]`/`[J]`/`[K]`/`[L]` debug controls.


---


---

## 🗺️ Architectural Roadmap

| Phase | Milestone | Status | Key Features |
| :--- | :--- | :--- | :--- |
| **Phase 1** | Foundation | ✅ Completed | Scaffolding, EventBus, StateMachine, SpatialGrid, Isometric Camera, FoW shader, Health system |
| **Phase 2** | Zombies & Navigation | ✅ Completed | FlowFieldNavigator, SensorySystem, Acoustic propagation, Shambler archetype, Melee combat |
| **Phase 3** | Building & NavObstacles | ✅ Completed | Grid building, Scrap & Chain Link walls, Interactive doors, Dynamic NavObstacles, Barricade degradation |
| **Phase 4** | Combat & Visibility Depth | ✅ Completed | Multi-weapon inventory, ballistics, tracers, Sprinter/Bloater archetypes, flashlight battery simulation |
| **Phase 5** | Survival & Economy | ✅ Completed | Inventory grid, container searching, hunger/thirst/stamina, crafting, corpse rot lifecycle & miasma aura, day/night cycle |
| **Phase 6** | Power Grid & Islanding | ✅ Completed | Combustion generator, battery bank, inverter, BFS sub-grid isolation, powered loads (floodlight, electric fence, freezer) |
| **Phase 7** | NPCs & Hostile Bandits | ✅ Completed | Survivor AI (3 archetypes), camp morale, task priority assignment, BanditBase squads (cover/flank/suppress), airdrop contest zones |
| **Phase 8** | Full Content Complete | 🔲 Planned | All 9 zombie archetypes, all 7 survivor roles, Tier 3 warlord bandits, 30 building variations |
| **Phase 9** | Polish & Optimization | 🔲 Planned | World events, weather/seasons, save/load serialization, flow-field thread pooling, release candidate |

---

## 📁 Repository Structure

```
res://
├── PROJECT ZOMBIE POST APOCALYPTIC.csproj  # .NET 8.0 Godot SDK project
├── project.godot                           # Engine config, input mappings, physics
├── assets/
│   └── shaders/
│       └── fog_of_war.gdshader             # GPU visibility overlay shader
├── scenes/
│   ├── entities/
│   │   ├── player/Player.tscn              # Player with camera, FoW, combat, spotlight
│   │   └── zombies/archetypes/
│   │       ├── Shambler.tscn               # Shambler walker archetype
│   │       ├── Sprinter.tscn               # Fast runner archetype
│   │       └── Bloater.tscn                # Exploding boomer archetype
│   ├── levels/TestArena.tscn               # Testing sandbox with lighting, barricades, power grid & HUD
│   ├── levels/SystemsSelfTest.tscn         # Headless regression harness (Phase 5 + 6 assertions)
│   └── world/defenses/walls/
│       ├── ScrapWoodFence.tscn             # Tier 1 wall segment
│       ├── ChainLinkWall.tscn              # Tier 2 see-through wall segment
│       └── WoodenDoor.tscn                 # Interactive doorway
└── src/
    ├── Core/
    │   ├── Autoloads/ (EventBus, GameManager)
    │   ├── Components/ (HealthComponent, AudioEmitterComponent, InventoryComponent)
    │   ├── Data/ (Enums, ItemData)
    │   ├── Diagnostics/ (SystemsSelfTest)
    │   ├── Spatial/ (SpatialGrid, FlowFieldNavigator)
    │   ├── StateMachine/ (StateMachine, State)
    │   ├── Utilities/ (MathUtils)
    │   └── Vision/ (FieldOfView, FogOfWarSystem, FogOfWarRenderer, VisionOccluder)
    ├── Entities/
    │   ├── Player/ (PlayerController, PlayerCombat, PlayerInteraction, PlayerStats)
    │   ├── Zombies/
    │   │   ├── ZombieBase.cs
    │   │   ├── Archetypes/ (ShamblerWalker.cs, SprinterRunner.cs, BloaterBoomer.cs)
    │   │   └── ZombieAI/ (SensorySystem, States: Idle, Alert, Chase, Attack, Hurt, Dead)
    │   ├── Survivors/ (SurvivorBase, SurvivorAI, MoraleSystem, SurvivorArchetypes)
    │   ├── Bandits/ (BanditBase, BanditSquad, BanditSpawner)
    │   └── NPC/ (NpcEnums: SurvivorTask, BanditTier, SquadRole, BanditCombatState)
    ├── Systems/
    │   ├── Building/ (BuildingSystem, BuildingGhost)
    │   ├── Combat/ (WeaponData, ProjectileManager)
    │   ├── Crafting/ (CraftingSystem, CraftingRecipe)
    │   └── Loot/ (LootContainer, LootEntry, container archetype loot tables)
    ├── UI/
    │   └── HUD/ (TestArenaHUD)
    └── World/
        ├── Buildings/StructuralIntegrity/ (BarricadeDegradation)
        ├── Defenses/ (WallBase, DoorBase, WallTiers: ScrapWoodFence, ChainLinkWall)
        ├── Environment/ (AcousticPropagation, Corpse, CorpseManager, DayNightCycle)
        ├── Power/
        │   ├── PowerGrid.cs (BFS islanding manager)
        │   ├── PowerGridNode.cs (base grid node)
        │   ├── PowerIsland.cs (per-island generation / storage / load simulation)
        │   ├── Generation/ (CombustionGenerator)
        │   ├── Storage/ (BatteryBank, Inverter)
        │   ├── Distribution/ (WiringSegment)
        │   └── Loads/ (PoweredDevice, SearchLight, ElectricFence, Freezer)
        └── WorldEvents/ (AirdropEvent)
```

---

## 🛠️ Getting Started

### Prerequisites
- [Godot Engine 4.7 .NET / Mono](https://godotengine.org/download/)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Installation & Launch
1. Clone this repository:
   ```bash
   git clone https://github.com/msudario018/PROJECT-GODOT-ZOMBIE.git
   cd PROJECT-GODOT-ZOMBIE
   ```
2. Build the C# solution:
   ```bash
   dotnet build
   ```
3. Open the project in Godot 4.7 and hit **F5** (or run `scenes/levels/TestArena.tscn`).
4. *(Optional)* Run the headless systems self-test — 82 assertions covering inventory, consumables, crafting, loot, corpse rot, BFS islanding, battery buffering, load shedding, morale, survivor tasks, bandit squads and airdrop contests:
   ```bash
   godot --headless --path . res://scenes/levels/SystemsSelfTest.tscn
   ```
   The process exit code equals the number of failed assertions, so `0` means everything is green.

---

## 📜 License
Developed as part of the Hardcore Post-Apocalyptic Zombie Sandbox Project.
All rights reserved.
