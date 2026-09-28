# Project Zombie Post-Apocalyptic

[![Engine](https://img.shields.io/badge/Godot-4.7_Forward%2B-blue.svg)](https://godotengine.org/)
[![Framework](https://img.shields.io/badge/.NET-8.0_C%23-purple.svg)](https://dotnet.microsoft.com/)
[![Physics](https://img.shields.io/badge/Physics-Jolt_3D-green.svg)](https://github.com/godot-jolt/godot-jolt)
[![Status](https://img.shields.io/badge/Status-Phase_4_Complete-success.svg)](#current-progress--phase-status)

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
| **`F`** | Flashlight | Toggles narrow $30^\circ$, $25\text{ m}$ vision cone & 3D spotlight (consumes battery). |
| **`E`** | Interact | Opens or closes nearby doors, dynamically updating navigation obstacles and vision occlusion. |
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

---

## 🗺️ Architectural Roadmap

| Phase | Milestone | Status | Key Features |
| :--- | :--- | :--- | :--- |
| **Phase 1** | Foundation | ✅ Completed | Scaffolding, EventBus, StateMachine, SpatialGrid, Isometric Camera, FoW shader, Health system |
| **Phase 2** | Zombies & Navigation | ✅ Completed | FlowFieldNavigator, SensorySystem, Acoustic propagation, Shambler archetype, Melee combat |
| **Phase 3** | Building & NavObstacles | ✅ Completed | Grid building, Scrap & Chain Link walls, Interactive doors, Dynamic NavObstacles, Barricade degradation |
| **Phase 4** | Combat & Visibility Depth | ✅ Completed | Multi-weapon inventory, ballistics, tracers, Sprinter/Bloater archetypes, flashlight battery simulation |
| **Phase 5** | Survival & Economy | 🔲 Planned | Inventory grid, container searching, hunger/thirst/stamina, corpse rot lifecycle & miasma aura |
| **Phase 6** | Power Grid & Islanding | 🔲 Planned | Generators, battery banks, inverters, BFS sub-grid isolation, powered defenses (fences, turrets, lights) |
| **Phase 7** | NPCs & Hostile Bandits | 🔲 Planned | Survivor AI, camp morale, hostile human raiders (BanditBase) with cover-seeking and flanking AI |
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
│   ├── levels/TestArena.tscn               # Testing sandbox with lighting, barricades & HUD
│   └── world/defenses/walls/
│       ├── ScrapWoodFence.tscn             # Tier 1 wall segment
│       ├── ChainLinkWall.tscn              # Tier 2 see-through wall segment
│       └── WoodenDoor.tscn                 # Interactive doorway
└── src/
    ├── Core/
    │   ├── Autoloads/ (EventBus, GameManager)
    │   ├── Components/ (HealthComponent, AudioEmitterComponent)
    │   ├── Data/ (Enums)
    │   ├── Spatial/ (SpatialGrid, FlowFieldNavigator)
    │   ├── StateMachine/ (StateMachine, State)
    │   ├── Utilities/ (MathUtils)
    │   └── Vision/ (FieldOfView, FogOfWarSystem, FogOfWarRenderer, VisionOccluder)
    ├── Entities/
    │   ├── Player/ (PlayerController, PlayerCombat, PlayerInteraction)
    │   └── Zombies/
    │       ├── ZombieBase.cs
    │       ├── Archetypes/ (ShamblerWalker.cs, SprinterRunner.cs, BloaterBoomer.cs)
    │       └── ZombieAI/ (SensorySystem, States: Idle, Alert, Chase, Attack, Hurt, Dead)
    ├── Systems/
    │   ├── Building/ (BuildingSystem, BuildingGhost)
    │   └── Combat/ (WeaponData, ProjectileManager)
    ├── UI/
    │   └── HUD/ (TestArenaHUD)
    └── World/
        ├── Buildings/StructuralIntegrity/ (BarricadeDegradation)
        ├── Defenses/ (WallBase, DoorBase, WallTiers: ScrapWoodFence, ChainLinkWall)
        └── Environment/ (AcousticPropagation)
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

---

## 📜 License
Developed as part of the Hardcore Post-Apocalyptic Zombie Sandbox Project.
All rights reserved.
