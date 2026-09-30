using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Entities.NPC;

namespace ZombieApocalypse.Entities.Bandits;

/// <summary>
/// Hostile human raider. Fights survivors / the player for loot and airdrops,
/// flees at low HP (threshold depends on <see cref="Tier"/>), and announces
/// itself acoustically so zombies join the chaos.
/// </summary>
public partial class BanditBase : CharacterBody3D
{
    [ExportGroup("Identity")]
    [Export] public BanditTier Tier = BanditTier.Scavenger;
    [Export] public string BanditName = "Raider";

    [ExportGroup("Combat")]
    [Export] public float MoveSpeed = 3.4f;
    [Export] public float MeleeDamage = 12.0f;
    [Export] public float MeleeCooldown = 1.4f;
    [Export] public float MeleeRange = 1.9f;
    [Export] public float RangedDamage = 10.0f;
    [Export] public float RangedCooldown = 2.2f;
    [Export] public float RangedRange = 16.0f;
    [Export] public float DetectionRadius = 20.0f;
    [Export] public float Gravity = 30.0f;

    // ── Runtime ──────────────────────────────────────────────────────
    public bool IsDead => Health != null && !Health.IsAlive;
    public bool IsFleeing { get; private set; }
    public HealthComponent Health { get; private set; } = null!;
    public InventoryComponent? Inventory { get; private set; }
    public AudioEmitterComponent? AudioEmitter { get; private set; }
    public BanditCombatState CombatState { get; set; } = BanditCombatState.Advance;
    public Node3D? CombatTarget { get; set; }
    public Vector3 FlankPoint { get; set; }
    public bool HasFlankPoint { get; set; }
    public BanditSquad? Squad { get; set; }
    public SquadRole Role { get; set; } = SquadRole.Advance;

    private float _meleeCooldown;
    private float _rangedCooldown;
    private float _lootCooldown;
    private float _retargetTimer;
    private float _deathTimer = -1f;

    public override void _EnterTree()
    {
        // Physics layers: 1=World, 2=Player, 3=Zombies, 5=Survivors, 6=Bandits
        CollisionLayer = 32;              // layer 6
        CollisionMask = 1 | 2 | 4 | 16 | 32;

        EnsureChild<HealthComponent>("HealthComponent");
        EnsureChild<InventoryComponent>("InventoryComponent");
        EnsureChild<AudioEmitterComponent>("AudioEmitterComponent");
        EnsureChild<NavigationAgent3D>("NavigationAgent3D");
        EnsureChild<Node3D>("Mesh");

        if (GetNodeOrNull<CollisionShape3D>("CollisionShape3D") == null)
        {
            var shape = new CollisionShape3D { Name = "CollisionShape3D" };
            shape.Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f };
            shape.Position = new Vector3(0f, 0.9f, 0f);
            AddChild(shape);
        }
    }

    public override void _Ready()
    {
        ApplyTierStats();

        Health = GetNode<HealthComponent>("HealthComponent");
        Health.Died += OnDied;

        Inventory = GetNode<InventoryComponent>("InventoryComponent");
        Inventory.GrantStartingItems = false;

        AudioEmitter = GetNode<AudioEmitterComponent>("AudioEmitterComponent");
        AddToGroup("bandits");

        if (GetNodeOrNull<MeshInstance3D>("MeshVisual") == null)
        {
            var visual = new MeshInstance3D { Name = "MeshVisual" };
            visual.Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f };
            visual.Position = new Vector3(0f, 0.9f, 0f);
            visual.MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = TierColor(),
                Roughness = 0.7f
            };
            AddChild(visual);
        }
    }

    public override void _ExitTree()
    {
        if (Health != null)
            Health.Died -= OnDied;
    }

    /// <summary>Tier stat presets. Applied to exports so scenes can pre-place bandits.</summary>
    public void ApplyTierStats()
    {
        switch (Tier)
        {
            case BanditTier.Warlord:
                MoveSpeed = 3.4f; MeleeDamage = 22f; RangedDamage = 18f;
                DetectionRadius = 24f;
                break;
            case BanditTier.Militia:
                MoveSpeed = 3.4f; MeleeDamage = 15f; RangedDamage = 12f;
                DetectionRadius = 22f;
                break;
            default:
                MoveSpeed = 3.2f; MeleeDamage = 12f; RangedDamage = 9f;
                DetectionRadius = 18f;
                break;
        }
    }

    /// <summary>HP fraction below which this bandit flees instead of fighting.</summary>
    public float RetreatThreshold => Tier switch
    {
        BanditTier.Scavenger => 0.5f,
        BanditTier.Militia => 0.3f,
        _ => 0.0f,
    };

    private Color TierColor() => Tier switch
    {
        BanditTier.Warlord => new Color(0.6f, 0.1f, 0.1f),
        BanditTier.Militia => new Color(0.55f, 0.35f, 0.1f),
        _ => new Color(0.4f, 0.4f, 0.42f),
    };

    // ── Combat loop ──────────────────────────────────────────────────

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        ApplyGravity(dt);

        if (IsDead)
        {
            _deathTimer -= dt;
            if (_deathTimer <= 0f) QueueFree();
            return;
        }

        _meleeCooldown = Mathf.Max(0f, _meleeCooldown - dt);
        _rangedCooldown = Mathf.Max(0f, _rangedCooldown - dt);
        _lootCooldown = Mathf.Max(0f, _lootCooldown - dt);
        _retargetTimer -= dt;

        UpdateFleeState();
        if (IsFleeing)
        {
            TickRetreat(dt);
            MoveAndSlide();
            return;
        }

        if (_retargetTimer <= 0f)
        {
            _retargetTimer = 0.5f;
            AcquireTarget();
        }

        if (!IsValidTarget(CombatTarget))
        {
            CombatState = SquadronWantsLoot() ? BanditCombatState.Loot : BanditCombatState.Advance;
        }

        switch (CombatState)
        {
            case BanditCombatState.Advance: TickAdvance(dt); break;
            case BanditCombatState.Suppress: TickSuppress(dt); break;
            case BanditCombatState.Flank: TickFlank(dt); break;
            case BanditCombatState.Loot: TickLoot(dt); break;
            case BanditCombatState.Retreat: TickRetreat(dt); break;
        }

        MoveAndSlide();
    }

    private void UpdateFleeState()
    {
        float threshold = RetreatThreshold;
        if (threshold <= 0f) return;

        bool shouldFlee = Health.HealthPercent < threshold;
        if (shouldFlee && !IsFleeing)
        {
            IsFleeing = true;
            CombatState = BanditCombatState.Retreat;
            AudioEmitter?.EmitCustomSound(12.0f, AudioSourceType.VoiceHuman);
            GD.Print($"[Bandit] {BanditName} breaks and flees (HP {Health.HealthPercent:P0}).");
        }
        else if (!shouldFlee && IsFleeing)
        {
            IsFleeing = false;
        }
    }

    private void AcquireTarget()
    {
        if (IsValidTarget(CombatTarget)) return;
        CombatTarget = BanditSquad.FindPreyNear(GlobalPosition, DetectionRadius);
    }


    // ── Combat states ────────────────────────────────────────────────

    private void TickAdvance(float dt)
    {
        if (!IsValidTarget(CombatTarget)) return;

        float dist = DistanceTo(CombatTarget!);
        if (dist <= MeleeRange && _meleeCooldown <= 0f)
        {
            _meleeCooldown = MeleeCooldown;
            MeleeStrike(CombatTarget!);
        }
        else if (dist > MeleeRange)
        {
            float speed = MoveSpeed * (Role == SquadRole.Advance ? 1.15f : 1.0f);
            MoveToward(GetCoverPoint(CombatTarget!.GlobalPosition), speed, dt);
        }
    }

    private void TickSuppress(float dt)
    {
        if (!IsValidTarget(CombatTarget)) return;

        FaceToward(CombatTarget!.GlobalPosition, dt);

        if (_rangedCooldown <= 0f)
        {
            _rangedCooldown = RangedCooldown;
            FireRanged(CombatTarget!);
        }

        Velocity = new Vector3(0f, Velocity.Y, 0f);
    }

    private void TickFlank(float dt)
    {
        if (!IsValidTarget(CombatTarget))
        {
            HasFlankPoint = false;
            return;
        }

        if (!HasFlankPoint)
        {
            FlankPoint = BanditSquad.ComputeFlankPoint(GlobalPosition, CombatTarget!.GlobalPosition);
            HasFlankPoint = true;
        }

        MoveToward(FlankPoint, MoveSpeed * 1.2f, dt);

        if (DistanceToPoint(FlankPoint) < 1.0f)
        {
            HasFlankPoint = false;
            CombatState = BanditCombatState.Suppress;
        }
    }

    private void TickLoot(float dt)
    {
        var crate = ZombieApocalypse.Entities.Survivors.SurvivorBase.FindUnsearchedContainer(GlobalPosition, 25f);
        if (crate == null)
        {
            CombatState = BanditCombatState.Advance;
            return;
        }

        MoveToward(crate.GlobalPosition, MoveSpeed, dt);

        if (DistanceToPoint(crate.GlobalPosition) <= 1.8f && _lootCooldown <= 0f && Inventory != null)
        {
            _lootCooldown = 12.0f;
            crate.ForceSearch(Inventory);
            AudioEmitter?.EmitCustomSound(8.0f, AudioSourceType.Footstep);
            GD.Print($"[Bandit] {BanditName} looted {crate.ContainerLabel}.");
            CombatState = BanditCombatState.Advance;
        }
    }

    // ── Attacks ──────────────────────────────────────────────────────

    private void MeleeStrike(Node3D foe)
    {
        AudioEmitter?.EmitMeleeSwing();
        FaceToward(foe.GlobalPosition, 0.05f);

        if (foe is Zombies.ZombieBase zombie)
        {
            Vector3 knock = zombie.GlobalPosition - GlobalPosition;
            knock.Y = 0f;
            knock = knock.LengthSquared() > 0.001f ? knock.Normalized() * 4.0f : Vector3.Zero;
            zombie.ApplyDamage(MeleeDamage, DamageType.Blunt, this, knock);
            return;
        }

        var health = foe.GetNodeOrNull<HealthComponent>("HealthComponent");
        health?.TakeDamage(MeleeDamage, DamageType.Blunt, this);
    }

    /// <summary>Ranged shot with tracer + impact feedback.</summary>
    public void FireRanged(Node3D foe)
    {
        Vector3 target = foe.GlobalPosition + Vector3.Up * 1.0f;
        Vector3 muzzle = GlobalPosition + Vector3.Up * 1.4f;

        // Start the ray outside our own capsule so we can't shoot ourselves.
        Vector3 initial = target - muzzle;
        if (initial.LengthSquared() > 0.01f)
            muzzle += initial.Normalized() * 0.7f;

        Vector3 dir = target - muzzle;
        float dist = dir.Length();
        if (dist < 0.01f) return;
        dir /= dist;

        float maxRange = Mathf.Min(RangedRange, 30f);
        float hitDist = maxRange;
        Node3D? hitNode = null;

        var space = GetWorld3D()?.DirectSpaceState;
        if (space != null)
        {
            var query = PhysicsRayQueryParameters3D.Create(muzzle, muzzle + dir * maxRange,
                1u | 2u | 4u | 16u | 32u);
            var hit = space.IntersectRay(query);
            if (hit.Count > 0)
            {
                hitDist = muzzle.DistanceTo((Vector3)hit["position"]);
                hitNode = hit["collider"].As<Node>() as Node3D;
            }
        }

        var projectiles = ZombieApocalypse.Systems.Combat.ProjectileManager.Instance;
        if (projectiles != null)
        {
            projectiles.SpawnTracer(muzzle, muzzle + dir * hitDist);
            if (hitNode != null)
                projectiles.SpawnImpact(muzzle + dir * hitDist, -dir);
        }

        AudioEmitter?.EmitGunshot(Tier == BanditTier.Scavenger);

        if (hitNode == null || hitDist > RangedRange) return;

        float falloff = 1f - 0.5f * (hitDist / RangedRange);
        float damage = RangedDamage * falloff;

        if (hitNode is Zombies.ZombieBase zombie)
        {
            zombie.ApplyDamage(damage, DamageType.Ballistic, this, dir * 3.0f);
        }
        else
        {
            var health = hitNode.GetNodeOrNull<HealthComponent>("HealthComponent");
            health?.TakeDamage(damage, DamageType.Ballistic, this);
        }
    }

    // ── Movement & helpers ───────────────────────────────────────────

    private Vector3 GetCoverPoint(Vector3 threatPos)
    {
        Vector3 toThreat = threatPos - GlobalPosition;
        toThreat.Y = 0f;
        if (toThreat.LengthSquared() < 0.01f) return threatPos;

        Vector3 side = new Vector3(-toThreat.Z, 0f, toThreat.X).Normalized();
        float offset = Role == SquadRole.Flank ? 4.0f : 2.0f;
        return threatPos + side * offset * (HasFlankPoint ? -1f : 1f);
    }

    private void MoveToward(Vector3 goal, float speed, float dt)
    {
        Vector3 to = goal - GlobalPosition;
        to.Y = 0f;
        if (to.Length() < 0.25f)
        {
            Velocity = new Vector3(0f, Velocity.Y, 0f);
            return;
        }

        Vector3 moveDir = to.Normalized();
        var nav = GetNodeOrNull<NavigationAgent3D>("NavigationAgent3D");
        if (nav != null)
        {
            nav.TargetPosition = goal;
            if (!nav.IsNavigationFinished())
            {
                Vector3 next = nav.GetNextPathPosition() - GlobalPosition;
                next.Y = 0f;
                if (next.LengthSquared() > 0.04f) moveDir = next.Normalized();
            }
        }

        var v = Velocity;
        v.X = moveDir.X * speed;
        v.Z = moveDir.Z * speed;
        Velocity = v;
        FaceToward(goal, dt);
    }

    private void FaceToward(Vector3 point, float dt)
    {
        Vector3 d = point - GlobalPosition;
        d.Y = 0f;
        if (d.LengthSquared() < 0.001f) return;

        float target = Mathf.Atan2(d.X, d.Z);
        var mesh = GetNodeOrNull<Node3D>("Mesh");
        if (mesh != null)
            mesh.Rotation = new Vector3(0f, Mathf.LerpAngle(mesh.Rotation.Y, target, 8.0f * Mathf.Max(dt, 0.016f)), 0f);
    }

    private float DistanceTo(Node3D other)
    {
        Vector3 d = other.GlobalPosition - GlobalPosition;
        d.Y = 0f;
        return d.Length();
    }

    private float DistanceToPoint(Vector3 point)
    {
        Vector3 d = point - GlobalPosition;
        d.Y = 0f;
        return d.Length();
    }

    private Vector3 RetreatAnchor()
    {
        if (Squad != null && GodotObject.IsInstanceValid(Squad))
            return Squad.SpawnOrigin;
        return CampAnchorFallback();
    }

    private Vector3 CampAnchorFallback()
    {
        var player = GetTree().GetFirstNodeInGroup("player") as Node3D;
        if (player == null) return GlobalPosition;
        Vector3 away = GlobalPosition - player.GlobalPosition;
        away.Y = 0f;
        return player.GlobalPosition + away.Normalized() * 20f;
    }

    private void ApplyGravity(float dt)
    {
        var v = Velocity;
        v.Y = IsOnFloor() ? -0.1f : v.Y - Gravity * dt;
        Velocity = v;
    }

    private static bool IsValidTarget(Node3D? node)
    {
        if (node == null || !GodotObject.IsInstanceValid(node)) return false;
        if (node is Zombies.ZombieBase zb) return !zb.IsDead;
        if (node is Survivors.SurvivorBase sv) return sv.IsAlive;
        var health = node.GetNodeOrNull<HealthComponent>("HealthComponent");
        return health == null || health.IsAlive;
    }

    private void EnsureChild<T>(string childName) where T : Node, new()
    {
        if (GetNodeOrNull<T>(childName) != null) return;
        var child = new T { Name = childName };
        if (child is HealthComponent hc)
        {
            hc.MaxHealth = 100f;
        }
        if (child is InventoryComponent inv)
        {
            inv.GrantStartingItems = false;
            inv.SlotCount = 8;
        }
        AddChild(child);
    }

    private void OnDied()
    {
        CombatState = BanditCombatState.Dead;
        Velocity = Vector3.Zero;

        DropLoot();
        Squad?.NotifyMemberDied(this);
        AudioEmitter?.EmitCustomSound(12.0f, AudioSourceType.VoiceHuman);
        GD.Print($"[Bandit] {BanditName} ({Tier}) killed at {GlobalPosition}.");

        _deathTimer = 3.0f;
    }

    /// <summary>
    /// Spawn a searchable cache holding everything the raider carried, so the
    /// player can actually loot the body (previously the drop was log-only).
    /// </summary>
    private void DropLoot()
    {
        if (Inventory == null) return;

        var entries = new List<Systems.Loot.LootEntry>();
        var carried = new List<(ItemData item, int qty)>();
        foreach (var stack in Inventory.Slots)
        {
            if (stack.Item == null || stack.Quantity <= 0) continue;
            carried.Add((stack.Item, stack.Quantity));
        }
        if (carried.Count == 0) return;

        foreach (var (item, qty) in carried)
        {
            int removed = Inventory.TryRemove(item.ItemId, qty);
            if (removed > 0) entries.Add(new Systems.Loot.LootEntry(item, removed, removed, 1f));
        }
        if (entries.Count == 0) return;

        var cache = new Systems.Loot.LootContainer
        {
            Name = $"{Name}_Drop",
            ContainerLabel = "Raider Cache",
            ContainerType = Systems.Loot.LootContainer.ContainerArchetype.GeneralJunk,
            Position = GlobalPosition,
        };

        var parent = GetTree()?.CurrentScene ?? GetParent();
        parent?.AddChild(cache);
        cache.SetFixedManifest(entries, restrictedForNpcs: true);
        GD.Print($"[Bandit] {BanditName} dropped {entries.Count} stack(s) as a raider cache.");
    }

    private void TickRetreat(float dt)
    {
        Vector3 away = GlobalPosition - RetreatAnchor();
        away.Y = 0f;
        if (away.LengthSquared() < 0.01f)
            away = -GlobalTransform.Basis.Z;
        away = away.Normalized();

        MoveToward(GlobalPosition + away * 10f, MoveSpeed * 1.3f, dt);
    }

    private bool SquadronWantsLoot()
    {
        if (Squad != null) return Squad.AllowLooting && _lootCooldown <= 0f;

        return _lootCooldown <= 0f
            && ZombieApocalypse.Entities.Survivors.SurvivorBase.FindUnsearchedContainer(GlobalPosition, 20f) != null;
    }
}
