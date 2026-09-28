using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Utilities;

namespace ZombieApocalypse.Core.Spatial;

/// <summary>
/// Uniform spatial grid for O(1) broad-phase entity queries.
/// Divides the XZ world plane into fixed-size cells. Each entity registers
/// itself into its current cell every physics tick, enabling:
///   - Fast neighbor queries ("all zombies within 12m")
///   - Horde density tracking (cells with high counts trigger merge logic)
///   - Acoustic/scent lookups (replaces brute-force distance checks)
///
/// The grid is sparse — only occupied cells allocate memory.
/// </summary>
public class SpatialGrid
{
    private readonly Dictionary<Vector2I, List<Node3D>> _cells = new();
    private readonly Dictionary<ulong, Vector2I> _entityCells = new(); // entity InstanceId → current cell
    private readonly float _cellSize;

    /// <summary>Size of each grid cell in world units.</summary>
    public float CellSize => _cellSize;

    /// <summary>Number of cells currently occupied by at least one entity.</summary>
    public int OccupiedCellCount => _cells.Count;

    /// <summary>Total number of tracked entities.</summary>
    public int EntityCount => _entityCells.Count;

    /// <param name="cellSize">Cell size in world units. Smaller = more precise, larger = fewer cells to iterate.</param>
    public SpatialGrid(float cellSize = 4.0f)
    {
        _cellSize = Mathf.Max(0.5f, cellSize);
    }

    // ════════════════════════════════════════════════════════════════
    // Coordinate Conversion
    // ════════════════════════════════════════════════════════════════

    /// <summary>Convert a world position to the grid cell it falls in.</summary>
    public Vector2I WorldToCell(Vector3 worldPos)
        => new((int)Mathf.Floor(worldPos.X / _cellSize),
               (int)Mathf.Floor(worldPos.Z / _cellSize));

    /// <summary>Get the world-space center of a grid cell (at Y=0).</summary>
    public Vector3 CellToWorld(Vector2I cell)
        => new(cell.X * _cellSize + _cellSize * 0.5f,
               0f,
               cell.Y * _cellSize + _cellSize * 0.5f);

    // ════════════════════════════════════════════════════════════════
    // Entity Registration
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Update an entity's cell registration. Call every physics tick for each tracked entity.
    /// Automatically handles cell transitions (removes from old cell, inserts into new cell).
    /// No-ops if the entity hasn't moved to a different cell.
    /// </summary>
    public void UpdateEntity(Node3D entity, Vector3 position)
    {
        var newCell = WorldToCell(position);
        var entityId = entity.GetInstanceId();

        if (_entityCells.TryGetValue(entityId, out var oldCell))
        {
            if (oldCell == newCell) return; // Same cell — skip
            RemoveFromCell(entity, oldCell);
        }

        AddToCell(entity, newCell);
        _entityCells[entityId] = newCell;
    }

    /// <summary>
    /// Remove an entity from the grid entirely. Call when an entity is destroyed or despawned.
    /// </summary>
    public void RemoveEntity(Node3D entity)
    {
        var entityId = entity.GetInstanceId();
        if (_entityCells.TryGetValue(entityId, out var cell))
        {
            RemoveFromCell(entity, cell);
            _entityCells.Remove(entityId);
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Queries
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Find all entities within a radius of a world position.
    /// Iterates cells in the AABB, then distance-filters for accuracy.
    /// </summary>
    /// <param name="center">World position center of the search.</param>
    /// <param name="radius">Search radius in world units.</param>
    /// <returns>List of entities within the radius (not sorted).</returns>
    public List<Node3D> QueryRadius(Vector3 center, float radius)
    {
        var results = new List<Node3D>();
        var centerCell = WorldToCell(center);
        int cellRadius = Mathf.CeilToInt(radius / _cellSize);
        float radiusSq = radius * radius;

        for (int dx = -cellRadius; dx <= cellRadius; dx++)
        {
            for (int dz = -cellRadius; dz <= cellRadius; dz++)
            {
                var cell = new Vector2I(centerCell.X + dx, centerCell.Y + dz);
                if (!_cells.TryGetValue(cell, out var entities)) continue;

                foreach (var entity in entities)
                {
                    if (IsInstanceValid(entity) &&
                        MathUtils.DistanceSquaredXZ(center, entity.GlobalPosition) <= radiusSq)
                    {
                        results.Add(entity);
                    }
                }
            }
        }

        return results;
    }

    /// <summary>Get all entities in a specific cell. Returns empty list for unoccupied cells.</summary>
    public IReadOnlyList<Node3D> GetCellContents(Vector2I cell)
    {
        return _cells.TryGetValue(cell, out var entities) ? entities : (IReadOnlyList<Node3D>)System.Array.Empty<Node3D>();
    }

    /// <summary>Get the number of entities in a specific cell.</summary>
    public int GetCellEntityCount(Vector2I cell)
    {
        return _cells.TryGetValue(cell, out var entities) ? entities.Count : 0;
    }

    /// <summary>Check if a cell has any entities.</summary>
    public bool IsCellOccupied(Vector2I cell) => _cells.ContainsKey(cell);

    /// <summary>Remove all entities from the grid.</summary>
    public void Clear()
    {
        _cells.Clear();
        _entityCells.Clear();
    }

    // ════════════════════════════════════════════════════════════════
    // Internals
    // ════════════════════════════════════════════════════════════════

    private void AddToCell(Node3D entity, Vector2I cell)
    {
        if (!_cells.TryGetValue(cell, out var list))
        {
            list = new List<Node3D>(4); // Pre-allocate small list
            _cells[cell] = list;
        }
        list.Add(entity);
    }

    private void RemoveFromCell(Node3D entity, Vector2I cell)
    {
        if (_cells.TryGetValue(cell, out var list))
        {
            list.Remove(entity);
            if (list.Count == 0)
                _cells.Remove(cell); // Free empty cells to keep the grid sparse
        }
    }

    private static bool IsInstanceValid(GodotObject obj)
    {
        return GodotObject.IsInstanceValid(obj);
    }
}
