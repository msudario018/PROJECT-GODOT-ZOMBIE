using Godot;

namespace ZombieApocalypse.Core.Autoloads;

/// <summary>Difficulty presets that scale the survival sandbox.</summary>
public enum DifficultyPreset
{
    Casual,
    Standard,
    Hardcore,
}

/// <summary>
/// Settings / difficulty store (blueprint autoload #1).
///
/// Loads first so every other system can read its configuration, and persists
/// to <c>user://settings.cfg</c> on change. Difficulty scalars are consumed by
/// the zombie and loot systems.
/// </summary>
public partial class ConfigManager : Node
{
    public static ConfigManager? Instance { get; private set; }

    public const string SettingsPath = "user://settings.cfg";
    public const string DefaultSavePath = "user://savegame.json";

    // ── Settings ─────────────────────────────────────────────────────
    public float MasterVolume { get; private set; } = 0.8f;
    public float SfxVolume { get; private set; } = 0.9f;
    public float AmbientVolume { get; private set; } = 0.5f;
    public DifficultyPreset Difficulty { get; private set; } = DifficultyPreset.Standard;
    public bool FogOfWarEnabledByDefault { get; private set; } = true;
    public float AutosaveIntervalSeconds { get; private set; } = 300f;
    public string SavePath { get; private set; } = DefaultSavePath;

    // ── Difficulty scalars ───────────────────────────────────────────
    public float ZombieDamageMultiplier => Difficulty switch
    {
        DifficultyPreset.Casual => 0.75f,
        DifficultyPreset.Hardcore => 1.5f,
        _ => 1f,
    };

    public float ZombieSpeedMultiplier => Difficulty switch
    {
        DifficultyPreset.Casual => 0.9f,
        DifficultyPreset.Hardcore => 1.1f,
        _ => 1f,
    };

    public float LootQuantityMultiplier => Difficulty switch
    {
        DifficultyPreset.Casual => 1.35f,
        DifficultyPreset.Hardcore => 0.75f,
        _ => 1f,
    };

    public override void _Ready()
    {
        Instance = this;
        Load();
        GD.Print($"[ConfigManager] Settings loaded (difficulty {Difficulty}, master {MasterVolume:P0}).");
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // ── Persistence ──────────────────────────────────────────────────

    /// <summary>Read settings from disk, falling back to defaults.</summary>
    public void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) != Error.Ok) return;

        MasterVolume = (float)cfg.GetValue("audio", "master", MasterVolume);
        SfxVolume = (float)cfg.GetValue("audio", "sfx", SfxVolume);
        AmbientVolume = (float)cfg.GetValue("audio", "ambient", AmbientVolume);

        string difficultyName = (string)cfg.GetValue("game", "difficulty", Difficulty.ToString());
        if (System.Enum.TryParse<DifficultyPreset>(difficultyName, true, out var parsed))
            Difficulty = parsed;

        FogOfWarEnabledByDefault = (bool)cfg.GetValue("game", "fog_of_war", FogOfWarEnabledByDefault);
        AutosaveIntervalSeconds = (float)cfg.GetValue("game", "autosave_seconds", AutosaveIntervalSeconds);
        SavePath = (string)cfg.GetValue("game", "save_path", SavePath);
    }

    /// <summary>Write current settings to disk.</summary>
    public bool Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("audio", "master", MasterVolume);
        cfg.SetValue("audio", "sfx", SfxVolume);
        cfg.SetValue("audio", "ambient", AmbientVolume);
        cfg.SetValue("game", "difficulty", Difficulty.ToString());
        cfg.SetValue("game", "fog_of_war", FogOfWarEnabledByDefault);
        cfg.SetValue("game", "autosave_seconds", AutosaveIntervalSeconds);
        cfg.SetValue("game", "save_path", SavePath);

        var err = cfg.Save(SettingsPath);
        if (err != Error.Ok)
            GD.PrintErr($"[ConfigManager] Failed to write {SettingsPath}: {err}");
        return err == Error.Ok;
    }

    // ── Mutators (persist immediately) ───────────────────────────────

    public void SetMasterVolume(float value)
    {
        MasterVolume = Mathf.Clamp(value, 0f, 1f);
        AudioManager.Instance?.ApplyVolumes();
        Save();
    }

    public void SetSfxVolume(float value)
    {
        SfxVolume = Mathf.Clamp(value, 0f, 1f);
        AudioManager.Instance?.ApplyVolumes();
        Save();
    }

    public void SetAmbientVolume(float value)
    {
        AmbientVolume = Mathf.Clamp(value, 0f, 1f);
        AudioManager.Instance?.ApplyVolumes();
        Save();
    }

    public void SetDifficulty(DifficultyPreset preset)
    {
        Difficulty = preset;
        Save();
        GD.Print($"[ConfigManager] Difficulty → {preset} " +
                 $"(zombie dmg ×{ZombieDamageMultiplier:F2}, speed ×{ZombieSpeedMultiplier:F2}, loot ×{LootQuantityMultiplier:F2})");
    }

    public void CycleDifficulty()
        => SetDifficulty((DifficultyPreset)(((int)Difficulty + 1) % 3));
}
