using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Autoloads;

namespace ZombieApocalypse.World.Terrain;

/// <summary>
/// Runtime navigation baker.
///
/// Godot's <see cref="NavigationAgent3D"/> needs a baked navigation mesh on the
/// navigation map; without one every agent silently falls back to direct
/// steering. The project ships no editor-baked navmesh, so this node bakes one
/// at scene start from all static bodies tagged with the "nav_geometry" group
/// (ground, walls, obstacles).
///
/// Walls additionally register <c>NavigationObstacle3D</c> avoidance from
/// <see cref="Defenses.WallBase"/>, so both baked holes and soft avoidance
/// keep agents out of structures.
/// </summary>
public partial class NavigationBaker : Node3D
{
    public const string GeometryGroup = "nav_geometry";

    [Export] public bool BakeOnReady = true;
    [Export] public float CellSize = 0.25f;
    /// <summary>Must match the navigation map's cell height (Godot default 0.25) to avoid rasterisation warnings.</summary>
    [Export] public float CellHeight = 0.25f;
    // Agent metrics are kept as exact multiples of the cell size/height above,
    // otherwise the baker reports voxel-precision warnings.
    [Export] public float AgentRadius = 0.5f;
    [Export] public float AgentHeight = 1.75f;
    [Export] public float AgentMaxClimb = 0.5f;
    [Export] public float AgentMaxSlopeDeg = 45f;

    public NavigationRegion3D? Region { get; private set; }
    public bool HasBakedMesh => Region?.NavigationMesh != null && Region.NavigationMesh.GetPolygonCount() > 0;

    public override void _Ready()
    {
        AddToGroup("navigation_baker");

        if (!BakeOnReady) return;

        // Defer one frame so every other _Ready (walls, occluders, groups) ran.
        CallDeferred(MethodName.BakeNow);
    }

    /// <summary>Bakes the navigation mesh from all "nav_geometry" static bodies.</summary>
    public void BakeNow()
    {
        if (Region == null)
        {
            Region = new NavigationRegion3D { Name = "RuntimeNavRegion" };
            AddChild(Region);
        }

        var navMesh = new NavigationMesh
        {
            CellSize = CellSize,
            CellHeight = CellHeight,
            AgentRadius = AgentRadius,
            AgentHeight = AgentHeight,
            AgentMaxClimb = AgentMaxClimb,
            AgentMaxSlope = AgentMaxSlopeDeg,
            GeometryParsedGeometryType = NavigationMesh.ParsedGeometryType.StaticColliders,
            GeometrySourceGeometryMode = NavigationMesh.SourceGeometryMode.GroupsWithChildren,
            GeometrySourceGroupName = GeometryGroup,
            GeometryCollisionMask = 1,
        };

        Region.NavigationMesh = navMesh;

        int geometryNodes = GetTree().GetNodesInGroup(GeometryGroup).Count;
        if (geometryNodes == 0)
        {
            GD.PrintErr($"[NavigationBaker] No nodes in group '{GeometryGroup}' — navmesh left empty (direct steering only).");
            return;
        }

        Region.BakeNavigationMesh(onThread: false);

        int polygons = Region.NavigationMesh.GetPolygonCount();
        GD.Print($"[NavigationBaker] Baked navmesh from {geometryNodes} geometry node(s): {polygons} polygon(s).");
    }
}