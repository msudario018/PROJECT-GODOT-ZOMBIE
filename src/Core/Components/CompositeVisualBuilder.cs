namespace ZombieApocalypse.Core.Components;

using Godot;
using System.Collections.Generic;

/// <summary>Styling knobs for a composite character model.</summary>
public struct CompositeStyle
{
    public Color Torso;
    public Color Head;
    public Color Limbs;
    public Color Accent;
    public float Height;          // metres, feet at Y=0
    public float Bulk;            // torso width/depth multiplier
    public float LeanDegrees;     // forward hunch
    public bool Spherical;        // bloater-style bulbous body

    /// <summary>Survivor green, upright and alert.</summary>
    public static CompositeStyle Survivor => new()
    {
        Torso = new Color(0.20f, 0.52f, 0.30f),
        Head = new Color(0.85f, 0.68f, 0.52f),
        Limbs = new Color(0.16f, 0.38f, 0.24f),
        Accent = new Color(0.90f, 0.85f, 0.35f),
        Height = 1.8f,
        Bulk = 1.0f,
        LeanDegrees = 0f,
    };

    /// <summary>Shambler: hunched, dark green, arms forward.</summary>
    public static CompositeStyle Shambler => new()
    {
        Torso = new Color(0.16f, 0.26f, 0.16f),
        Head = new Color(0.22f, 0.34f, 0.20f),
        Limbs = new Color(0.13f, 0.21f, 0.13f),
        Accent = new Color(0.75f, 0.85f, 0.45f),   // glinting eyes
        Height = 1.75f,
        Bulk = 1.05f,
        LeanDegrees = 16f,
    };

    /// <summary>Sprinter: lean, red-brown, pitched forward.</summary>
    public static CompositeStyle Sprinter => new()
    {
        Torso = new Color(0.42f, 0.18f, 0.13f),
        Head = new Color(0.52f, 0.28f, 0.18f),
        Limbs = new Color(0.34f, 0.14f, 0.10f),
        Accent = new Color(1.0f, 0.55f, 0.15f),
        Height = 1.7f,
        Bulk = 0.8f,
        LeanDegrees = 26f,
    };

    /// <summary>Bloater: bulbous, sickly green, barely upright.</summary>
    public static CompositeStyle Bloater => new()
    {
        Torso = new Color(0.30f, 0.40f, 0.22f),
        Head = new Color(0.38f, 0.48f, 0.26f),
        Limbs = new Color(0.24f, 0.32f, 0.18f),
        Accent = new Color(0.55f, 0.85f, 0.30f),
        Height = 1.7f,
        Bulk = 1.9f,
        LeanDegrees = 6f,
        Spherical = true,
    };

    /// <summary>Brute: heavy, grey, wide stance.</summary>
    public static CompositeStyle Brute => new()
    {
        Torso = new Color(0.30f, 0.30f, 0.33f),
        Head = new Color(0.36f, 0.36f, 0.38f),
        Limbs = new Color(0.24f, 0.24f, 0.27f),
        Accent = new Color(0.85f, 0.35f, 0.25f),
        Height = 2.5f,
        Bulk = 1.7f,
        LeanDegrees = 10f,
    };

    /// <summary>Screamer: pale, gaunt, upright.</summary>
    public static CompositeStyle Screamer => new()
    {
        Torso = new Color(0.78f, 0.74f, 0.72f),
        Head = new Color(0.85f, 0.80f, 0.78f),
        Limbs = new Color(0.68f, 0.65f, 0.63f),
        Accent = new Color(0.95f, 0.25f, 0.25f),
        Height = 1.9f,
        Bulk = 0.75f,
        LeanDegrees = 4f,
    };

    /// <summary>Hound: low, fast, rust-red.</summary>
    public static CompositeStyle Hound => new()
    {
        Torso = new Color(0.36f, 0.15f, 0.12f),
        Head = new Color(0.44f, 0.20f, 0.14f),
        Limbs = new Color(0.28f, 0.12f, 0.10f),
        Accent = new Color(1.0f, 0.40f, 0.20f),
        Height = 1.1f,
        Bulk = 0.7f,
        LeanDegrees = 12f,
    };

    /// <summary>Pick a style from an archetype name (case-insensitive).</summary>
    public static CompositeStyle ForArchetype(string archetypeName) => archetypeName.ToLowerInvariant() switch
    {
        "sprinter" => Sprinter,
        "bloater" => Bloater,
        "brute" => Brute,
        "screamer" => Screamer,
        "hound" => Hound,
        "survivor" => Survivor,
        _ => Shambler,
    };

    /// <summary>Inspector-friendly presets (custom structs cannot be exported).</summary>
    public enum Preset
    {
        Survivor,
        Shambler,
        Sprinter,
        Bloater,
        Brute,
        Screamer,
        Hound,
    }

    /// <summary>Resolve an inspector preset to its style.</summary>
    public static CompositeStyle ForPreset(Preset preset) => preset switch
    {
        Preset.Sprinter => Sprinter,
        Preset.Bloater => Bloater,
        Preset.Brute => Brute,
        Preset.Screamer => Screamer,
        Preset.Hound => Hound,
        Preset.Shambler => Shambler,
        _ => Survivor,
    };
}

/// <summary>
/// Builds a stylised composite character out of primitive boxes/spheres.
///
/// Cheaper than an imported capsule and far more readable from the isometric
/// camera — you can tell a Shambler from a Sprinter at a glance. Everything is
/// parented to a <c>CharacterVisual</c> pivot, so the collision capsule and the
/// AI never move; only the model does.
///
/// Layout (feet at Y=0):
///   torso (leaning) ── head (+ accent eyes)
///   arms (forward)   legs
/// plus an optional weapon marker under the right hand.
/// </summary>
public static class CompositeVisualBuilder
{
    /// <summary>
    /// Build the model into a pivot and return the created root. Existing
    /// children are cleared first, so this is safe to re-run on reload.
    /// </summary>
    public static Node3D Build(Node3D pivot, CompositeStyle style, bool withWeaponMarker = false)
    {
        // Remove immediately as well as queueing the free: QueueFree alone is
        // deferred, so a rebuild would briefly hold two models.
        foreach (var child in pivot.GetChildren())
        {
            pivot.RemoveChild(child);
            child.QueueFree();
        }

        var root = new Node3D { Name = "Composite" };
        pivot.AddChild(root);

        float height = Mathf.Max(0.6f, style.Height);
        float bulk = Mathf.Max(0.4f, style.Bulk);

        // Torso: the visual anchor, leaned forward by style.
        float torsoHeight = height * 0.42f;
        var torso = new Node3D { Name = "Torso", Position = new Vector3(0f, height * 0.55f, 0f) };
        torso.RotationDegrees = new Vector3(style.LeanDegrees, 0f, 0f);
        root.AddChild(torso);

        // Bloaters get an oversized sphere instead of a box torso.
        torso.AddChild(MeshPart(
            style.Spherical
                ? new SphereMesh { Radius = 0.42f * bulk, Height = 0.84f * bulk, RadialSegments = 14, Rings = 8 }
                : new BoxMesh { Size = new Vector3(0.46f * bulk, torsoHeight, 0.30f * bulk) },
            style.Torso, Vector3.Zero));

        // Head with accent-coloured eyes.
        var head = new Node3D
        {
            Name = "Head",
            Position = new Vector3(0f, height * 0.55f + torsoHeight * 0.5f + 0.16f * bulk, -0.02f),
        };
        head.RotationDegrees = new Vector3(style.LeanDegrees * 0.5f, 0f, 0f);
        root.AddChild(head);

        head.AddChild(MeshPart(new BoxMesh { Size = new Vector3(0.26f * bulk, 0.26f, 0.24f * bulk) },
            style.Head, Vector3.Zero));
        head.AddChild(MeshPart(new BoxMesh { Size = new Vector3(0.18f, 0.05f, 0.03f) },
            style.Accent, new Vector3(0f, 0.02f, -0.13f * bulk)));

        // Arms reach forward, which reads as "shambling" at a distance.
        float armLength = height * 0.34f;
        var armRoot = new Node3D { Name = "Arms", Position = new Vector3(0f, height * 0.72f, 0f) };
        root.AddChild(armRoot);

        armRoot.AddChild(MeshPart(new BoxMesh { Size = new Vector3(0.11f, armLength, 0.11f) },
            style.Limbs, new Vector3(0.29f * bulk, -armLength * 0.35f, -0.12f)));
        armRoot.AddChild(MeshPart(new BoxMesh { Size = new Vector3(0.11f, armLength, 0.11f) },
            style.Limbs, new Vector3(-0.29f * bulk, -armLength * 0.35f, -0.12f)));

        // Legs.
        float legLength = height * 0.42f;
        var legRoot = new Node3D { Name = "Legs", Position = new Vector3(0f, height * 0.40f, 0f) };
        root.AddChild(legRoot);

        legRoot.AddChild(MeshPart(new BoxMesh { Size = new Vector3(0.14f, legLength, 0.14f) },
            style.Limbs, new Vector3(0.13f * bulk, -legLength * 0.5f, 0f)));
        legRoot.AddChild(MeshPart(new BoxMesh { Size = new Vector3(0.14f, legLength, 0.14f) },
            style.Limbs, new Vector3(-0.13f * bulk, -legLength * 0.5f, 0f)));

        if (withWeaponMarker)
        {
            // A stub for the gun, so the weapon has something to hang off.
            root.AddChild(new Node3D
            {
                Name = "WeaponMarker",
                Position = new Vector3(0.30f * bulk, height * 0.70f, -0.30f),
            });
        }

        return root;
    }

    private static MeshInstance3D MeshPart(Mesh mesh, Color colour, Vector3 position)
        => new()
        {
            Mesh = mesh,
            Position = position,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = colour,
                Roughness = 0.75f,
                Metallic = 0.05f,
            },
        };
}
