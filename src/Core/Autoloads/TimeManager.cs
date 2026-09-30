using Godot;
using ZombieApocalypse.World.Environment;

namespace ZombieApocalypse.Core.Autoloads;

/// <summary>
/// Authoritative game clock (blueprint autoload #4).
///
/// Owns the hour-of-day, day counter and day/night phase so time survives
/// scene reloads and can be saved/loaded. <see cref="DayNightCycle"/> becomes
/// a pure view: it registers itself here and only animates the sun/ambient
/// lighting for the current hour.
///
/// Registered as the "TimeManager" autoload (named to avoid shadowing Godot's
/// built-in <c>Time</c> singleton).
/// </summary>
public partial class TimeManager : Node
{
    public static TimeManager? Instance { get; private set; }

    [ExportGroup("Time")]
    /// <summary>Real-time seconds for a full 24 h cycle.</summary>
    [Export] public float DayDurationSeconds = 360f;
    [Export] public float CurrentHour = 12f;
    [Export] public bool IsTimePaused = false;
    [Export] public float TimeScale = 1f;

    [ExportGroup("Night Frenzy")]
    [Export] public float NightSpeedMultiplier = 1.35f;
    [Export] public float DawnHour = 5f;
    [Export] public float DayHour = 8f;
    [Export] public float DuskHour = 18f;
    [Export] public float NightHour = 21f;

    public int DayCount { get; private set; } = 1;
    public DayPhase CurrentPhase { get; private set; } = DayPhase.Day;
    public bool IsNight => CurrentPhase == DayPhase.Night;
    public float ZombieNightMultiplier => IsNight ? NightSpeedMultiplier : 1f;

    /// <summary>True once a scene has pushed its inspector values into this autoload.</summary>
    public bool SceneSettingsApplied { get; private set; }

    [Signal] public delegate void TimeChangedEventHandler(float hour, float minute);
    [Signal] public delegate void DayPhaseChangedEventHandler(int newPhase);
    [Signal] public delegate void DayRolledEventHandler(int dayCount);

    public override void _Ready()
    {
        Instance = this;
        RecomputePhase(announce: false);
        GD.Print("[TimeManager] Clock online (starts at 12:00, 1 of 360s days).");
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Process(double delta)
    {
        if (IsTimePaused) return;

        float dt = (float)delta;
        float hoursPerSecond = DayDurationSeconds > 0.01f ? 24f / DayDurationSeconds : 0f;
        float previous = CurrentHour;
        CurrentHour = (CurrentHour + hoursPerSecond * dt * TimeScale) % 24f;

        if (CurrentHour < previous)
        {
            DayCount++;
            EmitSignal(SignalName.DayRolled, DayCount);
            GD.Print($"[TimeManager] Day {DayCount} has begun.");
        }

        RecomputePhase(announce: true);

        int minute = Mathf.FloorToInt((CurrentHour - Mathf.Floor(CurrentHour)) * 60f);
        EmitSignal(SignalName.TimeChanged, Mathf.Floor(CurrentHour), minute);
    }

    /// <summary>Adopt the values authored on the scene's DayNightCycle (once per session).</summary>
    public void ApplySceneSettings(float dayDurationSeconds, float currentHour, bool isPaused)
    {
        if (SceneSettingsApplied) return;

        DayDurationSeconds = dayDurationSeconds;
        CurrentHour = Mathf.PosMod(currentHour, 24f);
        IsTimePaused = isPaused;
        SceneSettingsApplied = true;
        RecomputePhase(announce: false);
    }

    /// <summary>Test/debug hook: jump the clock and recompute the phase.</summary>
    public void SetHour(float hour)
    {
        CurrentHour = Mathf.PosMod(hour, 24f);
        RecomputePhase(announce: false);
    }

    /// <summary>Restore the clock to a saved day/hour (save/load).</summary>
    public void RestoreState(int dayCount, float hour, bool isPaused)
    {
        DayCount = Mathf.Max(1, dayCount);
        CurrentHour = Mathf.PosMod(hour, 24f);
        IsTimePaused = isPaused;
        RecomputePhase(announce: false);
    }

    /// <summary>Deterministic clock advance for tests and save/load fast-forward.</summary>
    public void Advance(float hours)
    {
        float previous = CurrentHour;
        CurrentHour = Mathf.PosMod(CurrentHour + hours, 24f);
        if (CurrentHour < previous)
        {
            DayCount++;
            EmitSignal(SignalName.DayRolled, DayCount);
        }
        RecomputePhase(announce: true);
    }

    private void RecomputePhase(bool announce)
    {
        DayPhase previousPhase = CurrentPhase;

        if (CurrentHour >= DawnHour && CurrentHour < DayHour)
            CurrentPhase = DayPhase.Dawn;
        else if (CurrentHour >= DayHour && CurrentHour < DuskHour)
            CurrentPhase = DayPhase.Day;
        else if (CurrentHour >= DuskHour && CurrentHour < NightHour)
            CurrentPhase = DayPhase.Dusk;
        else
            CurrentPhase = DayPhase.Night;

        if (previousPhase != CurrentPhase)
        {
            EmitSignal(SignalName.DayPhaseChanged, (int)CurrentPhase);
            if (announce)
                GD.Print($"[TimeManager] Phase → {CurrentPhase} at {GetFormattedTime()}");
        }
    }

    public string GetFormattedTime()
    {
        int h = Mathf.FloorToInt(CurrentHour);
        int m = Mathf.FloorToInt((CurrentHour - h) * 60f);
        return $"Day {DayCount} {h:D2}:{m:D2} - {CurrentPhase}";
    }
}
