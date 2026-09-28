using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Entities.Player;

namespace ZombieApocalypse.Core.Vision;

/// <summary>
/// Computes the player's field of view by raycasting against vision-blocking
/// geometry. Reports visible cells to <see cref="FogOfWarSystem"/> each physics frame.
///
/// Vision model:
///   - Base vision cone: configurable angle and range (default 120°, 15m)
///   - Flashlight cone: narrow, long range (30°, 25m), toggled by player
///   - Proximity circle: 360° always-visible close range (3m)
///   - Rays are cast on collision layer 4 (vision blockers)
///   - Facing direction follows the mouse cursor (via PlayerController.GetAimDirection)
///
/// Attach as a child of the Player CharacterBody3D.
/// </summary>
public partial class FieldOfView : Node3D
{
    // ── Configuration ──────────────────────────────────────────────
    [ExportGroup("Vision Cone")]
    [Export] public float VisionRange = 15f;
    [Export] public float VisionAngle = 120f;
    [Export] public int VisionRayCount = 72;

    [ExportGroup("Flashlight")]
    [Export] public float FlashlightRange = 25f;
    [Export] public float FlashlightAngle = 30f;
    [Export] public int FlashlightRayCount = 24;

    [ExportGroup("Proximity")]
    [Export] public float ProximityRadius = 3.0f;

    [ExportGroup("Raycasting")]
    [Export] public float RayHeight = 1.0f;

    /// <summary>Whether the flashlight is currently active.</summary>
    public bool IsFlashlightActive { get; set; }

    // ── References ─────────────────────────────────────────────────
    private FogOfWarSystem? _fogSystem;
    private PlayerController? _player;

    // ── Constants ──────────────────────────────────────────────────
    /// <summary>Collision layer 4 bitmask for vision-blocking geometry.</summary>
    private const uint VisionBlockerMask = 1 << 3; // Layer 4 = bit 3

    public override void _Ready()
    {
        _player = GetParent() as PlayerController;
        _fogSystem = FogOfWarSystem.Instance ?? FindFogOfWarSystem();

        if (_fogSystem != null && _player != null)
        {
            _fogSystem.RevealArea(_player.GlobalPosition, ProximityRadius + 5.0f);
        }
        else if (_fogSystem == null)
        {
            GD.Print("[FieldOfView] FogOfWarSystem not found initially — will retry in _PhysicsProcess.");
        }

        if (_player == null)
            GD.PrintErr("[FieldOfView] Parent is not PlayerController!");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.F1)
        {
            FogOfWarRenderer.Instance?.ToggleFog();
        }

        if (@event.IsActionPressed("flashlight"))
        {
            IsFlashlightActive = !IsFlashlightActive;
            GD.Print($"[FieldOfView] Flashlight {(IsFlashlightActive ? "ON" : "OFF")}");
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _fogSystem ??= FogOfWarSystem.Instance ?? FindFogOfWarSystem();
        if (_fogSystem == null || _player == null) return;

        var playerPos = _player.GlobalPosition;
        var aimDir = _player.GetAimDirection();
        float facingAngle = Mathf.Atan2(aimDir.X, aimDir.Z);

        var visibleCells = new HashSet<Vector2I>();
        var spaceState = GetWorld3D().DirectSpaceState;

        // 1. Always-visible proximity circle (360°, short range)
        AddCellsInRadius(playerPos, ProximityRadius, visibleCells);

        // 2. Main vision cone
        CastCone(spaceState, playerPos, facingAngle, VisionAngle, VisionRange, VisionRayCount, visibleCells);

        // 3. Optional flashlight cone (narrow, long)
        if (IsFlashlightActive)
            CastCone(spaceState, playerPos, facingAngle, FlashlightAngle, FlashlightRange, FlashlightRayCount, visibleCells);

        _fogSystem.UpdateVisibility(visibleCells);
    }

    // ════════════════════════════════════════════════════════════════
    // Raycasting
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Cast a cone of rays from the player's position, adding all visible cells.
    /// Rays stop at vision-blocking geometry (collision layer 4).
    /// </summary>
    private void CastCone(PhysicsDirectSpaceState3D spaceState, Vector3 origin,
                          float centerAngle, float coneAngle, float range,
                          int rayCount, HashSet<Vector2I> cells)
    {
        float halfAngle = Mathf.DegToRad(coneAngle * 0.5f);
        float totalAngle = Mathf.DegToRad(coneAngle);
        float angleStep = rayCount > 1 ? totalAngle / (rayCount - 1) : 0f;
        float startAngle = centerAngle - halfAngle;

        var rayOrigin = new Vector3(origin.X, RayHeight, origin.Z);

        for (int i = 0; i < rayCount; i++)
        {
            float angle = startAngle + angleStep * i;
            var dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            var rayEnd = rayOrigin + dir * range;

            // Cast ray against vision blockers only
            var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd, VisionBlockerMask);
            var result = spaceState.IntersectRay(query);

            // Determine effective range (hit = blocked, no hit = full range)
            float effectiveRange = range;
            if (result.Count > 0)
            {
                var hitPos = (Vector3)result["position"];
                effectiveRange = new Vector3(hitPos.X - origin.X, 0f, hitPos.Z - origin.Z).Length();
            }

            // Walk along the ray and add cells
            AddCellsAlongRay(origin, angle, effectiveRange, cells);
        }
    }

    /// <summary>
    /// Walk along a ray direction from origin, adding every fog grid cell the ray passes through.
    /// Uses half-cell-size steps for reliable coverage.
    /// </summary>
    private void AddCellsAlongRay(Vector3 origin, float angle, float distance, HashSet<Vector2I> cells)
    {
        float step = _fogSystem!.CellSize * 0.5f;
        int steps = Mathf.CeilToInt(distance / step);
        var dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

        for (int i = 0; i <= steps; i++)
        {
            var pos = origin + dir * (step * i);
            var cell = _fogSystem.WorldToCell(pos);
            if (_fogSystem.IsValidCell(cell))
                cells.Add(cell);
        }
    }

    /// <summary>
    /// Add all cells within a radius of a position (used for proximity vision).
    /// </summary>
    private void AddCellsInRadius(Vector3 center, float radius, HashSet<Vector2I> cells)
    {
        int cellRadius = Mathf.CeilToInt(radius / _fogSystem!.CellSize);
        var centerCell = _fogSystem.WorldToCell(center);

        for (int dx = -cellRadius; dx <= cellRadius; dx++)
        for (int dy = -cellRadius; dy <= cellRadius; dy++)
        {
            var cell = new Vector2I(centerCell.X + dx, centerCell.Y + dy);
            if (_fogSystem.IsValidCell(cell))
                cells.Add(cell);
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Discovery
    // ════════════════════════════════════════════════════════════════

    private FogOfWarSystem? FindFogOfWarSystem()
    {
        // Search the scene tree for the FogOfWarSystem node
        var root = GetTree()?.Root;
        if (root == null) return null;
        return root.FindChild("FogOfWarSystem", true, false) as FogOfWarSystem;
    }
}
