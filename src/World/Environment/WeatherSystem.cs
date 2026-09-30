using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Data;

namespace ZombieApocalypse.World.Environment;

/// <summary>
/// Seasonal weather driver.
///
/// Picks a <see cref="WeatherState"/> each morning from a season-weighted table,
/// derives the <see cref="Season"/> from the survival-day counter, and publishes
/// the gameplay multipliers the rest of the simulation reads:
/// daylight attenuation, thirst drain, miasma suppression and horde pressure.
/// </summary>
public partial class WeatherSystem : Node
{
    public static WeatherSystem? Instance { get; private set; }

    [ExportGroup("Calendar")]
    /// <summary>Survival days per season.</summary>
    [Export] public int DaysPerSeason = 6;
    /// <summary>
    /// Force a specific weather state for testing/scenarios. -1 = roll normally.
    /// Stored as an int because nullable enums cannot be exported.
    /// </summary>
    [Export] public int ForcedWeatherIndex = -1;

    [ExportGroup("Effects")]
    [Export] public float MaxThirstDrainMultiplier = 1.4f;

    /// <summary>Current weather.</summary>
    public WeatherState CurrentWeather { get; private set; } = WeatherState.Clear;
    /// <summary>Current season.</summary>
    public Season CurrentSeason { get; private set; } = Season.Spring;
    /// <summary>Day within the current season (1-based).</summary>
    public int DayOfSeason { get; private set; } = 1;

    /// <summary>How much daylight the weather removes (1.0 = none).</summary>
    public float DaylightFactor => CurrentWeather switch
    {
        WeatherState.Clear => 1.0f,
        WeatherState.Overcast => 0.68f,
        WeatherState.Rain => 0.45f,
        WeatherState.Storm => 0.28f,
        WeatherState.Fog => 0.55f,
        _ => 1f,
    };

    /// <summary>Extra thirst drain (heat and humidity); clamped to a sane band.</summary>
    public float ThirstDrainMultiplier => Mathf.Clamp(
        1f + (CurrentSeason is Season.Summer ? 0.25f : CurrentSeason is Season.Winter ? -0.2f : 0f)
          + (CurrentWeather switch { WeatherState.Storm => 0.15f, WeatherState.Clear => 0.1f, _ => 0f }),
        0.6f, MaxThirstDrainMultiplier);

    /// <summary>0-1 factor reducing miasma damage and spread (rain washes it out).</summary>
    public float MiasmaSuppression => CurrentWeather switch
    {
        WeatherState.Rain => 0.6f,
        WeatherState.Storm => 0.85f,
        _ => 0f,
    };

    /// <summary>Horde pressure multiplier — cold and foul weather embolden the dead.</summary>
    public float HordePressureMultiplier
    {
        get
        {
            float seasonFactor = CurrentSeason switch
            {
                Season.Winter => 1.2f,
                Season.Autumn => 1.1f,
                _ => 1.0f,
            };
            float weatherFactor = CurrentWeather switch
            {
                WeatherState.Fog => 1.15f,
                WeatherState.Storm => 1.1f,
                _ => 1f,
            };
            return seasonFactor * weatherFactor;
        }
    }

    // Relative weights per season. Winters are bleak, summers clear.
    private static readonly Dictionary<Season, (WeatherState state, float weight)[]> _tables = new()
    {
        [Season.Spring] = new[]
        {
            (WeatherState.Clear, 0.45f), (WeatherState.Overcast, 0.3f),
            (WeatherState.Rain, 0.2f),  (WeatherState.Fog, 0.05f),
        },
        [Season.Summer] = new[]
        {
            (WeatherState.Clear, 0.65f), (WeatherState.Overcast, 0.2f),
            (WeatherState.Storm, 0.1f),  (WeatherState.Rain, 0.05f),
        },
        [Season.Autumn] = new[]
        {
            (WeatherState.Overcast, 0.35f), (WeatherState.Fog, 0.3f),
            (WeatherState.Rain, 0.25f),    (WeatherState.Clear, 0.1f),
        },
        [Season.Winter] = new[]
        {
            (WeatherState.Overcast, 0.35f), (WeatherState.Fog, 0.3f),
            (WeatherState.Storm, 0.2f),    (WeatherState.Clear, 0.15f),
        },
    };

    public override void _Ready()
    {
        Instance = this;

        // Align to whatever the clock already knows (survives scene reloads).
        if (TimeManager.Instance != null)
            EvaluateForDay(TimeManager.Instance.DayCount, announce: false);

        GD.Print($"[WeatherSystem] Online — {CurrentSeason}, {CurrentWeather} " +
                 $"(daylight ×{DaylightFactor:F2}, thirst ×{ThirstDrainMultiplier:F2}).");
    }

    public override void _ExitTree()
    {
        if (TimeManager.Instance != null)
            TimeManager.Instance.OnDayRolled -= OnTimeManagerDayRolled;
        if (Instance == this) Instance = null;
    }

    /// <summary>Adapter matching the clock's C# day-rollover event signature.</summary>
    public void EvaluateForDayForSignal(int dayCount) => EvaluateForDay(dayCount, announce: true);

    /// <summary>
    /// Hook the clock for day rollover, so a restored save and a fast-forwarded
    /// clock both re-roll the weather.
    /// </summary>
    public override void _EnterTree()
    {
        if (TimeManager.Instance != null)
            TimeManager.Instance.OnDayRolled += OnTimeManagerDayRolled;
    }

    private void OnTimeManagerDayRolled(int dayCount) => EvaluateForDay(dayCount, announce: true);

    /// <summary>
    /// Recompute season and weather for a given survival day. Public so save/load
    /// and the self-test can drive it deterministically.
    /// </summary>
    public void EvaluateForDay(int dayCount, bool announce = true)
    {
        int seasonLength = Mathf.Max(1, DaysPerSeason);
        int seasonIndex = ((dayCount - 1) / seasonLength) % 4;

        Season previousSeason = CurrentSeason;
        Season season = (Season)seasonIndex;

        CurrentSeason = season;
        DayOfSeason = ((dayCount - 1) % seasonLength) + 1;

        WeatherState weather = ForcedWeatherIndex >= 0
            ? (WeatherState)Mathf.Clamp(ForcedWeatherIndex, 0, 4)
            : RollWeather(season);
        WeatherState previousWeather = CurrentWeather;
        CurrentWeather = weather;

        if (announce && season != previousSeason)
        {
            EventBus.Instance?.EmitSeasonChanged((int)CurrentSeason);
            GD.Print($"[WeatherSystem] {CurrentSeason} begins (day {DayOfSeason}).");
        }

        if (announce && weather != previousWeather)
        {
            EventBus.Instance?.EmitWeatherChanged((int)CurrentWeather);
            GD.Print($"[WeatherSystem] Weather → {CurrentWeather} " +
                     $"(daylight ×{DaylightFactor:F2}, miasma suppression {MiasmaSuppression:P0}).");
        }
    }

    private WeatherState RollWeather(Season season)
    {
        var table = _tables[season];
        float total = 0f;
        foreach (var (_, weight) in table) total += weight;

        float roll = GD.Randf() * total;
        foreach (var (state, weight) in table)
        {
            roll -= weight;
            if (roll <= 0f) return state;
        }
        return table[^1].state;
    }

    public string GetSummaryLine()
        => $"{CurrentSeason} day {DayOfSeason} · {CurrentWeather} · " +
           $"daylight {DaylightFactor:P0} · thirst ×{ThirstDrainMultiplier:F2}";
}
