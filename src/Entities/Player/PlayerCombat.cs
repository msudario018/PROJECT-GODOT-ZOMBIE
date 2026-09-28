using Godot;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Core.Utilities;
using ZombieApocalypse.Entities.Zombies;

namespace ZombieApocalypse.Entities.Player;

/// <summary>
/// Handles player melee combat, weapon swings, hit detection, and damage application.
/// </summary>
public partial class PlayerCombat : Node3D
{
    [ExportGroup("Melee Tuning")]
    [Export] public float AttackDamage = 35.0f;
    [Export] public DamageType DamageType = DamageType.Blunt;
    [Export] public float AttackRange = 2.4f;
    [Export] public float AttackArcDeg = 110.0f;
    [Export] public float AttackCooldown = 0.45f;
    [Export] public float KnockbackForce = 7.0f;

    private PlayerController _player = null!;
    private AudioEmitterComponent? _audioEmitter;
    private float _cooldownTimer = 0f;
    private Node3D? _swingVisual;

    public bool CanAttack => _cooldownTimer <= 0f;

    public override void _Ready()
    {
        _player = GetOwner<PlayerController>() ?? (GetParent() as PlayerController)!;
        _audioEmitter = _player.GetNodeOrNull<AudioEmitterComponent>("AudioEmitterComponent");

        CreateSwingVisual();
    }

    public override void _Process(double delta)
    {
        if (_cooldownTimer > 0f)
        {
            _cooldownTimer -= (float)delta;
        }

        if (Input.IsActionJustPressed("attack") && CanAttack)
        {
            ExecuteMeleeAttack();
        }
    }

    private void ExecuteMeleeAttack()
    {
        _cooldownTimer = AttackCooldown;

        // Emit sound for zombie sensory systems
        _audioEmitter?.EmitMeleeSwing();

        // Get aim direction toward mouse cursor
        Vector3 aimDir = _player.GetAimDirection();
        Vector3 playerPos = _player.GlobalPosition;

        // Play visual swing arc
        PlaySwingVisual(aimDir);

        // Broad-phase query through SpatialGrid
        var candidates = ZombieBase.SharedSpatialGrid.QueryRadius(playerPos, AttackRange + 0.5f);

        float halfAngleRad = Mathf.DegToRad(AttackArcDeg * 0.5f);
        float facingAngle = Mathf.Atan2(aimDir.X, aimDir.Z);

        int hitCount = 0;
        foreach (var entity in candidates)
        {
            if (entity is ZombieBase zombie && !zombie.IsDead)
            {
                Vector3 zPos = zombie.GlobalPosition;

                // Test cone
                if (MathUtils.IsInCone(playerPos, facingAngle, halfAngleRad, zPos, AttackRange))
                {
                    Vector3 knockDir = (zPos - playerPos).Normalized();
                    knockDir.Y = 0f;

                    zombie.ApplyDamage(AttackDamage, DamageType, _player, knockDir * KnockbackForce);
                    hitCount++;
                }
            }
        }

        if (hitCount > 0)
        {
            GD.Print($"[PlayerCombat] Melee attack hit {hitCount} zombie(s) for {AttackDamage} damage.");
        }
    }

    private void CreateSwingVisual()
    {
        // Simple visual indicator for swing arc
        var meshInst = new MeshInstance3D();
        var cylinder = new CylinderMesh
        {
            TopRadius = AttackRange,
            BottomRadius = AttackRange,
            Height = 0.1f
        };
        meshInst.Mesh = cylinder;

        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 1f, 0.4f, 0.35f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        meshInst.MaterialOverride = mat;
        meshInst.Visible = false;

        AddChild(meshInst);
        _swingVisual = meshInst;
    }

    private void PlaySwingVisual(Vector3 aimDir)
    {
        if (_swingVisual == null) return;

        _swingVisual.Position = aimDir * (AttackRange * 0.4f) + Vector3.Up * 0.8f;
        _swingVisual.Scale = new Vector3(0.3f, 0.05f, 0.3f);
        _swingVisual.Visible = true;

        var tween = CreateTween();
        tween.TweenProperty(_swingVisual, "scale", new Vector3(0.9f, 0.05f, 0.9f), 0.12f);
        tween.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(_swingVisual))
                _swingVisual.Visible = false;
        }));
    }
}
