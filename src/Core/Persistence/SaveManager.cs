using Godot;
using System.Threading.Tasks;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Entities.Bandits;
using ZombieApocalypse.Entities.Survivors;
using ZombieApocalypse.Entities.Zombies;

namespace ZombieApocalypse.Core.Persistence;

/// <summary>
/// Save/load service (blueprint autoload #6).
///
/// Owns the run statistics, serialises the live scene through
/// <see cref="SaveCapture"/> and restores it with <see cref="SaveApply"/>.
/// Loading always goes through a scene reload so runtime-spawned nodes
/// (corpses, bandit caches, player-built walls) are rebuilt from scratch rather
/// than duplicated.
///
/// Registered as autoload "Save".
/// </summary>
public partial class SaveManager : Node
{
    public static SaveManager? Instance { get; private set; }

    [ExportGroup("Persistence")]
    [Export] public bool AutoSaveEnabled = true;
    /// <summary>Save again if the player dies (on top of the restart prompt).</summary>
    [Export] public bool AutoSaveOnDeath = true;

    /// <summary>Run counters, persisted with the save.</summary>
    public StatsData Stats { get; private set; } = new();

    /// <summary>Number of saves written this session (test/HUD hook).</summary>
    public int SavesWritten { get; private set; }
    /// <summary>Number of loads applied this session (test/HUD hook).</summary>
    public int LoadsApplied { get; private set; }

    [Signal] public delegate void GameSavedEventHandler(string path);
    [Signal] public delegate void GameLoadedEventHandler(string path);
    [Signal] public delegate void SaveFailedEventHandler(string reason);

    private float _autoSaveTimer;

    /// <summary>Path the current save is written to and read from.</summary>
    public string SavePath => ConfigManager.Instance?.SavePath ?? ConfigManager.DefaultSavePath;

    public override void _Ready()
    {
        Instance = this;
        _autoSaveTimer = AutoSaveInterval();

        if (EventBus.Instance != null)
        {
            EventBus.Instance.OnEntityKilled += OnEntityKilled;
            EventBus.Instance.OnCorpseBuried += OnCorpseBuried;
        }

        GD.Print($"[SaveManager] Ready. Save path: {SavePath} (autosave every {AutoSaveInterval():F0}s).");
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.OnEntityKilled -= OnEntityKilled;
            EventBus.Instance.OnCorpseBuried -= OnCorpseBuried;
        }
        if (Instance == this) Instance = null;
    }

    public override void _Process(double delta)
    {
        if (!AutoSaveEnabled) return;

        _autoSaveTimer -= (float)delta;
        if (_autoSaveTimer > 0f) return;

        _autoSaveTimer = AutoSaveInterval();
        if (HasSave() || GameManager.Instance?.CurrentState == GameState.Playing)
            SaveGame(reason: "autosave");
    }

    private float AutoSaveInterval()
        => Mathf.Max(30f, ConfigManager.Instance?.AutosaveIntervalSeconds ?? 300f);

    // ── Statistics ───────────────────────────────────────────────────

    private void OnEntityKilled(Node entity)
    {
        if (entity == null) return;

        if (entity is ZombieBase) Stats.ZombieKills++;
        else if (entity is BanditBase) Stats.BanditKills++;
        else if (entity is SurvivorBase) Stats.SurvivorsLost++;
    }

    private void OnCorpseBuried(Vector3 position) => Stats.CorpsesBuried++;

    /// <summary>Count a looted container (called by the save system, exposed for tests).</summary>
    public void NoteContainerSearched() => Stats.ContainersSearched++;

    // ── Public API ───────────────────────────────────────────────────

    /// <summary>Snapshot the live simulation without touching the disk.</summary>
    public SaveData Capture() => SaveCapture.CaptureAll(Stats);

    /// <summary>
    /// Push a snapshot into the live scene. Returns the number of applied
    /// sections; used by <see cref="LoadGame"/> after the reload and directly
    /// by the self-test.
    /// </summary>
    public int ApplyTo(SaveData data)
    {
        if (data == null) return 0;

        int applied = 0;
        if (SaveApply.ApplyClock(data.Clock)) applied++;
        if (SaveApply.ApplyPlayer(data.Player)) applied++;
        if (SaveApply.ApplyPower(data.Power)) applied++;
        if (SaveApply.ApplyCamp(data.Camp)) applied++;
        if (SaveApply.ApplyWorld(data.World) > 0) applied++;

        Stats = data.Stats;
        return applied;
    }

    /// <summary>Capture and write the save file. Returns false if the write failed.</summary>
    public bool SaveGame(string reason = "manual")
    {
        var data = Capture();

        using var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
        if (file == null)
        {
            string reason2 = $"cannot open {SavePath} for writing ({Godot.FileAccess.GetOpenError()})";
            GD.PrintErr($"[SaveManager] Save failed: {reason2}");
            EmitSignal(SignalName.SaveFailed, reason2);
            return false;
        }

        file.StoreString(data.ToJson());

        SavesWritten++;
        GD.Print($"[SaveManager] Saved ({reason}) → {SavePath} " +
                 $"[{data.Clock.DayCount}d {data.Clock.Hour:F1}h, {data.Camp.Survivors.Count} survivors, " +
                 $"{data.World.Containers.Count} containers, {data.World.Corpses.Count} corpses].");
        EmitSignal(SignalName.GameSaved, SavePath);
        return true;
    }

    /// <summary>Read the save file, or null when it is missing/corrupt/wrong version.</summary>
    public SaveData? ReadSave() => ReadSave(SavePath);

    /// <summary>Parse a save file from an arbitrary path.</summary>
    public static SaveData? ReadSave(string path)
    {
        if (!Godot.FileAccess.FileExists(path)) return null;

        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null) return null;

        var data = SaveData.FromJson(file.GetAsText());
        if (data == null)
            GD.PrintErr($"[SaveManager] Could not parse save at {path}.");
        return data;
    }

    public bool HasSave() => Godot.FileAccess.FileExists(SavePath);

    public void DeleteSave()
    {
        if (HasSave())
        {
            Godot.DirAccess.RemoveAbsolute(SavePath);
            GD.Print($"[SaveManager] Deleted save at {SavePath}.");
        }
    }

    /// <summary>
    /// Load the save and rebuild the scene around it: the current scene is
    /// reloaded first so runtime-spawned objects are recreated cleanly, then the
    /// snapshot is applied once the new scene is ready.
    /// </summary>
    public async void LoadGame()
    {
        var data = ReadSave();
        if (data == null)
        {
            EmitSignal(SignalName.SaveFailed, $"no readable save at {SavePath}");
            GD.PrintErr($"[SaveManager] Nothing to load at {SavePath}.");
            return;
        }

        if (data.ScenePath != "" && SaveCapture.CurrentScenePath() != ""
            && data.ScenePath != SaveCapture.CurrentScenePath())
            GD.PushWarning($"[SaveManager] Save was made in '{data.ScenePath}' but the current scene is " +
                           $"'{SaveCapture.CurrentScenePath()}'; loading anyway.");

        GetTree().Paused = false;
        GameManager.Instance?.SetState(GameState.Playing);
        GetTree().ReloadCurrentScene();

        // Wait for the reloaded scene to finish _Ready on its nodes.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        int applied = ApplyTo(data);
        LoadsApplied++;

        GD.Print($"[SaveManager] Loaded {SavePath} — {applied} section(s) applied " +
                 $"(day {data.Clock.DayCount}, {data.Clock.Hour:F1}h).");
        EmitSignal(SignalName.GameLoaded, SavePath);
    }

    /// <summary>Save immediately when the player dies (called by GameManager).</summary>
    public void HandlePlayerDeath()
    {
        if (AutoSaveOnDeath)
            SaveGame(reason: "player death");
    }
}
