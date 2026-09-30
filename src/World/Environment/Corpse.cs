using Godot;
using System;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Entities.Player;

namespace ZombieApocalypse.World.Environment;

/// <summary>
/// Lifecycle stages of a fallen corpse.
/// </summary>
public enum CorpseStage
{
    Fresh,          // 0 - 45s: newly fallen, lootable
    Bloated,        // 45 - 120s: swelling, foul odor
    RottingMiasma,  // 120 - 300s: active toxic miasma aura damaging nearby living beings
    Skeleton        // 300s+: decay complete, no miasma, slowly sinks and dissolves
}

/// <summary>
/// A fallen corpse in the world with an organic decay lifecycle:
///   - Rot progression (Fresh -> Bloated -> Rotting Miasma -> Skeleton -> Despawn)
///   - Miasma Aura: during RottingMiasma stage, damages players within MiasmaRadius.
///   - Disposal mechanics:
///       * Search / Loot: scavenge remaining cloth, ammo, or scrap
///       * Bury: cleanses the area and creates a peaceful grave mound
///       * Burn: incinerates with fire, destroying miasma immediately
/// </summary>
public partial class Corpse : StaticBody3D
{
    [Export] public float FreshDuration = 45f;
    [Export] public float BloatedDuration = 75f;
    [Export] public float MiasmaDuration = 180f;
    [Export] public float SkeletonDuration = 60f;

    [Export] public float MiasmaRadius = 4.0f;
    [Export] public float MiasmaDamagePerSecond = 3.5f;

    public CorpseStage CurrentStage { get; private set; } = CorpseStage.Fresh;
    public float AgeSeconds { get; private set; } = 0f;
    public bool IsSearched { get; private set; } = false;
    public bool IsBuried { get; private set; } = false;
    public bool IsBurning { get; private set; } = false;

    /// <summary>
    /// When true (set by a powered Freezer within range) the rot lifecycle is
    /// paused: the corpse never advances past its current stage and never
    /// starts emitting miasma.
    /// </summary>
    public bool IsFrozen { get; set; } = false;

    private MeshInstance3D? _bodyMesh;
    private MeshInstance3D? _miasmaCloudMesh;
    private OmniLight3D? _miasmaLight;
    private Label3D? _promptLabel;
    private StandardMaterial3D? _bodyMaterial;
    private StandardMaterial3D? _miasmaMaterial;

    private float _burnTimer = 0f;

    public override void _Ready()
    {
        AddToGroup("interactables");
        AddToGroup("corpses");

        CreateVisuals();
    }

    private void CreateVisuals()
    {
        // Body mesh (horizontal fallen capsule)
        _bodyMesh = new MeshInstance3D();
        var capsule = new CapsuleMesh
        {
            Radius = 0.32f,
            Height = 1.6f
        };
        _bodyMesh.Mesh = capsule;
        _bodyMesh.RotationDegrees = new Vector3(0, GD.RandRange(0, 360), 85);
        _bodyMesh.Position = new Vector3(0, 0.15f, 0);

        _bodyMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.35f, 0.38f, 0.32f),
            Roughness = 0.85f
        };
        _bodyMesh.MaterialOverride = _bodyMaterial;
        AddChild(_bodyMesh);

        // Miasma Aura Sphere (semitransparent noxious haze)
        _miasmaCloudMesh = new MeshInstance3D();
        var sphere = new SphereMesh
        {
            Radius = MiasmaRadius,
            Height = MiasmaRadius * 1.5f
        };
        _miasmaCloudMesh.Mesh = sphere;
        _miasmaCloudMesh.Position = new Vector3(0, 0.5f, 0);

        _miasmaMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.2f, 0.65f, 0.15f, 0.22f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        _miasmaCloudMesh.MaterialOverride = _miasmaMaterial;
        _miasmaCloudMesh.Visible = false;
        AddChild(_miasmaCloudMesh);

        // Miasma Glow Light
        _miasmaLight = new OmniLight3D
        {
            OmniRange = MiasmaRadius + 1.0f,
            LightColor = new Color(0.3f, 0.8f, 0.2f),
            LightEnergy = 0.8f,
            Visible = false
        };
        AddChild(_miasmaLight);

        // Prompt Label
        _promptLabel = new Label3D
        {
            Text = "[E] Inspect Corpse",
            Position = new Vector3(0, 1.2f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 20,
            Visible = false
        };
        AddChild(_promptLabel);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        if (IsBuried) return;

        if (IsBurning)
        {
            _burnTimer -= dt;
            if (_bodyMaterial != null)
            {
                _bodyMaterial.AlbedoColor = _bodyMaterial.AlbedoColor.Lerp(new Color(0.05f, 0.05f, 0.05f), dt * 2.0f);
            }
            if (_burnTimer <= 0f)
            {
                QueueFree();
            }
            return;
        }

        // Frozen by a powered freezer: rot progression halted entirely
        if (IsFrozen) return;

        AgeSeconds += dt;
        UpdateLifecycleStage(dt);

        if (CurrentStage == CorpseStage.RottingMiasma)
        {
            ApplyMiasmaDamage(dt);
        }
    }

    private void UpdateLifecycleStage(float dt)
    {
        float t = AgeSeconds;
        CorpseStage oldStage = CurrentStage;

        if (t < FreshDuration)
        {
            CurrentStage = CorpseStage.Fresh;
            if (_bodyMaterial != null)
                _bodyMaterial.AlbedoColor = new Color(0.38f, 0.42f, 0.35f);
            SetMiasmaActive(false);
        }
        else if (t < FreshDuration + BloatedDuration)
        {
            CurrentStage = CorpseStage.Bloated;
            if (_bodyMaterial != null)
                _bodyMaterial.AlbedoColor = new Color(0.48f, 0.45f, 0.28f);
            // Swell slightly
            if (_bodyMesh != null)
                _bodyMesh.Scale = _bodyMesh.Scale.Lerp(new Vector3(1.25f, 1.25f, 1.05f), dt * 0.5f);
            SetMiasmaActive(false);
        }
        else if (t < FreshDuration + BloatedDuration + MiasmaDuration)
        {
            CurrentStage = CorpseStage.RottingMiasma;
            if (_bodyMaterial != null)
                _bodyMaterial.AlbedoColor = new Color(0.25f, 0.35f, 0.18f);
            SetMiasmaActive(true);

            // Pulse miasma aura slightly
            if (_miasmaMaterial != null)
            {
                float pulse = 0.18f + 0.08f * Mathf.Sin(AgeSeconds * 2.5f);
                _miasmaMaterial.AlbedoColor = new Color(0.2f, 0.7f, 0.15f, pulse);
            }
        }
        else if (t < FreshDuration + BloatedDuration + MiasmaDuration + SkeletonDuration)
        {
            CurrentStage = CorpseStage.Skeleton;
            SetMiasmaActive(false);
            if (_bodyMaterial != null)
                _bodyMaterial.AlbedoColor = new Color(0.75f, 0.72f, 0.65f); // Bone color
            if (_bodyMesh != null)
                _bodyMesh.Scale = _bodyMesh.Scale.Lerp(new Vector3(0.6f, 0.6f, 0.8f), dt * 0.2f);
        }
        else
        {
            // Sunk away into earth, cleanup
            QueueFree();
        }

        // Notify global listeners the moment rot becomes hazardous
        if (CurrentStage == CorpseStage.RottingMiasma && oldStage != CorpseStage.RottingMiasma)
        {
            EventBus.Instance?.EmitCorpseRotAdvanced(GlobalPosition, MiasmaRadius);
        }
    }

    /// <summary>
    /// Re-create a saved corpse's state (save/load): how far it has rotted and
    /// whether it was already looted or buried. Miasma emission is re-derived
    /// from the stage so a restored rotting corpse still poisons the area.
    /// </summary>
    public void RestoreState(int stage, float ageSeconds, bool searched, bool buried)
    {
        CurrentStage = System.Enum.IsDefined(typeof(CorpseStage), stage)
            ? (CorpseStage)stage
            : CorpseStage.Fresh;
        AgeSeconds = Mathf.Max(0f, ageSeconds);
        IsSearched = searched;
        IsBuried = buried;
        IsBurning = false;
        _burnTimer = 0f;

        if (_bodyMesh != null)
        {
            _bodyMesh.Scale = CurrentStage switch
            {
                CorpseStage.Bloated => new Vector3(1.25f, 1.25f, 1.05f),
                CorpseStage.Skeleton => new Vector3(0.6f, 0.6f, 0.8f),
                _ => Vector3.One,
            };
        }

        SetMiasmaActive(CurrentStage == CorpseStage.RottingMiasma && !buried);
        UpdateLifecycleStage(0f);
    }

    private void SetMiasmaActive(bool active)
    {
        if (_miasmaCloudMesh != null) _miasmaCloudMesh.Visible = active;
        if (_miasmaLight != null) _miasmaLight.Visible = active;
    }

    private void ApplyMiasmaDamage(float dt)
    {
        var players = GetTree().GetNodesInGroup("player");
        foreach (var p in players)
        {
            if (p is Node3D playerNode)
            {
                float dist = GlobalPosition.DistanceTo(playerNode.GlobalPosition);
                if (dist <= MiasmaRadius)
                {
                    var health = playerNode.GetNodeOrNull<HealthComponent>("HealthComponent");
                    if (health != null && health.IsAlive)
                    {
                        health.TakeDamage(MiasmaDamagePerSecond * dt, DamageType.Toxic, this);
                    }
                }
            }
        }
    }

    public void ShowPrompt(bool visible, string customText = "")
    {
        if (_promptLabel != null)
        {
            if (!string.IsNullOrEmpty(customText))
                _promptLabel.Text = customText;
            else
                _promptLabel.Text = GetDefaultPrompt();
            _promptLabel.Visible = visible && !IsBuried && !IsBurning;
        }
    }

    public string GetDefaultPrompt()
    {
        if (IsBuried) return "Buried Grave";
        if (IsBurning) return "Incinerating...";

        return CurrentStage switch
        {
            CorpseStage.Fresh => "[E] Scavenge / Dispose Corpse",
            CorpseStage.Bloated => "[E] Dispose Bloated Corpse",
            CorpseStage.RottingMiasma => "[E] Cleanse Toxic Miasma!",
            CorpseStage.Skeleton => "[E] Clear Bone Remains",
            _ => "[E] Corpse"
        };
    }

    /// <summary>
    /// Search corpse for salvageable cloth, ammo, or scrap.
    /// </summary>
    public bool Scavenge(InventoryComponent inventory)
    {
        if (IsSearched || IsBuried || IsBurning) return false;

        IsSearched = true;
        int cloth = GD.RandRange(1, 3);
        inventory.TryAdd(ItemData.Cloth, cloth);

        // 35% chance to find 9mm ammo or scrap
        if (GD.Randf() < 0.35f)
        {
            inventory.TryAdd(ItemData.Ammo9mm, 1);
        }
        else if (GD.Randf() < 0.5f)
        {
            inventory.TryAdd(ItemData.Scrap, 2);
        }

        GD.Print($"[Corpse] Scavenged corpse for {cloth}× Cloth.");
        return true;
    }

    /// <summary>
    /// Dig and bury the corpse, eliminating miasma and leaving a mound.
    /// </summary>
    public void Bury()
    {
        if (IsBuried || IsBurning) return;

        IsBuried = true;
        SetMiasmaActive(false);

        if (_bodyMesh != null)
        {
            // Turn into dirt grave mound
            var box = new BoxMesh { Size = new Vector3(1.2f, 0.2f, 1.8f) };
            _bodyMesh.Mesh = box;
            _bodyMesh.Position = new Vector3(0, 0.1f, 0);
            _bodyMesh.RotationDegrees = Vector3.Zero;
            _bodyMesh.Scale = Vector3.One;
            if (_bodyMaterial != null)
                _bodyMaterial.AlbedoColor = new Color(0.28f, 0.22f, 0.15f); // Earth mound
        }

        if (_promptLabel != null)
            _promptLabel.Visible = false;

        EventBus.Instance?.EmitCorpseBuried(GlobalPosition);
        GD.Print("[Corpse] Corpse peacefully buried.");
    }

    /// <summary>
    /// Incinerate the corpse with fire/fuel, eradicating miasma immediately.
    /// </summary>
    public void Burn()
    {
        if (IsBuried || IsBurning) return;

        IsBurning = true;
        _burnTimer = 4.0f;
        SetMiasmaActive(false);

        // Create flame glow
        var fireLight = new OmniLight3D
        {
            LightColor = new Color(1f, 0.5f, 0.1f),
            LightEnergy = 2.5f,
            OmniRange = 5.0f
        };
        AddChild(fireLight);

        GD.Print("[Corpse] Corpse ignited and incinerating.");
    }
}
