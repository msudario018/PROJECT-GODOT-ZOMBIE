using Godot;
using System;
using ZombieApocalypse.Core.Data;

namespace ZombieApocalypse.World.Environment;

public enum DayPhase
{
    Dawn,   // 05:00 - 08:00 (Warm golden transition)
    Day,    // 08:00 - 18:00 (Bright daylight)
    Dusk,   // 18:00 - 21:00 (Deep orange/purple twilight)
    Night   // 21:00 - 05:00 (Pitch darkness, zombie frenzy)
}

/// <summary>
/// Controls the atmospheric 24-hour day/night cycle, adjusting:
///   - DirectionalLight3D sun angle, energy, and tint
///   - WorldEnvironment ambient lighting
///   - Zombie night frenzy modifiers (speed & detection range multiplier)
/// </summary>
public partial class DayNightCycle : Node3D
{
    public static DayNightCycle? Instance { get; private set; }

    [ExportGroup("Time Configuration")]
    /// <summary>Length of a full 24h day in real-time seconds. Default: 360s (6 minutes).</summary>
    [Export] public float DayDurationSeconds = 360f;
    /// <summary>Hour-of-day the clock starts at (pushed once to TimeManager).</summary>
    [Export] public float StartingHour = 12.0f;
    /// <summary>Whether to start with the clock frozen.</summary>
    [Export] public bool StartPaused = false;

    [ExportGroup("Scene References")]
    [Export] public DirectionalLight3D? SunLight;
    [Export] public WorldEnvironment? EnvironmentNode;

    /// <summary>
    /// Floor for ambient light. Without it, night + storm drove ambient down to
    /// ~0.02 and the arena became an unreadable black screen; the fog-of-war
    /// mask then made it look like a rendering failure rather than night.
    /// </summary>
    [Export] public float MinimumAmbientEnergy = 0.20f;

    // ── Public state ─────────────────────────────────────────────────
    // The authoritative clock lives in the TimeManager autoload so time keeps
    // running across scene reloads; this node is the lighting view of it and
    // falls back to a local clock when the autoload is unavailable.

    private static ZombieApocalypse.Core.Autoloads.TimeManager? Time
        => ZombieApocalypse.Core.Autoloads.TimeManager.Instance;

    private float _localHour = 12.0f;
    private DayPhase _localPhase = DayPhase.Day;

    /// <summary>Hour-of-day clock (0-24).</summary>
    public float CurrentHour => Time?.CurrentHour ?? _localHour;
    /// <summary>True when the clock is frozen.</summary>
    public bool IsTimePaused => Time?.IsTimePaused ?? false;
    /// <summary>Current day/night phase.</summary>
    public DayPhase CurrentPhase => Time?.CurrentPhase ?? _localPhase;
    /// <summary>Survival day counter (starts at 1).</summary>
    public int DayCount => Time?.DayCount ?? 1;

    public bool IsNight => CurrentPhase == DayPhase.Night;

    /// <summary>Current weather, or Clear when no WeatherSystem is in the scene.</summary>
    private WeatherState CurrentWeather => WeatherSystem.Instance?.CurrentWeather ?? WeatherState.Clear;

    /// <summary>Zombie speed and hearing multiplier during night frenzy.</summary>
    public float ZombieNightMultiplier => Time?.ZombieNightMultiplier ?? (IsNight ? 1.35f : 1.0f);

    // ── Signals ──────────────────────────────────────────────────────
    [Signal] public delegate void TimeChangedEventHandler(float hour, float minute);
    [Signal] public delegate void DayPhaseChangedEventHandler(int newPhase);

    private DayPhase _previousPhase = DayPhase.Day;

    /// <summary>
    /// Expose the configured ambient floor for the self-test without needing a
    /// scene instance (the default is the legibility floor).
    /// </summary>
    public static float MinimumAmbientEnergyForTests() => 0.20f;

    public override void _Ready()
    {
        Instance = this;

        // Hand the authored scene values to the autoload (first cycle only, so
        // reloading the arena does not rewind the clock).
        Time?.ApplySceneSettings(DayDurationSeconds, StartingHour, StartPaused);
        _localHour = CurrentHour;
        _previousPhase = CurrentPhase;

        if (SunLight == null)
            SunLight = GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D") 
                      ?? GetParent()?.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");

        if (EnvironmentNode == null)
            EnvironmentNode = GetNodeOrNull<WorldEnvironment>("WorldEnvironment")
                             ?? GetParent()?.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");

        if (EnvironmentNode == null)
            EnvironmentNode = GetNodeOrNull<WorldEnvironment>("WorldEnvironment")
                              ?? GetParent()?.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");

        // Let GameManager borrow the ambient floor so the death screen can be
        // made legible without duplicating the value.
        if (Core.Autoloads.GameManager.Instance is { } game)
        {
            game.EnvironmentNode = EnvironmentNode;
            game.MinimumAmbientEnergy = MinimumAmbientEnergy;
        }

        UpdateSunAndAtmosphere(0f);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // Fallback mode: when the TimeManager autoload is absent this node
        // drives the clock itself, keeping the node usable in isolation.
        if (Time == null && !IsTimePaused)
        {
            float hoursPerSecond = 24.0f / Mathf.Max(1f, DayDurationSeconds);
            _localHour = (_localHour + hoursPerSecond * dt) % 24.0f;
            UpdateLocalPhase();
        }

        UpdateSunAndAtmosphere(dt);

        int minute = Mathf.FloorToInt((CurrentHour - Mathf.Floor(CurrentHour)) * 60f);
        EmitSignal(SignalName.TimeChanged, Mathf.Floor(CurrentHour), minute);
    }

    /// <summary>Phase boundaries used in fallback mode (mirrors TimeManager).</summary>
    private void UpdateLocalPhase()
    {
        if (_localHour >= 5.0f && _localHour < 8.0f)
            _localPhase = DayPhase.Dawn;
        else if (_localHour >= 8.0f && _localHour < 18.0f)
            _localPhase = DayPhase.Day;
        else if (_localHour >= 18.0f && _localHour < 21.0f)
            _localPhase = DayPhase.Dusk;
        else
            _localPhase = DayPhase.Night;
    }

    private void UpdateSunAndAtmosphere(float dt)
    {
        // The phase itself is owned by TimeManager; this node only reacts.
        if (CurrentPhase != _previousPhase)
        {
            _previousPhase = CurrentPhase;
            EmitSignal(SignalName.DayPhaseChanged, (int)CurrentPhase);
            GD.Print($"[DayNightCycle] Phase transitioned to: {CurrentPhase} at {GetFormattedTime()}");
        }

        // Sun rotation: 06:00 is sunrise (0° pitch), 12:00 is zenith (90° pitch), 18:00 is sunset (180°)
        // Night rotates under the horizon.
        float sunAngle = (CurrentHour - 6.0f) / 24.0f * Mathf.Tau;
        if (SunLight != null)
        {
            SunLight.Rotation = new Vector3(-Mathf.Sin(sunAngle) * 1.1f, Mathf.Cos(sunAngle) * 0.8f, 0f);

            // Interpolate light energy and color based on phase
            float targetEnergy;
            Color targetColor;

            switch (CurrentPhase)
            {
                case DayPhase.Dawn:
                    float dawnT = (CurrentHour - 5.0f) / 3.0f;
                    targetEnergy = Mathf.Lerp(0.08f, 0.45f, dawnT);
                    targetColor = new Color(1.0f, 0.65f, 0.35f);
                    break;
                case DayPhase.Day:
                    targetEnergy = 0.55f;
                    targetColor = new Color(1.0f, 0.98f, 0.92f);
                    break;
                case DayPhase.Dusk:
                    float duskT = (CurrentHour - 18.0f) / 3.0f;
                    targetEnergy = Mathf.Lerp(0.45f, 0.05f, duskT);
                    targetColor = new Color(0.9f, 0.45f, 0.3f);
                    break;
                case DayPhase.Night:
                default:
                    targetEnergy = 0.04f; // Extremely low key light, reliance on flashlight
                    targetColor = new Color(0.2f, 0.25f, 0.45f);
                    break;
            }

            SunLight.LightEnergy = Mathf.MoveToward(SunLight.LightEnergy, targetEnergy, (dt > 0 ? dt : 1f) * 0.5f);
            SunLight.LightColor = SunLight.LightColor.Lerp(targetColor, (dt > 0 ? dt : 1f) * 0.5f);

            // Overcast skies and storms eat into the daylight the sun would give.
            float weatherFactor = WeatherSystem.Instance?.DaylightFactor ?? 1f;
            if (!Mathf.IsEqualApprox(weatherFactor, 1f))
            {
                SunLight.LightEnergy *= weatherFactor;
                // Rain and fog desaturate the light toward a flat grey.
                if (CurrentWeather is WeatherState.Rain or WeatherState.Storm)
                    SunLight.LightColor = SunLight.LightColor.Lerp(new Color(0.7f, 0.75f, 0.8f), 0.4f);
                else if (CurrentWeather == WeatherState.Fog)
                    SunLight.LightColor = SunLight.LightColor.Lerp(new Color(0.85f, 0.85f, 0.82f), 0.3f);
            }
        }

        // Adjust ambient energy if WorldEnvironment is available
        if (EnvironmentNode?.Environment != null)
        {
            float weatherFactor = WeatherSystem.Instance?.DaylightFactor ?? 1f;
            float targetAmbient = (CurrentPhase switch
            {
                DayPhase.Day => 0.15f,
                DayPhase.Dawn => 0.08f,
                DayPhase.Dusk => 0.06f,
                _ => 0.03f
            }) * Mathf.Lerp(0.6f, 1f, weatherFactor);

            // Never fall below the legibility floor: a black arena reads as a
            // crash, not as night.
            var environment = EnvironmentNode.Environment;
            environment.AmbientLightEnergy = Mathf.MoveToward(
                environment.AmbientLightEnergy,
                Mathf.Max(targetAmbient, MinimumAmbientEnergy),
                (dt > 0 ? dt : 1f) * 0.2f);
        }
    }

    public string GetFormattedTime()
    {
        int h = Mathf.FloorToInt(CurrentHour);
        int m = Mathf.FloorToInt((CurrentHour - h) * 60f);
        return $"{h:D2}:{m:D2} - {CurrentPhase}";
    }
}
