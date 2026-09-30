namespace ZombieApocalypse.Core.Components;

using Godot;
using System.Collections.Generic;

/// <summary>
/// Visual pivot for any character body (player, zombie, survivor, bandit).
///
/// Separates *presentation* from *simulation*: the CharacterBody3D and its
/// CollisionShape3D stay fixed, while everything visual hangs off a "Visual"
/// pivot that owns rotation, hit-flash and animation. That lets the capsule
/// primitives be swapped for imported .glb models without touching physics or
/// AI code.
///
/// Node contract (see the .tscn files):
///   Visual                      Node3D   — the pivot this component drives
///     Visual/ModelSlot          Node3D   — drop an imported model here
///     Visual/PrimitiveFallback  Node3D   — capsule stand-in, hidden when a model loads
///     Visual/RightHandSocket    Marker3D — weapon attachment
///     Visual/LeftHandSocket     Marker3D — offhand / flashlight attachment
///
/// Missing nodes are created on demand, so a scene only needs the ones it cares
/// about and everything else is built at runtime.
/// </summary>
public partial class CharacterVisual : Node3D
{
    [ExportGroup("Model")]
    /// <summary>Optional .glb/.gltf scene instantiated into the model slot.</summary>
    [Export] public PackedScene? ModelScene;
    /// <summary>Hide the primitive stand-in once a model is present.</summary>
    [Export] public bool HideFallbackWhenModelLoaded = true;
    /// <summary>Uniform scale applied to an imported model.</summary>
    [Export] public float ModelScale = 1.0f;
    /// <summary>Build a stylised composite model (torso/head/arms/legs) instead of the
    /// capsule stand-in. Ignored once an imported model is loaded.</summary>
    [Export] public bool UseCompositeModel = true;
    /// <summary>Styling preset for the composite build.</summary>
    [Export] public CompositeStyle.Preset StylePreset = CompositeStyle.Preset.Survivor;

    /// <summary>Resolved style for the current build (set by archetype code).</summary>
    public CompositeStyle Style { get; private set; } = CompositeStyle.Survivor;

    [ExportGroup("Hit Flash")]
    [Export] public Color FlashColor = new(1f, 0.25f, 0.2f);
    [Export] public float FlashDuration = 0.12f;
    [Export] public float FlashIntensity = 2.5f;

    [ExportGroup("Sockets")]
    [Export] public Vector3 RightHandOffset = new(0.32f, 1.1f, -0.15f);
    [Export] public Vector3 LeftHandOffset = new(-0.32f, 1.1f, -0.15f);

    [Signal] public delegate void ModelSwappedEventHandler(bool usingImportedModel);
    [Signal] public delegate void HitFlashedEventHandler();

    private Node3D? _pivot;
    private Node3D? _modelSlot;
    private Node3D? _fallback;
    private Marker3D? _rightHand;
    private Marker3D? _leftHand;
    private readonly List<MeshInstance3D> _meshes = new();
    private readonly List<Material> _originalMaterials = new();

    private float _flashTimer;
    private Node3D? _attachedWeapon;

    /// <summary>The Visual pivot this component rotates.</summary>
    public Node3D? Pivot => _pivot;
    /// <summary>Where imported models are parented.</summary>
    public Node3D? ModelSlot => _modelSlot;
    /// <summary>The primitive stand-in (hidden when a model is loaded).</summary>
    public Node3D? Fallback => _fallback;
    /// <summary>Weapon attachment point.</summary>
    public Marker3D? RightHandSocket => _rightHand;
    /// <summary>Offhand / flashlight attachment point.</summary>
    public Marker3D? LeftHandSocket => _leftHand;
    /// <summary>True when a right-hand weapon socket exists.</summary>
    public bool RightHandSocketVisible => _rightHand != null;
    /// <summary>True when a left-hand socket exists.</summary>
    public bool LeftHandSocketVisible => _leftHand != null;
    /// <summary>True once an imported model replaced the primitive.</summary>
    public bool IsUsingImportedModel { get; private set; }
    /// <summary>True while the hit flash is active.</summary>
    public bool IsFlashing => _flashTimer > 0f;
    /// <summary>Weapon currently parented to the right-hand socket.</summary>
    public Node3D? AttachedWeapon => _attachedWeapon;
    /// <summary>First mesh under the pivot — the node hit-flash and facing target.</summary>
    public MeshInstance3D? PrimaryMesh => _meshes.Count > 0 ? _meshes[0] : null;

    /// <summary>Every mesh under the pivot (model or fallback).</summary>
    public IReadOnlyList<MeshInstance3D> Meshes => _meshes;

    public override void _Ready()
    {
        BuildHierarchy();
        CacheMeshes();

        if (ModelScene != null)
            LoadModel(ModelScene);
        else if (UseCompositeModel && _fallback != null)
            BuildComposite(CompositeStyle.ForPreset(StylePreset));
    }

    /// <summary>Root of the generated composite model, if one was built.</summary>
    public Node3D? CompositeRoot { get; private set; }

    /// <summary>
    /// Replace the primitive stand-in with a stylised composite. Safe to call
    /// again to restyle; the model is rebuilt from scratch each time.
    /// </summary>
    public Node3D? BuildComposite(CompositeStyle style)
    {
        if (_fallback == null) return null;

        Style = style;
        IsUsingImportedModel = false;
        _fallback.Visible = true;

        // Drop any previous model so restyling never stacks two.
        var previous = _fallback.GetNodeOrNull<Node3D>("CompositeHolder");
        if (previous != null)
        {
            _fallback.RemoveChild(previous);
            previous.QueueFree();
        }

        // The pivot rotates, so the model must be built flat under it.
        var root = new Node3D { Name = "CompositeHolder" };
        _fallback.AddChild(root);
        CompositeVisualBuilder.Build(root, style, withWeaponMarker: true);
        CompositeRoot = root;

        CacheMeshes();
        return root;
    }

    /// <summary>
    /// Create or adopt the Visual hierarchy. Idempotent, so it can run again
    /// after a scene reload without duplicating nodes.
    /// </summary>
    public void BuildHierarchy()
    {
        _pivot = GetNodeOrNull<Node3D>("Visual")
                  ?? GetParent()?.GetNodeOrNull<Node3D>("Visual");

        if (_pivot == null)
        {
            _pivot = new Node3D { Name = "Visual" };
            GetParent()?.AddChild(_pivot);
        }

        _modelSlot = EnsureChild<Node3D>(_pivot, "ModelSlot");
        _fallback = EnsureChild<Node3D>(_pivot, "PrimitiveFallback");

        _rightHand = EnsureChild<Marker3D>(_pivot, "RightHandSocket");
        if (_rightHand.Position == Vector3.Zero) _rightHand.Position = RightHandOffset;

        _leftHand = EnsureChild<Marker3D>(_pivot, "LeftHandSocket");
        if (_leftHand.Position == Vector3.Zero) _leftHand.Position = LeftHandOffset;
    }

    private static T EnsureChild<T>(Node3D pivot, string name) where T : Node3D, new()
    {
        var existing = pivot.GetNodeOrNull<T>(name);
        if (existing != null) return existing;

        var created = new T { Name = name };
        pivot.AddChild(created);
        return created;
    }

    /// <summary>Collect the meshes we tint, so the hit flash can restore them.</summary>
    public void CacheMeshes()
    {
        _meshes.Clear();
        _originalMaterials.Clear();

        if (_pivot == null) return;

        foreach (var node in _pivot.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is not MeshInstance3D mesh) continue;
            _meshes.Add(mesh);
            _originalMaterials.Add(mesh.MaterialOverride ?? mesh.GetSurfaceOverrideMaterial(0));
        }
    }

    /// <summary>
    /// Instantiate an imported model into the model slot and hide the primitive
    /// stand-in. Returns the instantiated root, or null when the scene is empty.
    /// </summary>
    public Node3D? LoadModel(PackedScene? scene)
    {
        if (scene == null || _modelSlot == null) return null;

        foreach (var child in _modelSlot.GetChildren())
            child.QueueFree();

        var instance = scene.Instantiate<Node3D>();
        if (instance == null) return null;

        _modelSlot.AddChild(instance);
        instance.Scale = Vector3.One * ModelScale;

        IsUsingImportedModel = true;
        if (HideFallbackWhenModelLoaded && _fallback != null)
            _fallback.Visible = false;

        CacheMeshes();
        EmitSignal(SignalName.ModelSwapped, true);
        GD.Print($"[CharacterVisual] Imported model '{scene.ResourceName}' into ModelSlot.");
        return instance;
    }

    /// <summary>Drop the imported model and fall back to the primitive stand-in.</summary>
    public void UsePrimitiveFallback()
    {
        if (_modelSlot != null)
            foreach (var child in _modelSlot.GetChildren())
                child.QueueFree();

        IsUsingImportedModel = false;
        if (_fallback != null) _fallback.Visible = true;

        CacheMeshes();
        EmitSignal(SignalName.ModelSwapped, false);
    }

    /// <summary>Flash every mesh under the pivot white-red for a moment.</summary>
    public void FlashHit()
    {
        _flashTimer = FlashDuration;
        ApplyFlash();
        EmitSignal(SignalName.HitFlashed);
    }

    public override void _Process(double delta)
    {
        if (_flashTimer <= 0f) return;

        _flashTimer -= (float)delta;
        if (_flashTimer <= 0f) RestoreMaterials();
    }

    private void ApplyFlash()
    {
        var flash = new StandardMaterial3D
        {
            AlbedoColor = FlashColor,
            EmissionEnabled = true,
            Emission = FlashColor,
            EmissionEnergyMultiplier = FlashIntensity,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

        for (int i = 0; i < _meshes.Count; i++)
        {
            _meshes[i].MaterialOverride = flash;
            _meshes[i].SetSurfaceOverrideMaterial(0, flash);
        }
    }

    private void RestoreMaterials()
    {
        for (int i = 0; i < _meshes.Count; i++)
        {
            var original = _originalMaterials[i];
            if (original == null) continue;

            _meshes[i].MaterialOverride = original;
            _meshes[i].SetSurfaceOverrideMaterial(0, original);
        }
    }

    /// <summary>Rotate the pivot to face a world direction (XZ only).</summary>
    public void FaceWorldDirection(Vector3 direction, float turnSpeed, float delta)
    {
        if (_pivot == null) return;

        direction.Y = 0f;
        if (direction.LengthSquared() < 0.001f) return;

        float target = Mathf.Atan2(direction.X, direction.Z);
        _pivot.Rotation = new Vector3(0f, Mathf.LerpAngle(_pivot.Rotation.Y, target, turnSpeed * delta), 0f);
    }

    /// <summary>Attach a weapon mesh to a hand socket, replacing any previous one.</summary>
    public Node3D? AttachWeapon(Node3D? weapon, bool leftHanded = false)
    {
        ClearWeapon();
        if (weapon == null) return null;

        var socket = leftHanded ? _leftHand : _rightHand;
        if (socket == null) return null;

        socket.AddChild(weapon);
        _attachedWeapon = weapon;
        return weapon;
    }

    /// <summary>Detach and free whatever is in the weapon socket.</summary>
    public void ClearWeapon()
    {
        if (_attachedWeapon == null) return;
        _attachedWeapon.QueueFree();
        _attachedWeapon = null;
    }
}
