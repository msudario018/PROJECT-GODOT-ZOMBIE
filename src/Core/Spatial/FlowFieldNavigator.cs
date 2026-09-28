using Godot;
using System;
using System.Collections.Generic;
using ZombieApocalypse.Core.Autoloads;

namespace ZombieApocalypse.Core.Spatial;

/// <summary>
/// Precomputed flow-field navigation system for mass horde steering.
/// 
/// Architecture (Section 6b):
///   - Precomputes a direction vector field over a 2D grid on the XZ world plane.
///   - Uses Dijkstra flood-fill from target cell(s) outward.
///   - Obstacles (walls, impassable terrain) have maximum cost.
///   - Each cell stores: integration cost, normalized 2D direction vector, blocked flag.
///   - Hundreds of zombies in "Horde" mode sample their current cell's vector
///     in O(1) time without triggering individual NavigationAgent3D A* queries.
///   - Automatically recomputes when the target moves beyond a threshold or when
///     notified via <see cref="EventBus.OnFlowFieldInvalidated"/>.
/// </summary>
public partial class FlowFieldNavigator : Node3D
{
    public static FlowFieldNavigator? Instance { get; private set; }

    [ExportGroup("Grid Configuration")]
    [Export] public float CellSize = 2.0f;
    [Export] public int GridWidth = 64;
    [Export] public int GridHeight = 64;
    [Export] public Vector3 WorldCenter = Vector3.Zero;

    [ExportGroup("Target & Recompute Tuning")]
    [Export] public NodePath? TargetPath;
    [Export] public float TargetMoveThreshold = 3.0f;
    [Export] public float MaxFieldAgeSeconds = 2.5f;
    [Export(PropertyHint.Layers3DPhysics)] public uint ObstacleCollisionMask = 1 | 8; // Layer 1 (World) & Layer 4 (VisionBlockers / Walls)

    // Grid buffers
    private byte[] _blocked = Array.Empty<byte>();
    private float[] _costField = Array.Empty<float>();
    private Vector2[] _flowField = Array.Empty<Vector2>();

    // Runtime state
    private Node3D? _target;
    private Vector3 _lastTargetPos;
    private float _timeSinceLastUpdate = 0f;
    private bool _needsRecompute = true;
    private Vector2I _gridOriginCellOffset;

    private const float DiagonalCost = 1.4142f;
    private const float OrthogonalCost = 1.0f;
    private const float UnreachableCost = 999999f;

    // Neighbor offsets (8 directions: N, S, E, W, NE, NW, SE, SW)
    private static readonly (int dx, int dz, float cost)[] Neighbors = new (int, int, float)[]
    {
        (0, -1, OrthogonalCost),
        (0, 1, OrthogonalCost),
        (-1, 0, OrthogonalCost),
        (1, 0, OrthogonalCost),
        (-1, -1, DiagonalCost),
        (1, -1, DiagonalCost),
        (-1, 1, DiagonalCost),
        (1, 1, DiagonalCost)
    };

    public override void _Ready()
    {
        Instance = this;
        InitializeGrid();

        if (TargetPath != null && !TargetPath.IsEmpty)
        {
            _target = GetNodeOrNull<Node3D>(TargetPath);
        }

        // Subscribe to flow-field invalidation (e.g. wall placed or destroyed)
        if (EventBus.Instance != null)
        {
            EventBus.Instance.OnFlowFieldInvalidated += MarkDirty;
        }

        GD.Print($"[FlowFieldNavigator] Initialized {GridWidth}x{GridHeight} grid (CellSize={CellSize}m, total {GridWidth * GridHeight} cells).");
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;

        if (EventBus.Instance != null)
        {
            EventBus.Instance.OnFlowFieldInvalidated -= MarkDirty;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _timeSinceLastUpdate += (float)delta;

        // Try auto-acquiring player if target is missing
        if (_target == null || !GodotObject.IsInstanceValid(_target))
        {
            _target = GetTree().GetFirstNodeInGroup("player") as Node3D;
        }

        if (_target != null && GodotObject.IsInstanceValid(_target))
        {
            float targetMovedSq = _lastTargetPos.DistanceSquaredTo(_target.GlobalPosition);
            if (targetMovedSq > TargetMoveThreshold * TargetMoveThreshold)
            {
                _needsRecompute = true;
            }
        }

        if (_timeSinceLastUpdate >= MaxFieldAgeSeconds)
        {
            _needsRecompute = true;
        }

        if (_needsRecompute)
        {
            RecomputeFlowField();
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Grid Initialization & Coordinates
    // ════════════════════════════════════════════════════════════════

    private void InitializeGrid()
    {
        int totalCells = GridWidth * GridHeight;
        _blocked = new byte[totalCells];
        _costField = new float[totalCells];
        _flowField = new Vector2[totalCells];

        _gridOriginCellOffset = new Vector2I(
            Mathf.FloorToInt(WorldCenter.X / CellSize) - GridWidth / 2,
            Mathf.FloorToInt(WorldCenter.Z / CellSize) - GridHeight / 2
        );
    }

    public void SetTarget(Node3D target)
    {
        _target = target;
        _needsRecompute = true;
    }

    public void MarkDirty()
    {
        _needsRecompute = true;
    }

    private int GridIndex(int x, int z) => z * GridWidth + x;

    private bool IsInGrid(int x, int z) => x >= 0 && x < GridWidth && z >= 0 && z < GridHeight;

    public Vector2I WorldToGridCoords(Vector3 worldPos)
    {
        int cellX = Mathf.FloorToInt(worldPos.X / CellSize) - _gridOriginCellOffset.X;
        int cellZ = Mathf.FloorToInt(worldPos.Z / CellSize) - _gridOriginCellOffset.Y;
        return new Vector2I(cellX, cellZ);
    }

    public Vector3 GridCoordsToWorld(int x, int z)
    {
        float wx = (x + _gridOriginCellOffset.X + 0.5f) * CellSize;
        float wz = (z + _gridOriginCellOffset.Y + 0.5f) * CellSize;
        return new Vector3(wx, WorldCenter.Y, wz);
    }

    // ════════════════════════════════════════════════════════════════
    // Recomputation
    // ════════════════════════════════════════════════════════════════

    public void RecomputeFlowField()
    {
        if (_target == null || !GodotObject.IsInstanceValid(_target))
            return;

        _needsRecompute = false;
        _timeSinceLastUpdate = 0f;
        _lastTargetPos = _target.GlobalPosition;

        // Recenter grid origin around target if shifted
        _gridOriginCellOffset = new Vector2I(
            Mathf.FloorToInt(_lastTargetPos.X / CellSize) - GridWidth / 2,
            Mathf.FloorToInt(_lastTargetPos.Z / CellSize) - GridHeight / 2
        );

        ScanObstacles();
        ComputeCostField();
        ComputeVectorField();
    }

    /// <summary>
    /// Probe the physics world to find static walls/obstacles for each cell.
    /// </summary>
    private void ScanObstacles()
    {
        var spaceState = GetWorld3D()?.DirectSpaceState;
        if (spaceState == null) return;

        float halfCell = CellSize * 0.45f;
        var shapeQuery = new PhysicsShapeQueryParameters3D();
        var box = new BoxShape3D();
        box.Size = new Vector3(CellSize * 0.9f, 2.0f, CellSize * 0.9f);
        shapeQuery.ShapeRid = box.GetRid();
        shapeQuery.CollisionMask = ObstacleCollisionMask;

        for (int z = 0; z < GridHeight; z++)
        {
            for (int x = 0; x < GridWidth; x++)
            {
                var worldPos = GridCoordsToWorld(x, z);
                shapeQuery.Transform = new Transform3D(Basis.Identity, worldPos + Vector3.Up * 1.0f);
                var hits = spaceState.IntersectShape(shapeQuery, 1);
                _blocked[GridIndex(x, z)] = (byte)(hits.Count > 0 ? 1 : 0);
            }
        }
    }

    /// <summary>
    /// Dijkstra flood-fill from the target cell outwards.
    /// </summary>
    private void ComputeCostField()
    {
        int totalCells = GridWidth * GridHeight;
        for (int i = 0; i < totalCells; i++)
        {
            _costField[i] = UnreachableCost;
        }

        var targetGridPos = WorldToGridCoords(_lastTargetPos);
        if (!IsInGrid(targetGridPos.X, targetGridPos.Y))
            return;

        int targetIdx = GridIndex(targetGridPos.X, targetGridPos.Y);
        _costField[targetIdx] = 0f;

        // Priority queue / min-heap simulation with PriorityQueue
        var openQueue = new PriorityQueue<int, float>();
        openQueue.Enqueue(targetIdx, 0f);

        while (openQueue.Count > 0)
        {
            if (!openQueue.TryDequeue(out int currentIdx, out float currentCost))
                break;

            if (currentCost > _costField[currentIdx])
                continue;

            int curX = currentIdx % GridWidth;
            int curZ = currentIdx / GridWidth;

            foreach (var (dx, dz, stepCost) in Neighbors)
            {
                int nx = curX + dx;
                int nz = curZ + dz;

                if (!IsInGrid(nx, nz))
                    continue;

                int neighborIdx = GridIndex(nx, nz);
                if (_blocked[neighborIdx] == 1)
                    continue;

                float newCost = currentCost + stepCost;
                if (newCost < _costField[neighborIdx])
                {
                    _costField[neighborIdx] = newCost;
                    openQueue.Enqueue(neighborIdx, newCost);
                }
            }
        }
    }

    /// <summary>
    /// For each cell, point toward the neighbor with the lowest integration cost.
    /// </summary>
    private void ComputeVectorField()
    {
        for (int z = 0; z < GridHeight; z++)
        {
            for (int x = 0; x < GridWidth; x++)
            {
                int curIdx = GridIndex(x, z);

                if (_blocked[curIdx] == 1 || _costField[curIdx] >= UnreachableCost)
                {
                    _flowField[curIdx] = Vector2.Zero;
                    continue;
                }

                float lowestCost = _costField[curIdx];
                Vector2 bestDir = Vector2.Zero;

                foreach (var (dx, dz, _) in Neighbors)
                {
                    int nx = x + dx;
                    int nz = z + dz;

                    if (!IsInGrid(nx, nz))
                        continue;

                    int nIdx = GridIndex(nx, nz);
                    float nCost = _costField[nIdx];

                    if (nCost < lowestCost)
                    {
                        lowestCost = nCost;
                        bestDir = new Vector2(dx, dz);
                    }
                }

                _flowField[curIdx] = bestDir.Normalized();
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Public Query API
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sample the flow-field direction at a world position.
    /// Returns a normalized 3D horizontal direction (Y=0) pointing toward the target.
    /// If outside grid or unreachable, falls back to direct vector toward the target.
    /// </summary>
    public Vector3 GetFlowDirection(Vector3 worldPos)
    {
        var gridPos = WorldToGridCoords(worldPos);

        if (IsInGrid(gridPos.X, gridPos.Y))
        {
            int idx = GridIndex(gridPos.X, gridPos.Y);
            var dir2D = _flowField[idx];

            if (dir2D.LengthSquared() > 0.01f)
            {
                return new Vector3(dir2D.X, 0f, dir2D.Y).Normalized();
            }
        }

        // Fallback: direct line to target
        if (_target != null && GodotObject.IsInstanceValid(_target))
        {
            var diff = _target.GlobalPosition - worldPos;
            diff.Y = 0f;
            return diff.LengthSquared() > 0.01f ? diff.Normalized() : Vector3.Zero;
        }

        return Vector3.Zero;
    }

    /// <summary>
    /// Check whether a world position is within the active flow-field bounds and has a path.
    /// </summary>
    public bool HasPath(Vector3 worldPos)
    {
        var gridPos = WorldToGridCoords(worldPos);
        if (!IsInGrid(gridPos.X, gridPos.Y)) return false;
        int idx = GridIndex(gridPos.X, gridPos.Y);
        return _costField[idx] < UnreachableCost;
    }
}
