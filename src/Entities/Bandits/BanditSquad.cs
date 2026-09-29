using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Utilities;
using ZombieApocalypse.Entities.NPC;
using ZombieApocalypse.Entities.Survivors;

namespace ZombieApocalypse.Entities.Bandits;

/// <summary>
/// Squad coordinator: leader, role assignment, group morale and retreat logic.
/// Owns its members, assigns Advance/Suppress/Flank roles, and retreats the
/// whole squad when morale collapses.
/// </summary>
public partial class BanditSquad : Node3D
{
    [ExportGroup("Squad")]
    [Export] public string SquadName = "Raider Squad";
    [Export] public BanditTier Tier = BanditTier.Scavenger;
    [Export] public int SquadSize = 3;
    [Export] public bool AllowLooting = true;
    [Export] public Vector3 SpawnOrigin = Vector3.Zero;
    [Export] public float RallyRadius = 12.0f;

    public readonly List<BanditBase> Members = new();
    public float SquadMorale { get; private set; } = 1.0f;
    public bool IsEliminated => Members.Count == 0;
    public bool IsRetreating { get; private set; }

    private float _roleTimer;
    private float _rallyTimer;
    private bool _raidAnnounced;

    public override void _Ready()
    {
        AddToGroup("bandit_squads");
    }

    public override void _ExitTree()
    {
        RemoveFromGroup("bandit_squads");
    }

    /// <summary>Spawn members around <see cref="SpawnOrigin"/> and register them.</summary>
    public void Deploy()
    {
        for (int i = 0; i < SquadSize; i++)
        {
            var member = new BanditBase
            {
                Name = $"{SquadName}_{i + 1}",
                Tier = Tier,
                BanditName = $"{SquadName} #{i + 1}",
                Position = SpawnOrigin + new Vector3(
                    (float)GD.RandRange(-3.0, 3.0), 0f, (float)GD.RandRange(-3.0, 3.0)),
            };
            member.Squad = this;
            AddChild(member);
            Members.Add(member);
        }

        AssignRoles();
        SquadMorale = 1.0f;
        IsRetreating = false;

        if (!_raidAnnounced)
        {
            _raidAnnounced = true;
            EventBus.Instance?.EmitBanditRaidIncoming(this);
            GD.Print($"[BanditSquad] {SquadName} ({Tier}, {SquadSize} raiders) incoming at {SpawnOrigin}.");
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        _roleTimer -= dt;
        if (_roleTimer <= 0f)
        {
            _roleTimer = 3.0f;
            AssignRoles();
        }

        _rallyTimer -= dt;
        if (_rallyTimer <= 0f)
        {
            _rallyTimer = 5.0f;
            CheckRally();
        }
    }

    /// <summary>Called by a member when it dies. Drops squad morale; mass retreat at zero.</summary>
    public void NotifyMemberDied(BanditBase member)
    {
        Members.Remove(member);

        SquadMorale = Mathf.Max(0f, SquadMorale - (member.Role == SquadRole.Advance ? 0.45f : 0.3f));
        GD.Print($"[BanditSquad] {SquadName} lost {member.BanditName}. Morale {SquadMorale:P0}.");

        if (Members.Count == 0)
        {
            EventBus.Instance?.EmitBanditSquadEliminated(this);
            GD.Print($"[BanditSquad] {SquadName} eliminated.");
            QueueFree();
            return;
        }

        if (SquadMorale <= 0.01f && !IsRetreating)
        {
            IsRetreating = true;
            foreach (var survivor in Members)
            {
                if (GodotObject.IsInstanceValid(survivor) && !survivor.IsDead)
                {
                    survivor.CombatState = BanditCombatState.Retreat;
                    survivor.CombatTarget = null;
                }
            }
            GD.Print($"[BanditSquad] {SquadName} breaks — mass retreat!");
        }

        AssignRoles();
    }

    // ── Roles ────────────────────────────────────────────────────────

    // ── Rally & targeting ────────────────────────────────────────────

    /// <summary>
    /// If the squad drifted apart, pull stragglers toward the leader.
    /// Called periodically; cheap group cohesion without formation math.
    /// </summary>
    public void CheckRally()
    {
        var leader = GetLeader();
        if (leader == null) return;

        foreach (var member in Members)
        {
            if (!GodotObject.IsInstanceValid(member) || member == leader || member.IsDead)
                continue;
            if (member.CombatState == BanditCombatState.Retreat)
                continue;

            float distSq = MathUtils.DistanceSquaredXZ(member.GlobalPosition, leader.GlobalPosition);
            if (distSq > RallyRadius * RallyRadius)
            {
                member.CombatTarget = null;
                member.HasFlankPoint = false;
                member.CombatState = BanditCombatState.Advance;
            }
        }
    }

    public BanditBase? GetLeader()
    {
        foreach (var m in Members)
            if (GodotObject.IsInstanceValid(m) && !m.IsDead)
                return m;
        return null;
    }

    /// <summary>Nearest living survivor, player, or zombie — bandits fight anything.</summary>
    public static Node3D? FindPreyNear(Vector3 from, float radius)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return null;

        Node3D? best = null;
        float bestSq = radius * radius;

        foreach (var group in new[] { "survivors", "player", "zombies" })
        {
            foreach (var node in tree.GetNodesInGroup(group))
            {
                if (node is not Node3D target || !GodotObject.IsInstanceValid(target)) continue;
                if (target is Zombies.ZombieBase zb && zb.IsDead) continue;
                if (target is SurvivorBase sv && !sv.IsAlive) continue;
                if (target is BanditBase) continue;

                var health = target.GetNodeOrNull<HealthComponent>("HealthComponent");
                if (health != null && !health.IsAlive) continue;

                float distSq = MathUtils.DistanceSquaredXZ(from, target.GlobalPosition);
                if (distSq < bestSq)
                {
                    bestSq = distSq;
                    best = target;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Deterministic side-step point for a flanker: perpendicular to the
    /// attacker→target axis, 6 m out. Pure math, safe for headless tests.
    /// </summary>
    public static Vector3 ComputeFlankPoint(Vector3 attacker, Vector3 target)
    {
        Vector3 axis = target - attacker;
        axis.Y = 0f;
        if (axis.LengthSquared() < 0.01f)
            return target + new Vector3(6f, 0f, 0f);

        Vector3 side = new Vector3(-axis.Z, 0f, axis.X).Normalized();
        return target + side * 6.0f;
    }


    /// <summary>Leader takes Advance; others split Suppress/Flank by index.</summary>
    public void AssignRoles()
    {
        var alive = new List<BanditBase>();
        foreach (var m in Members)
            if (GodotObject.IsInstanceValid(m) && !m.IsDead)
                alive.Add(m);

        for (int i = 0; i < alive.Count; i++)
        {
            var member = alive[i];
            if (IsRetreating)
            {
                member.CombatState = BanditCombatState.Retreat;
                continue;
            }

            member.Role = i switch
            {
                0 => SquadRole.Advance,
                1 => SquadRole.Suppress,
                _ => (i % 2 == 0) ? SquadRole.Flank : SquadRole.Suppress,
            };

            if (member.CombatState is BanditCombatState.Retreat or BanditCombatState.Dead)
                continue;

            member.CombatState = member.Role switch
            {
                SquadRole.Advance => BanditCombatState.Advance,
                SquadRole.Suppress => BanditCombatState.Suppress,
                _ => BanditCombatState.Flank,
            };
        }
    }
}
