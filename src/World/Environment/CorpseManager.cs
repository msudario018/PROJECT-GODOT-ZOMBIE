using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Autoloads;

namespace ZombieApocalypse.World.Environment;

/// <summary>
/// World-level manager that listens to entity deaths and spawns decaying corpses
/// with rotting lifecycles, miasma zones, and cleanup options.
/// </summary>
public partial class CorpseManager : Node3D
{
    public static CorpseManager? Instance { get; private set; }

    [Export] public int MaxCorpsesInWorld = 50;

    private readonly List<Corpse> _activeCorpses = new();

    public override void _Ready()
    {
        Instance = this;

        if (EventBus.Instance != null)
        {
            EventBus.Instance.OnEntityKilled += OnEntityKilled;
        }
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.OnEntityKilled -= OnEntityKilled;
        }

        if (Instance == this)
            Instance = null;
    }

    private void OnEntityKilled(Node entity)
    {
        // Only spawn corpses for zombies / characters, not structural elements
        if (entity is CharacterBody3D body && (body.IsInGroup("zombies") || body.IsInGroup("bandits") || body.IsInGroup("survivors")))
        {
            SpawnCorpse(body.GlobalPosition);
        }
    }

    public Corpse SpawnCorpse(Vector3 position)
    {
        // Enforce max corpse cap to prevent memory bloat
        if (_activeCorpses.Count >= MaxCorpsesInWorld)
        {
            var oldest = _activeCorpses[0];
            _activeCorpses.RemoveAt(0);
            if (GodotObject.IsInstanceValid(oldest))
                oldest.QueueFree();
        }

        var corpse = new Corpse
        {
            Position = new Vector3(position.X, 0.05f, position.Z)
        };

        AddChild(corpse);
        _activeCorpses.Add(corpse);

        corpse.TreeExited += () => _activeCorpses.Remove(corpse);

        GD.Print($"[CorpseManager] Spawned corpse at {position:F1}. Total active corpses: {_activeCorpses.Count}");
        return corpse;
    }

    /// <summary>Free every corpse in the world (used before restoring a save).</summary>
    public int ClearCorpses()
    {
        int cleared = 0;
        foreach (var corpse in _activeCorpses)
        {
            if (GodotObject.IsInstanceValid(corpse))
            {
                corpse.QueueFree();
                cleared++;
            }
        }
        _activeCorpses.Clear();
        return cleared;
    }

    public int GetActiveMiasmaCount()
    {
        int count = 0;
        foreach (var c in _activeCorpses)
        {
            if (GodotObject.IsInstanceValid(c) && c.CurrentStage == CorpseStage.RottingMiasma && !c.IsBuried && !c.IsBurning)
                count++;
        }
        return count;
    }
}
