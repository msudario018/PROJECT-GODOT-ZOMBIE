using Godot;
using System;
using System.Collections.Generic;
using ZombieApocalypse.Core.Data;

namespace ZombieApocalypse.Core.Vision;

/// <summary>
/// Manages the fog-of-war visibility grid.
///
/// Each cell tracks a visibility value:
///   1.0 = Hidden (never seen, fully opaque black fog)
///   0.65 = Explored (previously seen, dimmed — terrain visible but no live entities)
///   0.0 = Visible (currently in player's field of view, full clarity)
///
/// Updated each physics frame by <see cref="FieldOfView"/>, which reports
/// which cells are currently within the player's vision cone.
/// The system smoothly lerps cell values toward their targets for
/// fade-in/fade-out transitions.
///
/// Provides an <see cref="ImageTexture"/> consumed by <see cref="FogOfWarRenderer"/>.
/// </summary>
public partial class FogOfWarSystem : Node
{
    // ── Configuration ──────────────────────────────────────────────
    [ExportGroup("Grid")]
    [Export] public float CellSize = 1.0f;
    [Export] public Vector2 WorldOrigin = new(-25f, -25f);
    [Export] public Vector2 WorldSize = new(50f, 50f);

    [ExportGroup("Transitions")]
    [Export] public float RevealSpeed = 20.0f;
    [Export] public float HideSpeed = 4.0f;
    [Export] public float ExploredAlpha = 0.65f;

    // ── Public State ───────────────────────────────────────────────
    public static FogOfWarSystem? Instance { get; private set; }

    /// <summary>The GPU texture updated each frame. Consumed by FogOfWarRenderer's shader.</summary>
    public ImageTexture? FogTexture { get; private set; }

    /// <summary>Grid width in cells.</summary>
    public int GridWidth { get; private set; }
    /// <summary>Grid height in cells.</summary>
    public int GridHeight { get; private set; }

    /// <summary>
    /// When frozen the visibility mask stops animating and holds whatever was
    /// last revealed. Set on player death so the scene stays visible.
    /// </summary>
    public bool IsFrozen { get; private set; }

    // ── Internal State ─────────────────────────────────────────────
    private float[,] _currentAlpha = null!;  // Current visual alpha (0=visible, 1=hidden)
    private float[,] _targetAlpha = null!;   // Target alpha to lerp toward
    private bool[,] _isCurrentlyVisible = null!; // Which cells were marked visible this frame
    private Image _fogImage = null!;
    private bool _dirty;

    public override void _Ready()
    {
        Instance = this;

        GridWidth = Mathf.Max(1, Mathf.CeilToInt(WorldSize.X / CellSize));
        GridHeight = Mathf.Max(1, Mathf.CeilToInt(WorldSize.Y / CellSize));

        _currentAlpha = new float[GridWidth, GridHeight];
        _targetAlpha = new float[GridWidth, GridHeight];
        _isCurrentlyVisible = new bool[GridWidth, GridHeight];

        // Initialize everything as hidden
        for (int x = 0; x < GridWidth; x++)
        for (int y = 0; y < GridHeight; y++)
        {
            _currentAlpha[x, y] = 1.0f;
            _targetAlpha[x, y] = 1.0f;
        }

        // Create the fog texture (single-channel R8: R = fog opacity)
        _fogImage = Image.CreateEmpty(GridWidth, GridHeight, false, Image.Format.R8);
        _fogImage.Fill(new Color(1f, 0f, 0f)); // Start fully hidden
        FogTexture = ImageTexture.CreateFromImage(_fogImage);

        // Instantly reveal starting area around spawn (world center 0,0)
        RevealArea(Vector3.Zero, 6.0f);

        GD.Print($"[FogOfWarSystem] Grid: {GridWidth}×{GridHeight} cells " +
                 $"(cell={CellSize}m, world={WorldSize.X}×{WorldSize.Y}m). Starting area revealed.");
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void _Process(double delta)
    {
        // Frozen = hold the last revealed mask (used on death, so the arena stays
        // legible behind the death banner instead of fading to black).
        if (IsFrozen)
        {
            if (_dirty)
            {
                UpdateFogTexture();
                _dirty = false;
            }
            return;
        }

        float dt = (float)delta;
        bool anyChanged = false;

        // Lerp current alpha toward target alpha
        for (int x = 0; x < GridWidth; x++)
        for (int y = 0; y < GridHeight; y++)
        {
            float target = _targetAlpha[x, y];
            float current = _currentAlpha[x, y];

            if (Mathf.Abs(current - target) < 0.005f)
            {
                if (current != target)
                {
                    _currentAlpha[x, y] = target;
                    anyChanged = true;
                }
                continue;
            }

            // Use different speeds for revealing vs hiding
            float speed = target < current ? RevealSpeed : HideSpeed;
            _currentAlpha[x, y] = Mathf.MoveToward(current, target, speed * dt);
            anyChanged = true;
        }

        if (anyChanged || _dirty)
        {
            UpdateFogTexture();
            _dirty = false;
        }
    }

    // ════════════════════════════════════════════════════════════════
    // API — called by FieldOfView each physics frame
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Update visibility with the set of cells currently in the player's field of view.
    /// Cells not in the set transition to "explored" if they were previously visible.
    /// </summary>
    public void UpdateVisibility(HashSet<Vector2I> visibleCells)
    {
        // Reset "currently visible" flags
        for (int x = 0; x < GridWidth; x++)
        for (int y = 0; y < GridHeight; y++)
        {
            if (_isCurrentlyVisible[x, y])
            {
                // Was visible last frame, now transitioning to explored
                _targetAlpha[x, y] = ExploredAlpha;
                _isCurrentlyVisible[x, y] = false;
            }
        }

        // Mark reported cells as visible
        foreach (var cell in visibleCells)
        {
            if (!IsValidCell(cell)) continue;
            _targetAlpha[cell.X, cell.Y] = 0f;
            _isCurrentlyVisible[cell.X, cell.Y] = true;
        }

        _dirty = true;
    }

    /// <summary>Force-reveal a cell permanently (e.g., for map markers, debug).</summary>
    public void RevealCell(Vector2I cell)
    {
        if (!IsValidCell(cell)) return;
        _targetAlpha[cell.X, cell.Y] = 0f;
        _currentAlpha[cell.X, cell.Y] = 0f;
        _dirty = true;
    }

    /// <summary>Stop animating the mask and hold the last revealed state.</summary>
    public void Freeze() => IsFrozen = true;

    /// <summary>Resume animating the mask (new run, load, respawn).</summary>
    public void Unfreeze() => IsFrozen = false;

    /// <summary>
    /// Reveal an area of cells around a world position.
    /// </summary>
    public void RevealArea(Vector3 center, float radius)
    {
        int cellRadius = Mathf.CeilToInt(radius / CellSize);
        var centerCell = WorldToCell(center);
        float radiusSq = radius * radius;

        for (int dx = -cellRadius; dx <= cellRadius; dx++)
        for (int dy = -cellRadius; dy <= cellRadius; dy++)
        {
            var cell = new Vector2I(centerCell.X + dx, centerCell.Y + dy);
            if (!IsValidCell(cell)) continue;

            float worldX = WorldOrigin.X + (cell.X + 0.5f) * CellSize;
            float worldZ = WorldOrigin.Y + (cell.Y + 0.5f) * CellSize;
            float distSq = (worldX - center.X) * (worldX - center.X) + (worldZ - center.Z) * (worldZ - center.Z);

            if (distSq <= radiusSq)
            {
                _targetAlpha[cell.X, cell.Y] = 0f;
                _currentAlpha[cell.X, cell.Y] = 0f;
                _isCurrentlyVisible[cell.X, cell.Y] = true;
                _fogImage?.SetPixel(cell.X, cell.Y, new Color(0f, 0f, 0f));
            }
        }

        if (FogTexture != null && _fogImage != null)
        {
            FogTexture.Update(_fogImage);
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Coordinate Helpers
    // ════════════════════════════════════════════════════════════════

    /// <summary>Convert a world position to a fog grid cell coordinate.</summary>
    public Vector2I WorldToCell(Vector3 worldPos)
    {
        int x = Mathf.FloorToInt((worldPos.X - WorldOrigin.X) / CellSize);
        int y = Mathf.FloorToInt((worldPos.Z - WorldOrigin.Y) / CellSize);
        return new Vector2I(x, y);
    }

    /// <summary>Check if a cell coordinate is within the grid bounds.</summary>
    public bool IsValidCell(Vector2I cell)
    {
        return cell.X >= 0 && cell.X < GridWidth &&
               cell.Y >= 0 && cell.Y < GridHeight;
    }

    /// <summary>Get the current visibility state of a cell.</summary>
    public VisibilityState GetCellState(Vector2I cell)
    {
        if (!IsValidCell(cell)) return VisibilityState.Hidden;
        float alpha = _currentAlpha[cell.X, cell.Y];
        if (alpha < 0.1f) return VisibilityState.Visible;
        if (alpha < 0.9f) return VisibilityState.Explored;
        return VisibilityState.Hidden;
    }

    // ════════════════════════════════════════════════════════════════
    // Save / Load — explored cells are run-length encoded ("x,y:len;...")
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Serialise every explored (non-hidden) cell in row-major order, run-length
    /// encoded. A 50x50 grid collapses from thousands of entries to a short string.
    /// </summary>
    public string ExportExplored()
    {
        var runs = new System.Text.StringBuilder();
        int runLength = 0;
        int runStart = -1;

        for (int y = 0; y < GridHeight; y++)
        for (int x = 0; x < GridWidth; x++)
        {
            bool explored = _currentAlpha[x, y] < 0.9f;

            if (explored)
            {
                if (runLength == 0) runStart = y * GridWidth + x;
                runLength++;
                continue;
            }

            if (runLength > 0)
            {
                if (runs.Length > 0) runs.Append(';');
                runs.Append($"{runStart / GridWidth},{runStart % GridWidth}:{runLength}");
                runLength = 0;
            }
        }

        if (runLength > 0)
        {
            if (runs.Length > 0) runs.Append(';');
            runs.Append($"{runStart / GridWidth},{runStart % GridWidth}:{runLength}");
        }

        return runs.ToString();
    }

    /// <summary>Restore explored cells from <see cref="ExportExplored"/> output.</summary>
    public int ImportExplored(string encoded)
    {
        if (string.IsNullOrEmpty(encoded)) return 0;

        int revealed = 0;
        foreach (var run in encoded.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = run.Split(':');
            if (parts.Length != 2) continue;

            var coords = parts[0].Split(',');
            if (coords.Length != 2) continue;
            if (!int.TryParse(coords[0], out int startX)) continue;
            if (!int.TryParse(coords[1], out int startY)) continue;
            if (!int.TryParse(parts[1], out int length)) continue;

            for (int i = 0; i < length; i++)
            {
                int index = i + startY * GridWidth + startX;
                int x = index % GridWidth;
                int y = index / GridWidth;
                if (!IsValidCell(new Vector2I(x, y))) continue;

                _currentAlpha[x, y] = ExploredAlpha;
                _targetAlpha[x, y] = ExploredAlpha;
                revealed++;
            }
        }

        _dirty = true;
        if (FogTexture != null && _fogImage != null)
            FogTexture.Update(_fogImage);

        return revealed;
    }

    // ════════════════════════════════════════════════════════════════
    // Texture Update
    // ════════════════════════════════════════════════════════════════

    private void UpdateFogTexture()
    {
        for (int x = 0; x < GridWidth; x++)
        for (int y = 0; y < GridHeight; y++)
        {
            float alpha = _currentAlpha[x, y];
            _fogImage.SetPixel(x, y, new Color(alpha, 0f, 0f));
        }

        FogTexture!.Update(_fogImage);
    }
}
