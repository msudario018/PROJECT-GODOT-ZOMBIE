# Project Zombie Post-Apocalyptic

[![Engine](https://img.shields.io/badge/Godot-4.7_Forward%2B-blue.svg)](https://godotengine.org/)
[![Framework](https://img.shields.io/badge/.NET-8.0_C%23-purple.svg)](https://dotnet.microsoft.com/)
[![Physics](https://img.shields.io/badge/Physics-Jolt_3D-green.svg)](https://github.com/godot-jolt/godot-jolt)
[![Status](https://img.shields.io/badge/Status-Phase_3_Complete-success.svg)](#current-progress--phase-status)

A hardcore, mechanically rich post-apocalyptic zombie survival sandbox built in **Godot 4.7 (.NET / C#)** with true 2.5D isometric perspective, Jolt Physics, and Forward+ D3D12 rendering.

Every system—from structural physics and horde flow-fields to acoustic propagation and off-grid electrical islanding—is engineered as an interconnected, event-driven simulation.

---

## 🎮 Current Controls

| Key / Input | Action | Mechanical Effect |
| :--- | :--- | :--- |
| **`WASD`** | Move (Isometric) | Smooth acceleration/friction physics aligned with camera diamond grid. |
| **`Shift`** | Sprint | Increases speed by $1.6\times$; emits louder $12\text{ m}$ footstep acoustic radius. |
| **`LMB / Space`** | Melee Attack | Swings weapon toward mouse cursor ($110^\circ$ arc, $2.4\text{ m}$ reach, $35\text{ HP}$ blunt damage, $7.0\text{ m/s}$ knockback). |
| **`E`** | Interact | Opens or closes nearby doors, dynamically updating navigation obstacles and vision occlusion. |
| **`1`** | Build Scrap Fence | Selects Tier 1 Scrap Wood Fence ($250\text{ HP}$, opaque, flammable). |
| **`2`** | Build Chain Link Wall | Selects Tier 2 Chain Link Wall ($500\text{ HP}$, see-through steel wire mesh). |
| **`3`** | Build Reinforced Door | Selects interactive wooden doorway with swinging leaf and locking support. |
| **`R`** | Rotate Preview | Rotates ghost placement preview by $90^\circ$. |
| **`RMB / Esc`** | Cancel Build | Cancels active building placement mode. |
| **`F1`** | Toggle Fog-of-War | Instantly toggles FoW overlay ON / OFF for debugging and full-scene inspection. |
| **`F`** | Flashlight | Toggles narrow $30^\circ$, $25\text{ m}$ forward vision cone. |

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

---

## 🗺️ Architectural Roadmap

| Phase | Milestone | Key Features |
| :--- | :--- | :--- |
| **Phase 1** | Foundation | Scaffolding, EventBus, StateMachine, SpatialGrid, Isometric Camera, FoW shader, Health system |
| **Phase 2** | Zombies & Navigation | FlowFieldNavigator, SensorySystem, Acoustic propagation, Shambler archetype, Melee combat |
| **Phase 3** | Building & NavObstacles | Grid building, Scrap & Chain Link walls, Interactive doors, Dynamic NavObstacles, Barricade degradation |
| **Phase 4** | Combat & Visibility Depth | Ranged weapons, ballistic projectiles, ammo, Sprinter/Bloater archetypes, flashlight battery drain |
| **Phase 5** | Survival & Economy | Inventory grid, container searching, hunger/thirst/stamina, corpse rot lifecycle & miasma aura |
| **Phase 6** | Power Grid & Islanding | Generators, battery banks, inverters, BFS sub-grid isolation, powered defenses (fences, turrets, lights) |
| **Phase 7** | NPCs & Hostile Bandits | Survivor AI, camp morale, hostile human raiders (BanditBase) with cover-seeking and flanking AI |
| **Phase 8** | Full Content Complete | All 9 zombie archetypes, all 7 survivor roles, Tier 3 warlord bandits, 30 building variations |
| **Phase 9** | Polish & Optimization | World events, weather/seasons, save/load serialization, flow-field thread pooling, release candidate |

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
│   │   ├── player/Player.tscn              # Player character with camera, FoW, combat
│   │   └── zombies/archetypes/Shambler.tscn# Shambler zombie with AI state machine
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
    │       ├── Archetypes/ (ShamblerWalker.cs)
    │       └── ZombieAI/ (SensorySystem, States: Idle, Alert, Chase, Attack, Hurt, Dead)
    ├── Systems/
    │   └── Building/ (BuildingSystem, BuildingGhost)
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
