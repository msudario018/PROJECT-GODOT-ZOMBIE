using Godot;
using ZombieApocalypse.Core.Utilities;
using ZombieApocalypse.Entities.Player;

namespace ZombieApocalypse.Systems.Building;

/// <summary>
/// Real-time grid-snapped building system.
/// 
/// Features:
///   - 2m grid snapping for defensive wall placement.
///   - Visual ghost preview with green/red placement validity detection.
///   - Hotkeys: [1] Scrap Wood Fence, [2] Chain Link Wall, [3] Reinforced Door, [R] Rotate, [RMB/Esc] Cancel.
///   - Instantly updates NavObstacles and triggers flow-field invalidation upon construction.
/// </summary>
public partial class BuildingSystem : Node3D
{
    [ExportGroup("Prefabs")]
    [Export] public PackedScene? ScrapWoodFenceScene;
    [Export] public PackedScene? ChainLinkWallScene;
    [Export] public PackedScene? DoorScene;

    [ExportGroup("Grid Tuning")]
    [Export] public float GridSnap = 2.0f;
    [Export(PropertyHint.Layers3DPhysics)] public uint ObstructionMask = 1 | 2 | 4; // World, Player, Zombies

    // Runtime state
    public int ActiveBuildType { get; private set; } = 0; // 0 = None, 1 = Wood, 2 = ChainLink, 3 = Door
    public float CurrentRotationDegrees { get; private set; } = 0f;
    public bool IsPlacementValid { get; private set; } = false;

    private BuildingGhost? _ghost;
    private PlayerController? _player;
    private Vector3 _currentSnappedPosition;

    public override void _Ready()
    {
        _player = GetTree().GetFirstNodeInGroup("player") as PlayerController;

        // Create ghost preview node
        _ghost = new BuildingGhost();
        _ghost.Visible = false;
        AddChild(_ghost);

        // Preload default scenes if unset
        ScrapWoodFenceScene ??= GD.Load<PackedScene>("res://scenes/world/defenses/walls/ScrapWoodFence.tscn");
        ChainLinkWallScene ??= GD.Load<PackedScene>("res://scenes/world/defenses/walls/ChainLinkWall.tscn");
        DoorScene ??= GD.Load<PackedScene>("res://scenes/world/defenses/walls/WoodenDoor.tscn");

        GD.Print("[BuildingSystem] Ready. Press 1, 2, or 3 to build structures.");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            switch (key.Keycode)
            {
                case Key.Key1:
                    SelectBuildType(1);
                    break;
                case Key.Key2:
                    SelectBuildType(2);
                    break;
                case Key.Key3:
                    SelectBuildType(3);
                    break;
                case Key.R:
                    if (ActiveBuildType > 0)
                    {
                        CurrentRotationDegrees = Mathf.PosMod(CurrentRotationDegrees + 90f, 360f);
                        if (_ghost != null) _ghost.RotationDegrees = new Vector3(0f, CurrentRotationDegrees, 0f);
                    }
                    break;
                case Key.Escape:
                    CancelBuilding();
                    break;
            }
        }
        else if (@event is InputEventMouseButton mouse && mouse.Pressed)
        {
            if (mouse.ButtonIndex == MouseButton.Right)
            {
                CancelBuilding();
            }
            else if (mouse.ButtonIndex == MouseButton.Left && ActiveBuildType > 0 && IsPlacementValid)
            {
                PlaceCurrentStructure();
            }
        }
    }

    public override void _Process(double delta)
    {
        if (ActiveBuildType == 0 || _ghost == null) return;

        _player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
        if (_player == null) return;

        Vector3 mouseWorld = _player.GetMouseWorldPosition();
        _currentSnappedPosition = MathUtils.SnapToGrid(mouseWorld, GridSnap);
        _currentSnappedPosition.Y = 0f;

        _ghost.GlobalPosition = _currentSnappedPosition;
        _ghost.RotationDegrees = new Vector3(0f, CurrentRotationDegrees, 0f);

        CheckPlacementValidity();
    }

    public void SelectBuildType(int type)
    {
        ActiveBuildType = type;
        if (_ghost != null)
        {
            _ghost.Visible = true;
            _ghost.SetDimensions(new Vector3(2.0f, 2.0f, 0.4f));
        }
        GD.Print($"[BuildingSystem] Selected structure type {type}. Left Click to place, R to rotate, RMB to cancel.");
    }

    public void CancelBuilding()
    {
        ActiveBuildType = 0;
        if (_ghost != null)
        {
            _ghost.Visible = false;
        }
    }

    private void CheckPlacementValidity()
    {
        var space = GetWorld3D()?.DirectSpaceState;
        if (space == null)
        {
            IsPlacementValid = false;
            return;
        }

        var shapeQuery = new PhysicsShapeQueryParameters3D();
        var box = new BoxShape3D { Size = new Vector3(1.8f, 1.8f, 0.35f) };
        shapeQuery.ShapeRid = box.GetRid();
        shapeQuery.CollisionMask = ObstructionMask;

        var basis = Basis.FromEuler(new Vector3(0f, Mathf.DegToRad(CurrentRotationDegrees), 0f));
        shapeQuery.Transform = new Transform3D(basis, _currentSnappedPosition + Vector3.Up * 1.0f);

        var hits = space.IntersectShape(shapeQuery, 1);
        IsPlacementValid = hits.Count == 0;

        _ghost?.SetPlacementValid(IsPlacementValid);
    }

    private void PlaceCurrentStructure()
    {
        PackedScene? sceneToPlace = ActiveBuildType switch
        {
            1 => ScrapWoodFenceScene,
            2 => ChainLinkWallScene,
            3 => DoorScene,
            _ => null
        };

        if (sceneToPlace == null)
        {
            GD.PrintErr($"[BuildingSystem] Scene for type {ActiveBuildType} is null!");
            return;
        }

        var instance = sceneToPlace.Instantiate<Node3D>();
        instance.Position = _currentSnappedPosition;
        instance.RotationDegrees = new Vector3(0f, CurrentRotationDegrees, 0f);

        GetTree().CurrentScene.AddChild(instance);
        GD.Print($"[BuildingSystem] Successfully placed structure at {_currentSnappedPosition}.");
    }
}
