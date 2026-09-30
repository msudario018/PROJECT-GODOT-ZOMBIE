using Godot;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Entities.NPC;
using ZombieApocalypse.Entities.Zombies;
using ZombieApocalypse.Systems.Loot;
using ZombieApocalypse.World.Defenses;
using ZombieApocalypse.World.Power.Generation;

namespace ZombieApocalypse.Entities.Survivors;

/// <summary>
/// Base NPC survivor with needs, task assignment and light combat.
/// Composed of Health, Inventory, SensorySystem (shared zombie senses),
/// AudioEmitter, NavigationAgent3D and SurvivorAI.
/// </summary>
public partial class SurvivorBase : CharacterBody3D
{
    public const float IdleWanderRadius = 4.0f;
    public const float CampDefenseRadius = 18.0f;
    public const float HealSearchRadius = 12.0f;
    public const float LootSearchRadius = 25.0f;
    public const float RepathInterval = 1.0f;
    public const float TaskReachDistance = 1.6f;
    public const float MeleeRange = 1.8f;
    public const float HealAmount = 30.0f;
    public const float WallRepairAmount = 40.0f;

    [ExportGroup("Identity")]
    [Export] public SurvivorArchetype Archetype = SurvivorArchetype.CombatVeteran;
    [Export] public string SurvivorName = "Survivor";

    [ExportGroup("Stats")]
    [Export] public float MoveSpeed = 3.6f;
    [Export] public float MaxHealth = 100f;
    [Export] public float ArmorRating = 0f;
    [Export] public float MeleeDamage = 14.0f;
    [Export] public float MeleeCooldown = 1.6f;
    [Export] public float AttackRange = 2.0f;
    [Export] public float Gravity = 30.0f;

    [ExportGroup("Camp")]
    [Export] public Vector3 CampAnchor = Vector3.Zero;
    [Export] public bool FollowPlayer = false;

    // ── Runtime ──────────────────────────────────────────────────────
    public SurvivorTask CurrentTask { get; private set; } = SurvivorTask.Idle;
    public SurvivorTask TaskMode { get; set; } = SurvivorTask.Scavenge;
    public Node3D? TaskTarget { get; private set; }
    public Vector3 TaskDestination { get; private set; }
    public bool IsAlive => Health != null && Health.IsAlive;
    public HealthComponent Health { get; private set; } = null!;
    public InventoryComponent? Inventory { get; private set; }
    public NavigationAgent3D? NavAgent { get; private set; }
    public ZombieApocalypse.Entities.Zombies.ZombieAI.SensorySystem? Sensory { get; private set; }
    public AudioEmitterComponent? AudioEmitter { get; private set; }
    public Node3D? Mesh { get; private set; }
    public float TaskSpeedMultiplier => MoraleSystem.Instance != null
        ? MoraleSystem.Instance.GetSurvivorSpeedMultiplier()
        : 1.0f;

    private float _attackCooldown;
    private float _healCooldown;
    private float _repairCooldown;
    private float _scavengeCooldown;

    public override void _EnterTree()
    {
        // Physics layers: 1=World, 2=Player, 3=Zombies, 5=Survivors, 6=Bandits
        CollisionLayer = 16;              // layer 5
        CollisionMask = 1 | 2 | 4 | 16 | 32;

        EnsureChild<HealthComponent>("HealthComponent");
        EnsureChild<InventoryComponent>("InventoryComponent");
        EnsureChild<ZombieApocalypse.Entities.Zombies.ZombieAI.SensorySystem>("SensorySystem");
        EnsureChild<AudioEmitterComponent>("AudioEmitterComponent");
        EnsureChild<NavigationAgent3D>("NavigationAgent3D");
        EnsureChild<Node3D>("Mesh");
        EnsureChild<SurvivorAI>("SurvivorAI");

        if (GetNodeOrNull<CollisionShape3D>("CollisionShape3D") == null)
        {
            var shape = new CollisionShape3D { Name = "CollisionShape3D" };
            shape.Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f };
            shape.Position = new Vector3(0f, 0.9f, 0f);
            AddChild(shape);
        }

        if (GetNodeOrNull<MeshInstance3D>("MeshVisual") == null)
        {
            var visual = new MeshInstance3D { Name = "MeshVisual" };
            visual.Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f };
            visual.Position = new Vector3(0f, 0.9f, 0f);
            visual.MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.25f, 0.55f, 0.85f),
                Roughness = 0.7f
            };
            GetNode<Node3D>("Mesh").AddChild(visual);
        }
    }

    public override void _Ready()
    {
        Health = GetNode<HealthComponent>("HealthComponent");
        Health.MaxHealth = MaxHealth;
        Health.ArmorRating = ArmorRating;
        Health.Died += OnDied;

        Inventory = GetNode<InventoryComponent>("InventoryComponent");
        Inventory.GrantStartingItems = false;
        Sensory = GetNode<ZombieApocalypse.Entities.Zombies.ZombieAI.SensorySystem>("SensorySystem");
        AudioEmitter = GetNode<AudioEmitterComponent>("AudioEmitterComponent");
        NavAgent = GetNode<NavigationAgent3D>("NavigationAgent3D");
        Mesh = GetNode<Node3D>("Mesh");
        AddToGroup("survivors");
    }

    public override void _ExitTree()
    {
        if (Health != null)
            Health.Died -= OnDied;
    }

    // ── Task driver ──────────────────────────────────────────────────

    public override void _PhysicsProcess(double delta)
    {
        if (!IsAlive) return;
        float dt = (float)delta;

        ApplyGravity(dt);

        _attackCooldown = Mathf.Max(0f, _attackCooldown - dt);
        _healCooldown = Mathf.Max(0f, _healCooldown - dt);
        _repairCooldown = Mathf.Max(0f, _repairCooldown - dt);
        _scavengeCooldown = Mathf.Max(0f, _scavengeCooldown - dt);

        switch (CurrentTask)
        {
            case SurvivorTask.Defend: TickDefend(dt); break;
            case SurvivorTask.Scavenge: TickScavenge(dt); break;
            case SurvivorTask.Repair: TickRepair(dt); break;
            case SurvivorTask.RefuelPower: TickRefuel(dt); break;
            case SurvivorTask.Heal: TickHeal(dt); break;
            case SurvivorTask.Deliver: TickDeliver(dt); break;
            default: TickIdle(dt); break;
        }

        MoveAndSlide();
    }

    private void TickIdle(float dt)
    {
        Vector3 goal = TaskDestination;
        if (FollowPlayer)
        {
            var player = GetTree().GetFirstNodeInGroup("player") as Node3D;
            if (player != null) goal = player.GlobalPosition;
        }
        else if (goal.LengthSquared() < 0.01f)
        {
            goal = CampAnchor;
        }

        MoveToward(goal, MoveSpeed * 0.5f * TaskSpeedMultiplier, dt);
    }

    private void TickDefend(float dt)
    {
        if (!IsValidTarget(TaskTarget))
        {
            SetTask(SurvivorTask.Idle, null, CampAnchor);
            return;
        }

        var foe = TaskTarget!;
        MoveToward(foe.GlobalPosition, MoveSpeed * TaskSpeedMultiplier, dt);

        if (DistanceTo(foe) <= MeleeRange && _attackCooldown <= 0f)
        {
            _attackCooldown = MeleeCooldown;
            AudioEmitter?.EmitMeleeSwing();
            DealMeleeDamage(foe, MeleeDamage);
        }
    }

    private void TickScavenge(float dt)
    {
        if (TaskTarget is not LootContainer crate || !GodotObject.IsInstanceValid(crate))
        {
            SetTask(SurvivorTask.Idle, null, CampAnchor);
            return;
        }

        MoveToward(crate.GlobalPosition, MoveSpeed * TaskSpeedMultiplier, dt);

        if (DistanceTo(crate) <= TaskReachDistance && _scavengeCooldown <= 0f)
        {
            _scavengeCooldown = crate.SearchTime + 0.5f;
            if (Inventory != null)
            {
                crate.TrySearch(Inventory);
                AudioEmitter?.EmitCustomSound(6.0f, ZombieApocalypse.Core.Data.AudioSourceType.Footstep);
            }
        }
    }

    private void TickRepair(float dt)
    {
        if (TaskTarget is not WallBase wall || !GodotObject.IsInstanceValid(wall))
        {
            SetTask(SurvivorTask.Idle, null, CampAnchor);
            return;
        }

        MoveToward(wall.GlobalPosition, MoveSpeed * TaskSpeedMultiplier, dt);

        if (DistanceTo(wall) <= AttackRange && _repairCooldown <= 0f)
        {
            _repairCooldown = 2.0f;
            wall.Health.Heal(WallRepairAmount);
            AudioEmitter?.EmitCustomSound(6.0f, ZombieApocalypse.Core.Data.AudioSourceType.Construction);
            GD.Print($"[Survivor] {SurvivorName} repaired {wall.WallName} (+{WallRepairAmount} HP).");
        }
    }

    private void TickRefuel(float dt)
    {
        if (TaskTarget is not CombustionGenerator generator
            || !GodotObject.IsInstanceValid(generator)
            || Inventory == null
            || !Inventory.Has("fuel_can"))
        {
            SetTask(SurvivorTask.Idle, null, CampAnchor);
            return;
        }

        MoveToward(generator.GlobalPosition, MoveSpeed * TaskSpeedMultiplier, dt);

        if (DistanceTo(generator) <= TaskReachDistance)
        {
            if (generator.RefuelFromInventory(Inventory))
                GD.Print($"[Survivor] {SurvivorName} refuelled the generator.");
            SetTask(SurvivorTask.Idle, null, CampAnchor);
        }
    }

    private void TickHeal(float dt)
    {
        if (!IsValidTarget(TaskTarget))
        {
            SetTask(SurvivorTask.Idle, null, CampAnchor);
            return;
        }

        var ally = TaskTarget!;
        MoveToward(ally.GlobalPosition, MoveSpeed * TaskSpeedMultiplier, dt);

        if (DistanceTo(ally) <= AttackRange && _healCooldown <= 0f)
        {
            _healCooldown = 4.0f;
            HealAlly(ally);
        }
    }

    /// <summary>Carry scavenged goods to the camp stockpile and deposit them.</summary>
    private void TickDeliver(float dt)
    {
        if (TaskTarget is not LootContainer stockpile || !GodotObject.IsInstanceValid(stockpile))
        {
            SetTask(SurvivorTask.Idle, null, CampAnchor);
            return;
        }

        int carried = Inventory?.TotalItemCount() ?? 0;
        if (carried == 0)
        {
            SetTask(SurvivorTask.Idle, null, CampAnchor);
            return;
        }

        MoveToward(stockpile.GlobalPosition, MoveSpeed * TaskSpeedMultiplier, dt);

        if (DistanceTo(stockpile) <= TaskReachDistance && Inventory != null)
        {
            if (stockpile.TryDeposit(Inventory))
                GD.Print($"[Survivor] {SurvivorName} stocked the camp supplies.");
            SetTask(SurvivorTask.Idle, null, CampAnchor);
        }
    }

    // ── Actions & movement ───────────────────────────────────────────

    private void HealAlly(Node3D ally)
    {
        HealthComponent? health = ally is SurvivorBase s
            ? s.Health
            : ally.GetNodeOrNull<HealthComponent>("HealthComponent");

        if (health == null || !health.IsAlive) return;

        float healed = Archetype == SurvivorArchetype.CombatMedic ? HealAmount * 1.5f : HealAmount;
        float actual = health.Heal(healed);
        AudioEmitter?.EmitCustomSound(6.0f, ZombieApocalypse.Core.Data.AudioSourceType.VoiceHuman);
        GD.Print($"[Survivor] {SurvivorName} healed {ally.Name} (+{actual:F0} HP).");
    }

    private void DealMeleeDamage(Node3D foe, float damage)
    {
        if (foe is ZombieBase zombie)
        {
            Vector3 knock = zombie.GlobalPosition - GlobalPosition;
            knock.Y = 0f;
            knock = knock.LengthSquared() > 0.001f ? knock.Normalized() * 4.0f : Vector3.Zero;
            zombie.ApplyDamage(damage, ZombieApocalypse.Core.Data.DamageType.Blunt, this, knock);
            return;
        }

        var health = foe.GetNodeOrNull<HealthComponent>("HealthComponent");
        health?.TakeDamage(damage, ZombieApocalypse.Core.Data.DamageType.Blunt, this);
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
        if (NavAgent != null)
        {
            NavAgent.TargetPosition = goal;
            if (!NavAgent.IsNavigationFinished())
            {
                Vector3 next = NavAgent.GetNextPathPosition() - GlobalPosition;
                next.Y = 0f;
                if (next.LengthSquared() > 0.04f) moveDir = next.Normalized();
            }
        }

        var v = Velocity;
        v.X = moveDir.X * speed;
        v.Z = moveDir.Z * speed;
        Velocity = v;

        if (Mesh != null)
        {
            float target = Mathf.Atan2(moveDir.X, moveDir.Z);
            Mesh.Rotation = new Vector3(0f, Mathf.LerpAngle(Mesh.Rotation.Y, target, 8.0f * dt), 0f);
        }
    }

    private float DistanceTo(Node3D other)
    {
        Vector3 d = other.GlobalPosition - GlobalPosition;
        d.Y = 0f;
        return d.Length();
    }

    private void ApplyGravity(float dt)
    {
        var v = Velocity;
        v.Y = IsOnFloor() ? -0.1f : v.Y - Gravity * dt;
        Velocity = v;
    }

    private static bool IsValidTarget(Node3D? node)
        => node != null && GodotObject.IsInstanceValid(node);

    private void EnsureChild<T>(string childName) where T : Node, new()
    {
        if (GetNodeOrNull<T>(childName) != null) return;
        var child = new T { Name = childName };
        if (child is HealthComponent hc)
        {
            hc.MaxHealth = MaxHealth;
            hc.ArmorRating = ArmorRating;
        }
        if (child is InventoryComponent inv)
        {
            inv.GrantStartingItems = false;
            inv.SlotCount = 12;
        }
        if (child is NavigationAgent3D nav)
        {
            nav.TargetDesiredDistance = 1.0f;
            nav.PathMaxDistance = 3.0f;
        }
        AddChild(child);
    }


    // ── Group queries (shared by SurvivorAI + tests) ─────────────────

    public static Node3D? FindThreatNear(Vector3 anchor, float radius)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return null;

        Node3D? best = null;
        float bestSq = radius * radius;

        foreach (var group in new[] { "zombies", "bandits" })
        {
            foreach (var node in tree.GetNodesInGroup(group))
            {
                if (node is not Node3D target || !GodotObject.IsInstanceValid(target)) continue;
                if (target is ZombieBase zb && zb.IsDead) continue;
                if (target is Bandits.BanditBase bb && bb.IsDead) continue;

                float distSq = ZombieApocalypse.Core.Utilities.MathUtils.DistanceSquaredXZ(anchor, target.GlobalPosition);
                if (distSq < bestSq)
                {
                    bestSq = distSq;
                    best = target;
                }
            }
        }

        return best;
    }

    public static Node3D? FindInjuredAlly(Vector3 from, float radius)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return null;

        Node3D? best = null;
        float bestFraction = 0.75f;
        float radiusSq = radius * radius;

        foreach (var group in new[] { "survivors", "player" })
        {
            foreach (var node in tree.GetNodesInGroup(group))
            {
                if (node is not Node3D target || !GodotObject.IsInstanceValid(target)) continue;

                HealthComponent? health = target is SurvivorBase s
                    ? s.Health
                    : target.GetNodeOrNull<HealthComponent>("HealthComponent");
                if (health == null || !health.IsAlive || health.IsFullHealth) continue;
                if (ZombieApocalypse.Core.Utilities.MathUtils.DistanceSquaredXZ(from, target.GlobalPosition) > radiusSq)
                    continue;

                if (health.HealthPercent < bestFraction)
                {
                    bestFraction = health.HealthPercent;
                    best = target;
                }
            }
        }

        return best;
    }

    public static LootContainer? FindUnsearchedContainer(Vector3 from, float radius)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return null;

        LootContainer? best = null;
        float bestSq = radius * radius;

        foreach (var node in tree.GetNodesInGroup("loot_containers"))
        {
            if (node is not LootContainer crate || !GodotObject.IsInstanceValid(crate)) continue;
            if (crate.IsSearched || crate.IsSearching) continue;
            if (crate.IsAirdropCache) continue;
            // The camp stockpile is our own store — never scavenge it back.
            if (crate.IsInGroup("camp_stockpile")) continue;

            float distSq = ZombieApocalypse.Core.Utilities.MathUtils.DistanceSquaredXZ(from, crate.GlobalPosition);
            if (distSq < bestSq)
            {
                bestSq = distSq;
                best = crate;
            }
        }

        return best;
    }

    /// <summary>Locate the camp stockpile containers (group "camp_stockpile").</summary>
    public static LootContainer? FindStockpile(Vector3 from, float radius)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return null;

        LootContainer? best = null;
        float bestSq = radius * radius;

        foreach (var node in tree.GetNodesInGroup("camp_stockpile"))
        {
            if (node is not LootContainer pile || !GodotObject.IsInstanceValid(pile)) continue;

            float distSq = ZombieApocalypse.Core.Utilities.MathUtils.DistanceSquaredXZ(from, pile.GlobalPosition);
            if (distSq < bestSq)
            {
                bestSq = distSq;
                best = pile;
            }
        }

        return best;
    }

    public static WallBase? FindDamagedWall(Vector3 from)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return null;

        WallBase? best = null;
        float bestSq = float.MaxValue;

        foreach (var node in tree.GetNodesInGroup("walls"))
        {
            if (node is not WallBase wall || !GodotObject.IsInstanceValid(wall)) continue;
            if (wall.Health == null || wall.Health.IsFullHealth) continue;

            float distSq = ZombieApocalypse.Core.Utilities.MathUtils.DistanceSquaredXZ(from, wall.GlobalPosition);
            if (distSq < bestSq)
            {
                bestSq = distSq;
                best = wall;
            }
        }

        return best;
    }

    public static CombustionGenerator? FindGenerator(Vector3 from, float radius)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return null;

        CombustionGenerator? best = null;
        float bestSq = radius * radius;

        foreach (var node in tree.GetNodesInGroup("power_nodes"))
        {
            if (node is not CombustionGenerator generator || !GodotObject.IsInstanceValid(generator))
                continue;

            float distSq = ZombieApocalypse.Core.Utilities.MathUtils.DistanceSquaredXZ(from, generator.GlobalPosition);
            if (distSq < bestSq)
            {
                bestSq = distSq;
                best = generator;
            }
        }

        return best;
    }

    private void OnDied()
    {
        AudioEmitter?.EmitCustomSound(10.0f, ZombieApocalypse.Core.Data.AudioSourceType.VoiceHuman);
        GD.Print($"[Survivor] {SurvivorName} ({Archetype}) died at {GlobalPosition}.");
    }

    /// <summary>Assign a task plus its target and destination.</summary>
    public void SetTask(SurvivorTask task, Node3D? target, Vector3 destination)
    {
        CurrentTask = task;
        TaskTarget = target;
        TaskDestination = destination;
    }
}
