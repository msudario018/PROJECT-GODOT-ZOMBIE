# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [0.7.0] - 2026-09-29 — Phase 7: NPCs & Hostile Bandits

### Added
- **Survivor AI (`Entities/Survivors/SurvivorBase.cs`, `SurvivorAI.cs`):** composable NPC survivor with Health, Inventory, SensorySystem, NavigationAgent3D and a priority task loop — **Defend** (melee threats within an $18\text{ m}$ camp radius) → **Heal** (ally below 75% HP, including the player) → **Scavenge** (nearest unsearched container) → **Repair** (damaged walls, +40 HP per patch) → **RefuelPower** (carries a fuel can to the generator) → **Idle** wander. Self-builds its child components in `_EnterTree`, so survivors can be instantiated from code or scenes.
- **Survivor Archetypes (`SurvivorArchetypes.cs`):** CombatMedic (×1.5 heal, 90 HP), CombatVeteran (140 HP, +2 armor, 22 melee damage), ScavengerScout (4.4 m/s, 0.6× search time, 80 HP).
- **Camp Morale (`Entities/Survivors/MoraleSystem.cs`):** 0–100 camp morale driven by starvation, active miasma zones, grid status and events (survivor death −12, player death −25, burial +1.5, bandit kill +4, zombie kill +0.5). Broadcasts `OnCampMoraleChanged` and modulates survivor task speed (×0.8 below 30 morale, ×1.1 above 75).
- **Bandit Squads (`Entities/Bandits/BanditBase.cs`, `BanditSquad.cs`):** hostile raiders with tiered presets — Scavenger (retreats below 50% HP, suppressed gunshots), Militia (30% HP), Warlord (never retreats). Squad leader takes the **Advance** role while others **Suppress** (stationary ranged) or **Flank** (perpendicular 6 m side-step), with cohesion rally checks, group morale collapse → mass retreat, and a 45% morale penalty for losing the leader.
- **Bandit Combat Loop:** melee strikes, ranged shots with tracers/impact sparks/falloff damage, cover-seeking approach offsets, container looting during lulls, and acoustic footprints (gunshots alert zombies).
- **Bandit Spawner (`BanditSpawner.cs`):** cadence-based or debug-triggered raids deployed on a ring around the player; fires `OnBanditRaidIncoming` / `OnBanditSquadEliminated`.
- **Airdrop Contests (`World/WorldEvents/AirdropEvent.cs`):** fixed-manifest military cache (2 medkits, 3× 9mm packs, 3× food, 2× cloth) reserved from survivor scavengers, lootable by the player, and contested by a spawned raider squad; emits `OnAirdropSpawned`.
- **Shared NPC Enumerations (`Entities/NPC/NpcEnums.cs`):** `SurvivorTask`, `SurvivorArchetype`, `BanditTier`, `SquadRole`, `BanditCombatState`.
- **HUD Phase 7 Panel (`TestArenaHUD_Phase7.cs`):** camp morale bar, survivor/bandit counters, airdrop banner and debug keys `[H]` spawn raid, `[J]` airdrop, `[K]` cycle survivor task, `[L]` recruit survivor.
- **Self-Test Coverage:** 31 new assertions (82 total) for morale math/broadcasts, survivor task assignment, archetype stat presets, squad role assignment, flank geometry, tier retreat thresholds, squad elimination events, and airdrop manifest delivery.

### Changed
- **`LootContainer`:** search can be forced into any inventory (`ForceSearch`) for NPC scavengers/looters; airdrop caches carry a **per-instance** fixed loot manifest instead of mutating the shared archetype tables.
- **`CorpseManager`:** survivors and bandits now spawn corpse nodes on death, feeding the Phase 5 rot/miasma pipeline.
- **`EventBus`:** new `OnSurvivorTaskAssigned` event.
- **`TestArenaHUD`:** Phase 7 panel and debug keys wired through `partial` hooks so Phase 1–6 HUD code is untouched.

---

## [0.6.0] - 2026-09-29 — Phase 6: Power Grid & Islanding

### Added
- **Master Power Grid with BFS Islanding (`PowerGrid.cs`):**
  - Node graph discovery through the `power_nodes` / `power_wires` groups.
  - Breadth-first flood-fill groups nodes into electrically connected islands on every topology change; `OnGridIslanded` fires on splits and `OnIslandReconnected` when cables are repaired.
  - Connectivity comes from explicit `WiringSegment` cables plus an implicit proximity couple (≤ $4\text{ m}$) between adjacent hardware.
  - Per-tick (0.5 s) rebalancing, overload detection (`OnGridOverload`) and status broadcast (`OnPowerStatusChanged`).
- **Island Simulation (`PowerIsland.cs`):** each island independently sums generation, buffer discharge and demand, then allocates power by the `PowerPriority` load-shedding tiers (Critical → Defensive → Utility), drains or charges batteries, and reports deficit/shed counts.
- **Combustion Generator (`Generation/CombustionGenerator.cs`):** $1200\text{ W}$ DC source. Burns fuel canisters over real-time hours, emits a $55\text{ m}$ mechanical hum (attracts hordes), and is refuelled by interacting with a carried fuel canister.
- **Battery Bank (`Storage/BatteryBank.cs`):** $1200\text{ Wh}$ storage, $600\text{ W}$ charge / $900\text{ W}$ discharge limits, depth-of-discharge tracking and incremental capacity degradation on deep cycles.
- **Inverter (`Storage/Inverter.cs`):** DC→AC gate at $92\%$ efficiency with a $30\text{ W}$ standby draw; AC-only appliances (`RequiresInverter`) are shed in islands that have no powered inverter.
- **Wiring Cables (`Distribution/WiringSegment.cs`):** rated cable runs bridging arbitrary distance, with `Sever()` / `Repair()` / `ToggleSevered()` that force an island rebuild; drawn as an aligned cylinder mesh that turns red when cut.
- **Powered Loads (`Loads/`):**
  - `SearchLight`: $150\text{ W}$ floodlight that auto-illuminates between dusk and dawn.
  - `ElectricFence`: $320\text{ W}$ shock field ($22\text{ HP/s}$ electric damage, knockback) whose arc crackle emits a $25\text{ m}$ noise signature.
  - `Freezer`: $200\text{ W}$ cold storage that freezes corpses in a $5\text{ m}$ radius, halting miasma formation.
- **Power UI & Debug Controls (`TestArenaHUD.cs`):** live one-line grid summary (island count, generation, load, battery %, shed loads) with colour states, plus `[X]` sever/repair nearest cable and `[G]` generator start/stop.
- **Headless Regression Harness (`Core/Diagnostics/SystemsSelfTest.cs`, `scenes/levels/SystemsSelfTest.tscn`):** 51 assertions over inventory stacking/weight/encumbrance, consumables, crafting, loot containers, corpse rot events, grid topology, cable islanding, battery drain math ($610\text{ W} \times 60\text{ s} = 10.17\text{ Wh}$) and priority shedding. Exits with the failure count.

### Fixed
- **Fog-of-War shader parse failure (`assets/shaders/fog_of_war.gdshader`):** replaced the invalid `repeat_clamp_to_edge` sampler hint with valid Godot 4.7 syntax (`filter_linear, repeat_disable`), restoring the FoW overlay.
- **Zombie AI initialisation crash (`ZombieBase.cs`):** components are now resolved lazily, so the initial state entered by `StateMachine._Ready` (which runs before `ZombieBase._Ready`) no longer throws `NullReferenceException` on `Zombie.Sensory`.
- **Dead night-frenzy code:** `DayNightCycle.ZombieNightMultiplier` is now applied to zombie move speed (chase/alert/idle) and to sight range and hearing sensitivity.
- **Unused hearing sensitivity:** `SensorySystem.HearingSensitivity` is now applied per listener inside `AcousticPropagation`, instead of being ignored.

---

## [0.5.0] - 2026-09-29 — Phase 5: Survival & Economy

### Added
- **Slot-Based Inventory (`Core/Components/InventoryComponent.cs`):** 24 slots with per-item stack limits, real-time weight tracking, encumbrance detection ($-25\%$ move speed) and item consumption applying medical/food/water/stamina effects.
- **Item Registry (`Core/Data/ItemData.cs`):** flyweight `Resource` definitions for bandages, first aid kits, canned food, water, scrap, cloth, planks, nails, rope, 9mm/12G ammo packs, shovel, fuel canister and molotov cocktail.
- **Survival Needs (`Entities/Player/PlayerStats.cs`):** hunger, thirst and stamina drain/recovery, starvation and dehydration damage, stamina-depleted sprint lockout, and `Eat`/`Drink`/`RestoreStamina` API.
- **Crafting System (`Systems/Crafting/CraftingSystem.cs`):** five starter recipes (bandage, rope, improvised first aid kit, 9mm ammo box, molotov cocktail) with availability checks and ingredient consumption.
- **Searchable Loot Containers (`Systems/Loot/LootContainer.cs`):** five container archetypes with weighted loot tables, timed searches, searched-state visuals and interaction prompts.
- **Corpse Lifecycle (`World/Environment/Corpse.cs`, `CorpseManager.cs`):** Fresh → Bloated → Rotting Miasma → Skeleton decay with a toxic miasma aura, corpse scavenging, burial and incineration, capped corpse pooling, and `OnCorpseRotAdvanced` / `OnCorpseBuried` broadcasts.
- **Day/Night Cycle (`World/Environment/DayNightCycle.cs`):** time progression with Dawn/Day/Dusk/Night phases driving sun rotation, light energy/colour, ambient intensity and zombie night-frenzy multipliers.
- **Interaction & Hotkeys (`PlayerInteraction.cs`, `TestArenaHUD.cs`):** loot container searching, corpse scavenge/bury/burn, generator refuelling, plus `[7]`/`[8]`/`[9]` consumable hotkeys and `[C]`/`[V]`/`[B]` crafting shortcuts.

### Changed
- **Reload now consumes inventory ammo packs (`PlayerCombat.cs`):** when the loose reserve runs dry, ammo packs (`ammo_9mm`, `ammo_12g`) are converted into a full magazine from the inventory, making crafted and looted ammunition meaningful.
- **HUD (`TestArenaHUD.cs`):** added HP/food/water/stamina bars, inventory weight summary, day/night clock and miasma zone counter.

---



### Added
- **Synchronized Fog-of-War Flashlight Clearing (`FieldOfView.cs`, `FogOfWarSystem.cs`):**
  - Flashlight vision cone dynamically clears the Fog-of-War mask along the player's mouse aim direction.
  - Automatically synchronizes FoW raycast cone angle ($52^\circ$) and range ($25\text{ m}$) directly with `SpotLight3D` parameters using 48 dense raycasts.
  - Increased fog reveal transition speed to $20.0/\text{s}$ for instantaneous response when sweeping the mouse.
- **Downward Ground-Plane Light Puddle (`FieldOfView.cs`, `Player.tscn`):**
  - Flashlight `SpotLight3D` is positioned at chest height ($Y=1.2\text{ m}$) and aimed down toward the ground plane ($Y=0$) along the mouse aim vector.
  - Generates a prominent, high-contrast elliptical light puddle clearly visible from the isometric camera angle.
  - Low-battery warning dimming when battery drops below $15\%$.

### Changed
- **Evening/Night Lighting Atmosphere (`TestArena.tscn`):**
  - Reduced `DirectionalLight3D` sun energy from $1.3$ to $0.20$ with a cool moonlight tint (`Color(0.75, 0.82, 0.95)`).
  - Reduced ambient light energy from $0.15$ to $0.05$ with deep dusk ambient color (`Color(0.25, 0.3, 0.4)`).
  - Darkened background clear color to night black (`Color(0.06, 0.07, 0.09)`).
- **Flashlight Intensity & Parameters (`Player.tscn`, `FieldOfView.cs`):**
  - Boosted `SpotLight3D` light energy to $5.0$, spot range to $25.0\text{ m}$, and spot angle to $26^\circ$ ($52^\circ$ total beam).
  - Enabled dynamic shadow casting on flashlight beam.
  - Enabled flashlight by default on arena start.
- **Controls & Default Overlays (`project.godot`, `FogOfWarRenderer.cs`):**
  - Registered `flashlight` action in `project.godot` mapped to `Key.F` with physical key fallback in `FieldOfView.cs`.
  - Re-enabled Fog-of-War overlay by default with `[F1]` debug toggle.

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
