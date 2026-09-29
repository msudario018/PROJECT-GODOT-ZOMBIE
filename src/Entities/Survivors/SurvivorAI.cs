using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Entities.NPC;

namespace ZombieApocalypse.Entities.Survivors;

/// <summary>
/// Survivor AI controller. Runs a lightweight priority loop:
/// Defend (threats near the camp anchor) → Heal (injured ally) →
/// Scavenge (unsearched containers) — plus Repair, RefuelPower and Rest tasks.
/// </summary>
public partial class SurvivorAI : Node
{
    private SurvivorBase? _survivor;
    private float _repathTimer;

    public override void _Ready()
    {
        _survivor = GetParent() as SurvivorBase;
    }

    public override void _Process(double delta)
    {
        if (_survivor == null || !_survivor.IsAlive) return;

        _repathTimer -= (float)delta;
        if (_repathTimer > 0f) return;
        _repathTimer = SurvivorBase.RepathInterval;

        PickBestTask();
    }

    /// <summary>Evaluate every task type and take the highest-priority valid one.</summary>
    public void PickBestTask()
    {
        if (_survivor == null) return;

        if (TryStartDefend()) return;
        if (TryStartHeal()) return;

        SurvivorTask mode = _survivor.TaskMode;
        if (mode == SurvivorTask.Scavenge && TryStartScavenge()) return;
        if (mode == SurvivorTask.Repair && TryStartRepair()) return;
        if (mode == SurvivorTask.RefuelPower && TryStartRefuel()) return;

        _survivor.SetTask(SurvivorTask.Idle, null, SelectWanderTarget());
    }

    private bool TryStartDefend()
    {
        if (_survivor == null) return false;

        Node3D? threat = SurvivorBase.FindThreatNear(
            _survivor.CampAnchor, SurvivorBase.CampDefenseRadius);
        if (threat == null) return false;

        _survivor.SetTask(SurvivorTask.Defend, threat, threat.GlobalPosition);
        return true;
    }

    private bool TryStartHeal()
    {
        if (_survivor == null) return false;

        Node3D? ally = SurvivorBase.FindInjuredAlly(
            _survivor.GlobalPosition, SurvivorBase.HealSearchRadius);
        if (ally == null) return false;

        var isSurvivor = ally is SurvivorBase;
        var survHealth = isSurvivor ? ((SurvivorBase)ally).Health : null;
        var playerHealth = !isSurvivor ? ally.GetNodeOrNull<HealthComponent>("HealthComponent") : null;

        float fraction = isSurvivor
            ? survHealth!.HealthPercent
            : (playerHealth != null ? playerHealth.HealthPercent : 1f);

        // Only abandon other work for genuinely hurt allies.
        if (fraction >= 0.75f) return false;

        _survivor.SetTask(SurvivorTask.Heal, ally, ally.GlobalPosition);
        return true;
    }

    private bool TryStartScavenge()
    {
        if (_survivor == null) return false;

        var crate = SurvivorBase.FindUnsearchedContainer(
            _survivor.GlobalPosition, SurvivorBase.LootSearchRadius);
        if (crate == null) return false;

        _survivor.SetTask(SurvivorTask.Scavenge, crate, crate.GlobalPosition);
        return true;
    }

    private bool TryStartRepair()
    {
        if (_survivor == null) return false;

        var wall = SurvivorBase.FindDamagedWall(_survivor.GlobalPosition);
        if (wall == null) return false;

        _survivor.SetTask(SurvivorTask.Repair, wall, wall.GlobalPosition);
        return true;
    }

    private bool TryStartRefuel()
    {
        if (_survivor == null) return false;
        if (_survivor.Inventory?.Has("fuel_can") != true) return false;

        var generator = SurvivorBase.FindGenerator(_survivor.GlobalPosition, SurvivorBase.LootSearchRadius);
        if (generator == null) return false;

        _survivor.SetTask(SurvivorTask.RefuelPower, generator, generator.GlobalPosition);
        return true;
    }

    private Vector3 SelectWanderTarget()
    {
        if (_survivor == null) return Vector3.Zero;

        float angle = (float)GD.RandRange(0.0, Mathf.Tau);
        float radius = (float)GD.RandRange(1.0, SurvivorBase.IdleWanderRadius);
        Vector3 anchor = _survivor.CampAnchor;
        return new Vector3(
            anchor.X + Mathf.Cos(angle) * radius,
            0f,
            anchor.Z + Mathf.Sin(angle) * radius);
    }

    /// <summary>External callers (player/debug) can force a mode switch.</summary>
    public void SetMode(SurvivorTask mode)
    {
        if (_survivor != null) _survivor.TaskMode = mode;
    }
}
