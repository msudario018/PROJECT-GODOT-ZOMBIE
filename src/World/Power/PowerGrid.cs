using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.World.Power.Distribution;

namespace ZombieApocalypse.World.Power;

/// <summary>
/// Master electrical network for the level.
///
/// Responsibilities:
///   1. Discover every <see cref="PowerGridNode"/> (group "power_nodes") and
///      <see cref="WiringSegment"/> (group "power_wires").
///   2. BFS flood-fill the connection graph into electrically connected islands —
///      cables bridge distance, proximity couples adjacent hardware.
///   3. Tick each island independently: generation → load shedding by priority
///      → battery charge/discharge.
///   4. Broadcast results through <see cref="EventBus"/> so UI, defenses and
///      devices react without hard references.
/// </summary>
public partial class PowerGrid : Node
{
    public static PowerGrid? Instance { get; private set; }

    [ExportGroup("Simulation")]
    /// <summary>How often the grid rebalances (seconds of real time).</summary>
    [Export] public float TickInterval = 0.5f;

    [ExportGroup("Connectivity")]
    /// <summary>Nodes closer than this are treated as jumpered together.</summary>
    [Export] public float ProximityConnectRadius = 4.0f;

    /// <summary>Disable to require an explicit cable between every node.</summary>
    [Export] public bool AutoConnectByProximity = true;

    [ExportGroup("Debug")]
    [Export] public bool LogIslandRebuilds = true;

    // ── Public state (consumed by the HUD) ───────────────────────────
    public IReadOnlyList<PowerIsland> Islands => _islands;
    public IReadOnlyList<PowerGridNode> Nodes => _nodes;
    public int IslandCount => _islands.Count;
    public float TotalGenerationWatts { get; private set; }
    public float TotalDemandWatts { get; private set; }
    public float TotalStoredWattHours { get; private set; }
    public float TotalStorageCapacityWattHours { get; private set; }
    public int TotalShedLoads { get; private set; }

    public float BatteryPercent => TotalStorageCapacityWattHours > 0.01f
        ? TotalStoredWattHours / TotalStorageCapacityWattHours
        : 0f;

    private readonly List<PowerIsland> _islands = new();
    private readonly List<PowerGridNode> _nodes = new();
    private readonly List<WiringSegment> _wires = new();

    private float _tickTimer;
    private bool _rescanQueued = true;
    private int _nextIslandId = 1;

    public override void _Ready()
    {
        Instance = this;
        RequestRescan();
        GD.Print("[PowerGrid] Online. BFS islanding armed.");
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Process(double delta)
    {
        if (_rescanQueued)
        {
            _rescanQueued = false;
            Rescan();
        }

        _tickTimer += (float)delta;
        if (_tickTimer < TickInterval) return;

        float step = _tickTimer;
        _tickTimer = 0f;
        SimulateTick(step);
    }

    /// <summary>Queue a topology rebuild (node/cable added, removed or severed).</summary>
    public void RequestRescan() => _rescanQueued = true;

    /// <summary>Re-collects nodes and cables, then rebuilds all islands.</summary>
    public void Rescan()
    {
        _nodes.Clear();
        _wires.Clear();

        foreach (var node in GetTree().GetNodesInGroup("power_nodes"))
            if (node is PowerGridNode pn && GodotObject.IsInstanceValid(pn))
                _nodes.Add(pn);

        foreach (var wire in GetTree().GetNodesInGroup("power_wires"))
            if (wire is WiringSegment ws && GodotObject.IsInstanceValid(ws))
            {
                ws.RefreshEndpoints();
                _wires.Add(ws);
            }

        RebuildIslands();
    }

    /// <summary>
    /// BFS flood-fill over the connection graph. Every unvisited node seeds a new
    /// island which absorbs all nodes reachable through cables or proximity.
    /// </summary>
    public void RebuildIslands()
    {
        int previousCount = _islands.Count;
        _islands.Clear();

        var visited = new HashSet<PowerGridNode>();
        var queue = new Queue<PowerGridNode>();

        foreach (var seed in _nodes)
        {
            if (visited.Contains(seed)) continue;

            var island = new PowerIsland { Id = _nextIslandId++ };
            queue.Clear();
            queue.Enqueue(seed);
            visited.Add(seed);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                island.AddNode(current);

                foreach (var neighbor in GetNeighbors(current))
                {
                    if (visited.Contains(neighbor)) continue;
                    visited.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }

            _islands.Add(island);
        }

        if (LogIslandRebuilds && _nodes.Count > 0)
            GD.Print($"[PowerGrid] Rebuilt topology: {_nodes.Count} nodes, {_wires.Count} cables → {_islands.Count} island(s).");

        if (EventBus.Instance == null) return;

        if (_islands.Count > previousCount && previousCount > 0)
        {
            GD.Print($"[PowerGrid] GRID SPLIT — now {_islands.Count} independent islands!");
            foreach (var island in _islands)
                EventBus.Instance.EmitGridIslanded(island.Id, island.Nodes.Count);
        }
        else if (_islands.Count < previousCount)
        {
            GD.Print($"[PowerGrid] Islands reconnected — {_islands.Count} island(s) remain.");
            foreach (var island in _islands)
                EventBus.Instance.EmitIslandReconnected(island.Id);
        }
    }

    /// <summary>True when power can flow directly between two nodes.</summary>
    public bool AreConnected(PowerGridNode a, PowerGridNode b)
    {
        // Explicit cable (intact only)
        foreach (var wire in _wires)
        {
            if (wire.IsSevered || wire.NodeA == null || wire.NodeB == null) continue;
            if ((wire.NodeA == a && wire.NodeB == b) || (wire.NodeA == b && wire.NodeB == a))
                return true;
        }

        // Implicit jumper-cable / adjacent-hardware coupling
        if (AutoConnectByProximity)
        {
            float r = ProximityConnectRadius;
            if (a.GlobalPosition.DistanceSquaredTo(b.GlobalPosition) <= r * r)
                return true;
        }

        return false;
    }

    private List<PowerGridNode> GetNeighbors(PowerGridNode node)
    {
        var result = new List<PowerGridNode>();
        foreach (var other in _nodes)
        {
            if (other == node) continue;
            if (AreConnected(node, other)) result.Add(other);
        }
        return result;
    }

    /// <summary>
    /// Advances every island by <paramref name="dtSeconds"/>. Public so tools and
    /// tests can drive the grid deterministically.
    /// </summary>
    public void SimulateTick(float dtSeconds)
    {
        TotalGenerationWatts = 0f;
        TotalDemandWatts = 0f;
        TotalShedLoads = 0;
        float deficit = 0f;

        foreach (var island in _islands)
        {
            island.Tick(dtSeconds);
            TotalGenerationWatts += island.GenerationWatts;
            TotalDemandWatts += island.DemandWatts;
            TotalShedLoads += island.ShedLoadCount;
            deficit += island.DeficitWatts;
        }

        TotalStoredWattHours = 0f;
        TotalStorageCapacityWattHours = 0f;
        foreach (var node in _nodes)
        {
            if (!node.IsStorage) continue;
            TotalStoredWattHours += node.StoredWattHours;
            TotalStorageCapacityWattHours += node.StorageCapacityWattHours;
        }

        if (EventBus.Instance == null) return;
        EventBus.Instance.EmitPowerStatusChanged();

        if (deficit > 0.5f)
            EventBus.Instance.EmitGridOverload(deficit);
    }

    /// <summary>One-line grid summary for HUD / debug output.</summary>
    public string GetSummaryLine()
    {
        string islandText = IslandCount <= 1 ? "1 grid" : $"{IslandCount} ISLANDS (severed!)";

        return $"Power: {islandText} | Gen {TotalGenerationWatts:F0}W | Load {TotalDemandWatts:F0}W" +
               (TotalStorageCapacityWattHours > 0.01f ? $" | Batt {BatteryPercent * 100f:F0}%" : "") +
               (TotalShedLoads > 0 ? $" | SHED {TotalShedLoads} LOAD(S)" : "");
    }
}
