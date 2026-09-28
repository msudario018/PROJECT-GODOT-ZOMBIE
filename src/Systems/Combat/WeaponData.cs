using Godot;
using ZombieApocalypse.Core.Data;

namespace ZombieApocalypse.Systems.Combat;

/// <summary>
/// Data definition for weapons (melee and ranged).
/// Can be loaded as Resources or instantiated programmatically.
/// </summary>
[GlobalClass]
public partial class WeaponData : Resource
{
    [Export] public string WeaponId = "crowbar";
    [Export] public string WeaponName = "Rusty Crowbar";
    [Export] public bool IsRanged = false;
    [Export] public DamageType DamageType = DamageType.Blunt;
    [Export] public float Damage = 35.0f;
    [Export] public float Range = 2.4f;
    [Export] public float FireRate = 2.2f; // Attacks/shots per second
    [Export] public int MagazineCapacity = 0; // 0 for melee
    [Export] public float ReloadTime = 1.8f;
    [Export] public float NoiseRadius = 6.0f; // Acoustic propagation radius
    [Export] public bool IsSuppressed = false;
    [Export] public int PelletCount = 1; // 1 for single bullet, >1 for shotgun
    [Export] public float SpreadAngleDeg = 0.0f;
    [Export] public float KnockbackForce = 6.0f;

    public float Cooldown => FireRate > 0f ? 1.0f / FireRate : 0.5f;
}
