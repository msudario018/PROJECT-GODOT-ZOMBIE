using Godot;

namespace ZombieApocalypse.Systems.Building;

/// <summary>
/// Visual placement ghost for structural preview and snapping.
/// Displays a translucent box colored green for valid placement and red for obstructed locations.
/// </summary>
public partial class BuildingGhost : Node3D
{
    private MeshInstance3D _meshInstance = null!;
    private StandardMaterial3D _material = null!;

    private readonly Color ValidColor = new(0.2f, 0.95f, 0.35f, 0.45f);
    private readonly Color InvalidColor = new(0.95f, 0.25f, 0.25f, 0.55f);

    public override void _Ready()
    {
        _meshInstance = new MeshInstance3D();
        var boxMesh = new BoxMesh { Size = new Vector3(2.0f, 2.0f, 0.4f) };
        _meshInstance.Mesh = boxMesh;
        _meshInstance.Position = new Vector3(0f, 1.0f, 0f);

        _material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = ValidColor
        };
        _meshInstance.MaterialOverride = _material;

        AddChild(_meshInstance);
    }

    /// <summary>
    /// Update ghost visual validity color.
    /// </summary>
    public void SetPlacementValid(bool isValid)
    {
        if (_material != null)
        {
            _material.AlbedoColor = isValid ? ValidColor : InvalidColor;
        }
    }

    /// <summary>
    /// Set preview dimensions based on selected structure type.
    /// </summary>
    public void SetDimensions(Vector3 size)
    {
        if (_meshInstance?.Mesh is BoxMesh box)
        {
            box.Size = size;
            _meshInstance.Position = new Vector3(0f, size.Y * 0.5f, 0f);
        }
    }
}
