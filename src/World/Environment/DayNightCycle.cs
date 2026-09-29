using Godot;
using System;

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
    /// <summary>Current time in hours (0.00 to 23.99). Defaults to 12.0 (Noon).</summary>
    [Export] public float CurrentHour = 12.0f;
    [Export] public bool IsTimePaused = false;

    [ExportGroup("Scene References")]
    [Export] public DirectionalLight3D? SunLight;
    [Export] public WorldEnvironment? EnvironmentNode;

    // ── Public State ─────────────────────────────────────────────────
    public DayPhase CurrentPhase { get; private set; } = DayPhase.Day;
    public bool IsNight => CurrentPhase == DayPhase.Night;

    /// <summary>Zombie speed and hearing multiplier during night frenzy.</summary>
    public float ZombieNightMultiplier => IsNight ? 1.35f : 1.0f;

    // ── Signals ──────────────────────────────────────────────────────
    [Signal] public delegate void TimeChangedEventHandler(float hour, float minute);
    [Signal] public delegate void DayPhaseChangedEventHandler(int newPhase);

    private DayPhase _previousPhase = DayPhase.Day;

    public override void _Ready()
    {
        Instance = this;

        if (SunLight == null)
            SunLight = GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D") 
                      ?? GetParent()?.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");

        if (EnvironmentNode == null)
            EnvironmentNode = GetNodeOrNull<WorldEnvironment>("WorldEnvironment")
                             ?? GetParent()?.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");

        UpdateSunAndAtmosphere(0f);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Process(double delta)
    {
        if (IsTimePaused) return;

        float dt = (float)delta;
        float hoursPerSecond = 24.0f / DayDurationSeconds;
        CurrentHour = (CurrentHour + hoursPerSecond * dt) % 24.0f;

        UpdateSunAndAtmosphere(dt);

        int minute = Mathf.FloorToInt((CurrentHour - Mathf.Floor(CurrentHour)) * 60f);
        EmitSignal(SignalName.TimeChanged, Mathf.Floor(CurrentHour), minute);
    }

    private void UpdateSunAndAtmosphere(float dt)
    {
        // Determine phase
        if (CurrentHour >= 5.0f && CurrentHour < 8.0f)
            CurrentPhase = DayPhase.Dawn;
        else if (CurrentHour >= 8.0f && CurrentHour < 18.0f)
            CurrentPhase = DayPhase.Day;
        else if (CurrentHour >= 18.0f && CurrentHour < 21.0f)
            CurrentPhase = DayPhase.Dusk;
        else
            CurrentPhase = DayPhase.Night;

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
        }

        // Adjust ambient energy if WorldEnvironment is available
        if (EnvironmentNode?.Environment != null)
        {
            float targetAmbient = CurrentPhase switch
            {
                DayPhase.Day => 0.15f,
                DayPhase.Dawn => 0.08f,
                DayPhase.Dusk => 0.06f,
                _ => 0.03f
            };
            EnvironmentNode.Environment.AmbientLightEnergy = Mathf.MoveToward(
                EnvironmentNode.Environment.AmbientLightEnergy, targetAmbient, (dt > 0 ? dt : 1f) * 0.2f);
        }
    }

    public string GetFormattedTime()
    {
        int h = Mathf.FloorToInt(CurrentHour);
        int m = Mathf.FloorToInt((CurrentHour - h) * 60f);
        return $"{h:D2}:{m:D2} - {CurrentPhase}";
    }
}
