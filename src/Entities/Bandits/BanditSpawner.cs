using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Entities.NPC;
using ZombieApocalypse.Entities.Survivors;

namespace ZombieApocalypse.Entities.Bandits;

/// <summary>
/// Debug / event spawner for bandit raids. Timers can auto-launch raids;
/// HUD debug keys and headless tests call <see cref="SpawnSquad"/> directly.
/// </summary>
public partial class BanditSpawner : Node3D
{
    [ExportGroup("Raid Cadence")]
    [Export] public bool AutoRaid = false;
    [Export] public float RaidInterval = 120f;
    [Export] public BanditTier RaidTier = BanditTier.Scavenger;
    [Export] public int RaidSquadSize = 3;
    [Export] public float SpawnRadius = 20f;

    public readonly List<BanditSquad> ActiveSquads = new();

    private float _raidTimer;

    public override void _Ready()
    {
        AddToGroup("bandit_spawners");
        _raidTimer = RaidInterval;
    }

    public override void _Process(double delta)
    {
        if (!AutoRaid) return;

        _raidTimer -= (float)delta;
        if (_raidTimer > 0f) return;
        _raidTimer = RaidInterval;

        SpawnSquad(RaidTier, RaidSquadSize);
    }

    /// <summary>Spawn a squad on a ring around the player (or the spawner).</summary>
    public BanditSquad SpawnSquad(BanditTier tier, int size)
    {
        Vector3 center = GlobalPosition;
        var player = GetTree().GetFirstNodeInGroup("player") as Node3D;
        if (player != null) center = player.GlobalPosition;

        float angle = (float)GD.RandRange(0.0, Mathf.Tau);
        Vector3 origin = new(
            center.X + Mathf.Cos(angle) * SpawnRadius,
            0f,
            center.Z + Mathf.Sin(angle) * SpawnRadius);

        return SpawnSquadAt(tier, size, origin);
    }

    /// <summary>Spawn a squad at an exact position. Deterministic; used by tests.</summary>
    public BanditSquad SpawnSquadAt(BanditTier tier, int size, Vector3 origin)
    {
        var squad = new BanditSquad
        {
            Name = $"Raid_{ActiveSquads.Count + 1}",
            SquadName = $"Raid {ActiveSquads.Count + 1}",
            Tier = tier,
            SquadSize = size,
            SpawnOrigin = origin,
        };

        Node parent = GetParent() ?? this;
        parent.AddChild(squad);
        squad.Deploy();

        ActiveSquads.Add(squad);
        squad.TreeExited += () => ActiveSquads.Remove(squad);
        return squad;
    }
}
