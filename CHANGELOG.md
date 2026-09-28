# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [0.4.0] - 2026-09-28 — Phase 4: Combat & Visibility Depth

### Added
- **Multi-Weapon Combat System (`PlayerCombat.cs`, `WeaponData.cs`):**
  - **Slot 1 (`Key 4`):** Rusty Crowbar (Melee, 35 HP blunt damage, 2.4m range, 7.0 m/s knockback, silent 6m acoustic footprint).
  - **Slot 2 (`Key 5`):** M9 Service Pistol (Ranged 9mm, 42 HP ballistic damage, 15-round mag, 45 reserve, 1.6s reload, 45m acoustic footprint).
  - **Slot 3 (`Key 6`):** Remington 870 Shotgun (Ranged 12G, 8 pellets $\times$ 13 HP = 104 HP max point-blank damage, $12^\circ$ spread cone, 6-round tube, 24 reserve, 65m acoustic footprint).
  - Rapid weapon cycling via `Key Q` or direct number slot keys (`4`, `5`, `6`).
  - Magazine and reserve ammo tracking with reload cooldown and auto-reload on empty clip.
- **Ballistics & Visual Effects (`ProjectileManager.cs`):**
  - Real-time cylinder mesh beam tweening for high-speed bullet tracers.
  - Surface impact spark visual effects at raycast collision points.
- **Sprinter / Runner Zombie Archetype (`SprinterRunner.cs`, `Sprinter.tscn`):**
  - Fast erratic pursuit ($4.8\text{ m/s}$), fragile durability ($60\text{ HP}$).
  - Expanded sensory sight cone ($22\text{ m}$, $120^\circ$) that aggressively alerts to gunshots and sprint footsteps.
  - Rapid melee strikes ($18\text{ damage}$, $1.0\text{ s}$ cooldown).
- **Bloater / Boomer Zombie Archetype (`BloaterBoomer.cs`, `Bloater.tscn`):**
  - Durable, lumbering tank ($160\text{ HP}$, $1.2\text{ m/s}$ movement, $2.5\text{ horde force}$).
  - Suicide detonation fuse triggering when within $1.8\text{ m}$ of targets or upon death.
  - Toxic blast dealing $50\text{ explosive damage}$ across a $4.5\text{ m}$ radius with physics knockback against players, walls, and other zombies.
  - Generates a catastrophic $65\text{ m}$ acoustic explosion shockwave alerting the surrounding world.
- **Dynamic Flashlight & Battery Simulation (`FieldOfView.cs`, `SpotLight3D`):**
  - Real-time battery drain ($2\%/\text{s}$ while on) with auto-shutoff at $0\%$ and passive trickle charging ($0.5\%/\text{s}$).
  - Real-time orientation of player `SpotLight3D` node and narrow FoW cone to match mouse aim direction.
- **HUD Combat Overlay (`TestArenaHUD.cs`):**
  - Displays current weapon name, magazine/reserve ammunition, real-time reload percentage, flashlight battery gauge, and updated keybinding guide.

### Changed
- Ground albedo darkened to `Color(0.2, 0.22, 0.25)` to provide clear contrast beneath dynamic lights and fog.
- Ambient light energy balanced to `0.15` with `DirectionalLight3D` casting hard directional shadows.
- `BuildingSystem` and `PlayerCombat` hotkey conflict resolved: `Key R` reloads ranged weapons only when not placing structures.

---

## [0.3.0] - 2026-09-28 — Phase 3: Base Building & Dynamic NavObstacles

### Added
- **Grid-Based Construction System (`BuildingSystem.cs`, `BuildingGhost.cs`):**
  - Snaps placement to a uniform $2\text{ m}$ world grid.
  - Real-time placement ghost with green/red material feedback indicating collision clearance.
  - Key bindings for building Tier 1 Scrap Wood Fence (`1`), Tier 2 Chain Link Wall (`2`), and Reinforced Wooden Door (`3`).
  - Rotation preview in $90^\circ$ increments via `Key R`.
- **Dynamic Navigation Obstacles (`WallBase.cs`):**
  - Automatically provisions `NavigationObstacle3D` nodes on placement and clears them on destruction.
  - Triggers `EventBus.OnFlowFieldInvalidated` to force dynamic horde re-routing without thread-locking NavigationRegion rebakes.
- **Barricade Tiers & Properties:**
  - **Tier 1 (Scrap Wood Fence):** $250\text{ HP}$, fully opaque vision blocker, susceptible to fire.
  - **Tier 2 (Chain Link Wall):** $500\text{ HP}$, heavy-gauge steel wire mesh, see-through (transparent to line-of-sight and FoW).
- **Interactive Doorway (`DoorBase.cs`, `WoodenDoor.tscn`):**
  - Interactive opening/closing via `Key E`.
  - Dynamically toggles physics collision, vision occlusion (`VisionOccluder.cs`), and navigation obstacles.
- **Structural Integrity & Barricade Degradation:**
  - Accumulates multi-zombie horde push pressure: $\text{Pressure} = \sum \text{force} \cdot (1 + 0.05 \cdot N)$.
  - Triggers structural damage bursts when accumulated pressure exceeds threshold.

---

## [0.2.0] - 2026-09-28 — Phase 2: Zombies & Navigation

### Added
- **Dijkstra Horde Flow-Field Navigation (`FlowFieldNavigator.cs`):**
  - $O(1)$ directional vector lookup for swarming hordes across a $50 \times 50$ discrete world grid.
  - Avoids per-agent A* CPU pathfinding bottlenecks for large swarms.
- **Acoustic Simulation Framework (`AcousticPropagation.cs`, `AudioEmitterComponent.cs`):**
  - Propagation of acoustic events (walk, sprint, melee swing, gunshot, zombie screams) with attenuation based on weather and physical geometry.
- **AI Sensory Perception (`SensorySystem.cs`):**
  - $110^\circ$, $14\text{ m}$ vision cone with line-of-sight obstacle raycasts on collision Layer 4.
  - Proximity detection ($2.5\text{ m}$) and acoustic hearing sensitivity.
- **Hierarchical Zombie AI State Machine:**
  - 7 behavioral states: `Idle`, `Alert` (investigating sounds), `Chase`, `Attack` (windup and bite), `Hurt`, and `Dead`.
- **Shambler / Walker Archetype (`ShamblerWalker.cs`, `Shambler.tscn`):**
  - $80\text{ HP}$, $1.8\text{ m/s}$ dragging gait, $12\text{ damage}$, $1.3\text{ m}$ reach.
- **Melee Combat Mechanics:**
  - Mouse-directed arc sweeps, directional knockback impulses, and acoustic swing alerts.

---

## [0.1.0] - 2026-09-28 — Phase 1: Foundation & Architectural Scaffold

### Added
- **Engine Setup:** Godot 4.7 .NET / C# solution with Jolt 3D physics engine and Forward+ rendering.
- **Global Event Bus (`EventBus.cs`):** Decoupled signal dispatch for sounds, damage, entity deaths, and building events.
- **Game Management (`GameManager.cs`):** Global lifecycle state controller.
- **Finite State Machine Core (`StateMachine.cs`, `State.cs`):** Modular FSM foundation.
- **Spatial Partitioning (`SpatialGrid.cs`):** Spatial hash grid for accelerated $O(1)$ radius and neighborhood queries.
- **Isometric Orthographic Camera:** Locked isometric diamond angle ($-35.264^\circ$ pitch, $-45^\circ$ yaw) with trigonometric ground-plane focus.
- **Fog of War & Vision Occlusion (`FogOfWarSystem.cs`, `FogOfWarRenderer.cs`, `fog_of_war.gdshader`):**
  - 3-tier visibility model (Unexplored / Explored / Visible) rendered via fullscreen post-process shader.
  - Real-time line-of-sight raycasting stopping at vision-blocking colliders.
- **Player Character (`PlayerController.cs`, `Player.tscn`):**
  - 8-directional isometric movement with acceleration, friction, sprint multiplier, and mouse-directed aiming.
- **Health System (`HealthComponent.cs`):**
  - Health pooling, armor reduction, damage type resistances, and death notification events.
- **Test Arena Sandbox (`TestArena.tscn`):**
  - Enclosed 3D testing environment with static obstacles, lighting, and debug HUD overlay.
