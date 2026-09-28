using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Core.Utilities;
using ZombieApocalypse.Entities.Zombies;
using ZombieApocalypse.Systems.Combat;

namespace ZombieApocalypse.Entities.Player;

/// <summary>
/// Advanced player combat system managing melee and ranged weapons,
/// ballistics raycasting, multi-pellet shotgun spread, reloading, and acoustic noise generation.
/// </summary>
public partial class PlayerCombat : Node3D
{
    [Signal] public delegate void WeaponChangedEventHandler(string weaponName);
    [Signal] public delegate void AmmoChangedEventHandler(int currentMag, int reserve);

    [Export(PropertyHint.Layers3DPhysics)] public uint CombatHitMask = 1 | 4 | 8; // Layer 1 (World), Layer 3 (Zombies), Layer 4 (Obstacles)

    // Equipped weapons
    private readonly List<WeaponData> _weapons = new();
    private readonly List<int> _currentMags = new();
    private readonly List<int> _reserveAmmos = new();
    private int _currentWeaponIndex = 0;

    // Runtime state
    private float _attackCooldownTimer = 0f;
    private float _reloadTimer = 0f;
    private bool _isReloading = false;
    private PlayerController _player = null!;
    private AudioEmitterComponent? _audioEmitter;
    private Node3D? _swingVisual;

    public WeaponData CurrentWeapon => _weapons[_currentWeaponIndex];
    public int CurrentMag => _currentMags[_currentWeaponIndex];
    public int CurrentReserve => _reserveAmmos[_currentWeaponIndex];
    public bool IsReloading => _isReloading;
    public float ReloadProgress => _isReloading && CurrentWeapon.ReloadTime > 0 ? 1f - (_reloadTimer / CurrentWeapon.ReloadTime) : 0f;
    public bool CanAttack => _attackCooldownTimer <= 0f && !_isReloading;

    public override void _Ready()
    {
        _player = GetOwner<PlayerController>() ?? (GetParent() as PlayerController)!;
        _audioEmitter = _player.GetNodeOrNull<AudioEmitterComponent>("AudioEmitterComponent");

        InitializeWeapons();
        CreateSwingVisual();

        EmitSignal(SignalName.WeaponChanged, CurrentWeapon.WeaponName);
        EmitSignal(SignalName.AmmoChanged, CurrentMag, CurrentReserve);
    }

    private void InitializeWeapons()
    {
        // 1. Melee: Crowbar
        _weapons.Add(new WeaponData
        {
            WeaponId = "crowbar",
            WeaponName = "Rusty Crowbar",
            IsRanged = false,
            DamageType = DamageType.Blunt,
            Damage = 35.0f,
            Range = 2.4f,
            FireRate = 2.2f,
            MagazineCapacity = 0,
            NoiseRadius = 6.0f,
            KnockbackForce = 7.0f
        });
        _currentMags.Add(0);
        _reserveAmmos.Add(0);

        // 2. Ranged: M9 9mm Service Pistol
        _weapons.Add(new WeaponData
        {
            WeaponId = "pistol_9mm",
            WeaponName = "M9 Pistol (9mm)",
            IsRanged = true,
            DamageType = DamageType.Ballistic,
            Damage = 42.0f,
            Range = 30.0f,
            FireRate = 3.5f,
            MagazineCapacity = 15,
            ReloadTime = 1.6f,
            NoiseRadius = 45.0f,
            PelletCount = 1,
            SpreadAngleDeg = 1.5f,
            KnockbackForce = 4.5f
        });
        _currentMags.Add(15);
        _reserveAmmos.Add(45);

        // 3. Ranged: Remington 870 Shotgun
        _weapons.Add(new WeaponData
        {
            WeaponId = "shotgun_12g",
            WeaponName = "Remington 870 (12G)",
            IsRanged = true,
            DamageType = DamageType.Ballistic,
            Damage = 13.0f, // 8 pellets * 13 = 104 damage max
            Range = 16.0f,
            FireRate = 1.1f,
            MagazineCapacity = 6,
            ReloadTime = 2.4f,
            NoiseRadius = 65.0f,
            PelletCount = 8,
            SpreadAngleDeg = 12.0f,
            KnockbackForce = 9.0f
        });
        _currentMags.Add(6);
        _reserveAmmos.Add(24);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            // Weapon selection keys
            if (key.Keycode == Key.Key4) SelectWeapon(0);
            else if (key.Keycode == Key.Key5) SelectWeapon(1);
            else if (key.Keycode == Key.Key6) SelectWeapon(2);
            else if (key.Keycode == Key.Q) CycleWeapon();
            else if (key.Keycode == Key.R && !_isReloading && CurrentWeapon.IsRanged)
            {
                // Only reload if not currently building
                var building = GetTree().CurrentScene?.GetNodeOrNull<Systems.Building.BuildingSystem>("BuildingSystem");
                if (building == null || building.ActiveBuildType == 0)
                {
                    StartReload();
                }
            }
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        if (_attackCooldownTimer > 0f)
            _attackCooldownTimer -= dt;

        if (_isReloading)
        {
            _reloadTimer -= dt;
            if (_reloadTimer <= 0f)
            {
                FinishReload();
            }
        }

        // Left Click / Attack execution
        if (Input.IsActionJustPressed("attack") && CanAttack)
        {
            // Verify building system is not currently placing a structure
            var building = GetTree().CurrentScene?.GetNodeOrNull<Systems.Building.BuildingSystem>("BuildingSystem");
            if (building == null || building.ActiveBuildType == 0)
            {
                if (CurrentWeapon.IsRanged)
                    ExecuteRangedAttack();
                else
                    ExecuteMeleeAttack();
            }
        }
    }

    public void SelectWeapon(int index)
    {
        if (index < 0 || index >= _weapons.Count || index == _currentWeaponIndex) return;

        if (_isReloading)
            _isReloading = false;

        _currentWeaponIndex = index;
        _attackCooldownTimer = 0.2f;

        EmitSignal(SignalName.WeaponChanged, CurrentWeapon.WeaponName);
        EmitSignal(SignalName.AmmoChanged, CurrentMag, CurrentReserve);
        GD.Print($"[PlayerCombat] Switched to {CurrentWeapon.WeaponName}.");
    }

    public void CycleWeapon()
    {
        int next = (_currentWeaponIndex + 1) % _weapons.Count;
        SelectWeapon(next);
    }

    private void StartReload()
    {
        int needed = CurrentWeapon.MagazineCapacity - CurrentMag;
        if (needed <= 0 || CurrentReserve <= 0) return;

        _isReloading = true;
        _reloadTimer = CurrentWeapon.ReloadTime;
        GD.Print($"[PlayerCombat] Reloading {CurrentWeapon.WeaponName}...");
    }

    private void FinishReload()
    {
        _isReloading = false;
        int needed = CurrentWeapon.MagazineCapacity - CurrentMag;
        int loaded = Mathf.Min(needed, CurrentReserve);

        _currentMags[_currentWeaponIndex] += loaded;
        _reserveAmmos[_currentWeaponIndex] -= loaded;

        EmitSignal(SignalName.AmmoChanged, CurrentMag, CurrentReserve);
        GD.Print($"[PlayerCombat] Reload complete. {CurrentMag}/{CurrentReserve}");
    }

    private void ExecuteRangedAttack()
    {
        if (CurrentMag <= 0)
        {
            StartReload();
            return;
        }

        // Consume round
        _currentMags[_currentWeaponIndex]--;
        _attackCooldownTimer = CurrentWeapon.Cooldown;
        EmitSignal(SignalName.AmmoChanged, CurrentMag, CurrentReserve);

        // Acoustic signature (gunshots attract zombies across large areas!)
        _audioEmitter?.EmitGunshot(CurrentWeapon.IsSuppressed);

        Vector3 aimDir = _player.GetAimDirection();
        Vector3 muzzlePos = _player.GlobalPosition + Vector3.Up * 1.2f + aimDir * 0.6f;
        var space = GetWorld3D()?.DirectSpaceState;
        if (space == null) return;

        int pellets = CurrentWeapon.PelletCount;
        float spreadRad = Mathf.DegToRad(CurrentWeapon.SpreadAngleDeg * 0.5f);

        for (int p = 0; p < pellets; p++)
        {
            Vector3 shootDir = aimDir;
            if (spreadRad > 0.001f)
            {
                float angleOffset = (float)GD.RandRange(-spreadRad, spreadRad);
                shootDir = shootDir.Rotated(Vector3.Up, angleOffset);
            }

            Vector3 rayEnd = muzzlePos + shootDir * CurrentWeapon.Range;
            var query = PhysicsRayQueryParameters3D.Create(muzzlePos, rayEnd, CombatHitMask);
            var hit = space.IntersectRay(query);

            Vector3 hitPos = rayEnd;
            if (hit.Count > 0)
            {
                hitPos = (Vector3)hit["position"];
                Vector3 normal = (Vector3)hit["normal"];

                // Visual impact
                ProjectileManager.Instance?.SpawnImpact(hitPos, normal);

                // Apply damage
                var collider = hit["collider"].As<GodotObject>();
                if (collider is ZombieBase zombie && !zombie.IsDead)
                {
                    zombie.ApplyDamage(CurrentWeapon.Damage, CurrentWeapon.DamageType, _player, shootDir * CurrentWeapon.KnockbackForce);
                }
                else if (collider is Node hitNode)
                {
                    var health = hitNode.GetNodeOrNull<HealthComponent>("HealthComponent");
                    health?.TakeDamage(CurrentWeapon.Damage, CurrentWeapon.DamageType, _player);
                }
            }

            // Spawn visual tracer
            ProjectileManager.Instance?.SpawnTracer(muzzlePos, hitPos);
        }

        // Auto reload when magazine empty
        if (CurrentMag <= 0 && CurrentReserve > 0)
        {
            StartReload();
        }
    }

    private void ExecuteMeleeAttack()
    {
        _attackCooldownTimer = CurrentWeapon.Cooldown;
        _audioEmitter?.EmitMeleeSwing();

        Vector3 aimDir = _player.GetAimDirection();
        Vector3 playerPos = _player.GlobalPosition;

        PlaySwingVisual(aimDir);

        var candidates = ZombieBase.SharedSpatialGrid.QueryRadius(playerPos, CurrentWeapon.Range + 0.5f);
        float halfAngleRad = Mathf.DegToRad(55.0f);
        float facingAngle = Mathf.Atan2(aimDir.X, aimDir.Z);

        foreach (var entity in candidates)
        {
            if (entity is ZombieBase zombie && !zombie.IsDead)
            {
                Vector3 zPos = zombie.GlobalPosition;
                if (MathUtils.IsInCone(playerPos, facingAngle, halfAngleRad, zPos, CurrentWeapon.Range))
                {
                    Vector3 knockDir = (zPos - playerPos).Normalized();
                    knockDir.Y = 0f;
                    zombie.ApplyDamage(CurrentWeapon.Damage, CurrentWeapon.DamageType, _player, knockDir * CurrentWeapon.KnockbackForce);
                }
            }
        }
    }

    private void CreateSwingVisual()
    {
        var meshInst = new MeshInstance3D();
        var cylinder = new CylinderMesh
        {
            TopRadius = 2.4f,
            BottomRadius = 2.4f,
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
        _swingVisual.Position = aimDir * 1.0f + Vector3.Up * 0.8f;
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
