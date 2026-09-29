using Godot;
using System;
using ZombieApocalypse.Core.Data;

namespace ZombieApocalypse.Core.Autoloads;

/// <summary>
/// Global event bus singleton for decoupled cross-system communication.
/// All systems publish and subscribe to events through this hub rather than
/// holding direct references to each other. This is the backbone of the
/// component-based architecture.
/// 
/// Registered as Autoload "Events" (Priority 2).
/// </summary>
public partial class EventBus : Node
{
    /// <summary>Singleton instance, set in _Ready. Available after autoload initialization.</summary>
    public static EventBus? Instance { get; private set; }

    // ── Acoustic Events ────────────────────────────────────────────────────
    /// <summary>Fired when any entity or action generates audible noise.</summary>
    public event Action<Vector3, float, AudioSourceType>? OnSoundEmitted;

    // ── Combat Events ──────────────────────────────────────────────────────
    /// <summary>Fired after damage is applied: (attacker, target, finalDamage).</summary>
    public event Action<Node, Node, float>? OnDamageDealt;

    /// <summary>Fired when any entity's HP reaches zero.</summary>
    public event Action<Node>? OnEntityKilled;

    // ── Power Grid Events ──────────────────────────────────────────────────
    /// <summary>Fired when any island's power balance changes (generation vs load).</summary>
    public event Action? OnPowerStatusChanged;

    /// <summary>Fired when total load exceeds generation + battery capacity (watts excess).</summary>
    public event Action<float>? OnGridOverload;

    /// <summary>Fired when a wiring break splits the grid: (islandId, nodeCount).</summary>
    public event Action<int, int>? OnGridIslanded;

    /// <summary>Fired when wiring repair merges two islands back together.</summary>
    public event Action<int>? OnIslandReconnected;

    // ── World / Weather Events ─────────────────────────────────────────────
    /// <summary>Fired when weather transitions to a new state.</summary>
    public event Action<int>? OnWeatherChanged;

    /// <summary>Fired when the season advances.</summary>
    public event Action<int>? OnSeasonChanged;

    /// <summary>Fired when a military airdrop spawns at a position.</summary>
    public event Action<Vector3>? OnAirdropSpawned;

    // ── Survivor / Camp Events ─────────────────────────────────────────────
    /// <summary>Fired when camp morale value changes (0-100 scale).</summary>
    public event Action<float>? OnCampMoraleChanged;

    /// <summary>Fired when a survivor takes a task: (survivorName, taskId).</summary>
    public event Action<string, int>? OnSurvivorTaskAssigned;

    // ── Structural Events ──────────────────────────────────────────────────
    /// <summary>Fired when a structural element collapses (load-bearing failure).</summary>
    public event Action<Node>? OnStructuralFailure;

    /// <summary>Fired when a wall segment takes damage: (wallNode, damageAmount).</summary>
    public event Action<Node, float>? OnWallDamaged;

    // ── Navigation Events ──────────────────────────────────────────────────
    /// <summary>Fired when a new NavigationObstacle3D is registered (wall built).</summary>
    public event Action<Vector3>? OnNavObstacleAdded;

    /// <summary>Fired when a NavigationObstacle3D is removed (wall destroyed).</summary>
    public event Action<Vector3>? OnNavObstacleRemoved;

    /// <summary>Fired when flow-field pathfinding data needs recomputation.</summary>
    public event Action? OnFlowFieldInvalidated;

    // ── Vision / Fog-of-War Events ─────────────────────────────────────────
    /// <summary>Fired when cells enter the player's field of view.</summary>
    public event Action<Vector2I[]>? OnTilesRevealed;

    /// <summary>Fired when cells leave the player's field of view.</summary>
    public event Action<Vector2I[]>? OnTilesHidden;

    // ── Corpse / Sanitation Events ─────────────────────────────────────────
    /// <summary>Fired when a corpse advances its rot stage: (position, miasmaRadius).</summary>
    public event Action<Vector3, float>? OnCorpseRotAdvanced;

    /// <summary>Fired when a corpse is buried or burned.</summary>
    public event Action<Vector3>? OnCorpseBuried;

    // ── Bandit Events ──────────────────────────────────────────────────────
    /// <summary>Fired when a bandit raid begins targeting the player's base.</summary>
    public event Action<Node>? OnBanditRaidIncoming;

    /// <summary>Fired when a bandit squad is fully eliminated.</summary>
    public event Action<Node>? OnBanditSquadEliminated;

    // ── Player Events ──────────────────────────────────────────────────────
    /// <summary>Fired every physics frame with the player's current world position.</summary>
    public event Action<Vector3>? OnPlayerMoved;

    /// <summary>Fired when the player's health changes (current HP value).</summary>
    public event Action<float>? OnPlayerHealthChanged;

    // ════════════════════════════════════════════════════════════════════════
    // Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    public override void _Ready()
    {
        Instance = this;
        GD.Print("[EventBus] Initialized.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Fire Methods — type-safe wrappers for raising events
    // ════════════════════════════════════════════════════════════════════════

    public void EmitSound(Vector3 position, float radius, AudioSourceType type)
        => OnSoundEmitted?.Invoke(position, radius, type);

    public void EmitDamageDealt(Node attacker, Node target, float amount)
        => OnDamageDealt?.Invoke(attacker, target, amount);

    public void EmitEntityKilled(Node entity)
        => OnEntityKilled?.Invoke(entity);

    public void EmitPowerStatusChanged()
        => OnPowerStatusChanged?.Invoke();

    public void EmitGridOverload(float excessWatts)
        => OnGridOverload?.Invoke(excessWatts);

    public void EmitGridIslanded(int islandId, int nodeCount)
        => OnGridIslanded?.Invoke(islandId, nodeCount);

    public void EmitIslandReconnected(int islandId)
        => OnIslandReconnected?.Invoke(islandId);

    public void EmitWeatherChanged(int weatherState)
        => OnWeatherChanged?.Invoke(weatherState);

    public void EmitSeasonChanged(int season)
        => OnSeasonChanged?.Invoke(season);

    public void EmitAirdropSpawned(Vector3 position)
        => OnAirdropSpawned?.Invoke(position);

    public void EmitCampMoraleChanged(float morale)
        => OnCampMoraleChanged?.Invoke(morale);

    public void EmitSurvivorTaskAssigned(string survivorName, int taskId)
        => OnSurvivorTaskAssigned?.Invoke(survivorName, taskId);

    public void EmitStructuralFailure(Node element)
        => OnStructuralFailure?.Invoke(element);

    public void EmitWallDamaged(Node wall, float damage)
        => OnWallDamaged?.Invoke(wall, damage);

    /// <summary>
    /// Register a new navigation obstacle (wall placed). Also invalidates the flow-field.
    /// </summary>
    public void EmitNavObstacleAdded(Vector3 position)
    {
        OnNavObstacleAdded?.Invoke(position);
        OnFlowFieldInvalidated?.Invoke();
    }

    /// <summary>
    /// Remove a navigation obstacle (wall destroyed). Also invalidates the flow-field.
    /// </summary>
    public void EmitNavObstacleRemoved(Vector3 position)
    {
        OnNavObstacleRemoved?.Invoke(position);
        OnFlowFieldInvalidated?.Invoke();
    }

    public void EmitTilesRevealed(Vector2I[] cells)
        => OnTilesRevealed?.Invoke(cells);

    public void EmitTilesHidden(Vector2I[] cells)
        => OnTilesHidden?.Invoke(cells);

    public void EmitCorpseRotAdvanced(Vector3 position, float miasmaRadius)
        => OnCorpseRotAdvanced?.Invoke(position, miasmaRadius);

    public void EmitCorpseBuried(Vector3 position)
        => OnCorpseBuried?.Invoke(position);

    public void EmitBanditRaidIncoming(Node squad)
        => OnBanditRaidIncoming?.Invoke(squad);

    public void EmitBanditSquadEliminated(Node squad)
        => OnBanditSquadEliminated?.Invoke(squad);

    public void EmitPlayerMoved(Vector3 position)
        => OnPlayerMoved?.Invoke(position);

    public void EmitPlayerHealthChanged(float currentHP)
        => OnPlayerHealthChanged?.Invoke(currentHP);
}
