using Godot;
using System;
using System.Collections.Generic;
// The save DTOs use Godot's Variant-backed dictionary (JSON friendly).
using Dictionary = Godot.Collections.Dictionary;

namespace ZombieApocalypse.Core.Persistence;

/// <summary>
/// Small, defensive helpers for reading/writing the save dictionary.
/// Everything is accessed through these so a truncated or hand-edited save
/// degrades to defaults instead of throwing mid-load.
/// </summary>
public static class SaveJson
{
    public static bool Has(Dictionary d, string key) => d.ContainsKey(key);

    public static float F(Dictionary d, string key, float fallback = 0f)
        => d.ContainsKey(key) ? d[key].AsSingle() : fallback;

    public static int I(Dictionary d, string key, int fallback = 0)
        => d.ContainsKey(key) ? d[key].AsInt32() : fallback;

    public static long L(Dictionary d, string key, long fallback = 0)
        => d.ContainsKey(key) ? d[key].AsInt64() : fallback;

    public static bool B(Dictionary d, string key, bool fallback = false)
        => d.ContainsKey(key) ? d[key].AsBool() : fallback;

    public static string S(Dictionary d, string key, string fallback = "")
        => d.ContainsKey(key) ? d[key].AsString() : fallback;

    public static Dictionary D(Dictionary d, string key)
        => d.ContainsKey(key) ? d[key].AsGodotDictionary() : new Dictionary();

    public static Godot.Collections.Array A(Dictionary d, string key)
        => d.ContainsKey(key) ? d[key].AsGodotArray() : new Godot.Collections.Array();

    public static Godot.Collections.Array Vec(Vector3 v) => new() { v.X, v.Y, v.Z };

    public static Vector3 V(Godot.Collections.Array a)
        => a.Count >= 3 ? new Vector3(a[0].AsSingle(), a[1].AsSingle(), a[2].AsSingle()) : Vector3.Zero;

    /// <summary>Encode a list of DTOs through a per-item encoder.</summary>
    public static Godot.Collections.Array Encode<T>(IEnumerable<T> items, Func<T, Dictionary> encode)
    {
        var array = new Godot.Collections.Array();
        foreach (var item in items)
            array.Add(encode(item));
        return array;
    }

    /// <summary>Decode a list of DTOs through a per-item decoder.</summary>
    public static List<T> DecodeList<T>(Godot.Collections.Array array, Func<Dictionary, T> decode)
    {
        var list = new List<T>(array.Count);
        foreach (var entry in array)
            list.Add(decode(entry.AsGodotDictionary()));
        return list;
    }

    public static List<T> DecodeList<T>(Dictionary d, string key, Func<Dictionary, T> decode)
        => DecodeList(A(d, key), decode);
}

/// <summary>Clock and phase state, owned by the TimeManager autoload.</summary>
public class ClockData
{
    public float Hour = 12f;
    public int DayCount = 1;
    public bool IsPaused = false;
    public float TimeScale = 1f;

    public Dictionary ToDictionary() => new()
    {
        { "hour", Hour },
        { "day", DayCount },
        { "paused", IsPaused },
        { "time_scale", TimeScale },
    };

    public static ClockData FromDictionary(Dictionary d) => new()
    {
        Hour = SaveJson.F(d, "hour", 12f),
        DayCount = Mathf.Max(1, SaveJson.I(d, "day", 1)),
        IsPaused = SaveJson.B(d, "paused"),
        TimeScale = Mathf.Max(0f, SaveJson.F(d, "time_scale", 1f)),
    };
}

/// <summary>One inventory slot: an item id plus its stack size.</summary>
public class SlotData
{
    public string ItemId = "unknown";
    public int Quantity = 0;

    public SlotData() { }
    public SlotData(string itemId, int quantity) { ItemId = itemId; Quantity = quantity; }

    public Dictionary ToDictionary() => new() { { "item", ItemId }, { "qty", Quantity } };

    public static SlotData FromDictionary(Dictionary d)
        => new(SaveJson.S(d, "item", "unknown"), SaveJson.I(d, "qty"));
}

/// <summary>The player: position, vitals, carried gear and flashlight state.</summary>
public class PlayerData
{
    public Vector3 Position = Vector3.Zero;
    public float YawDegrees = 0f;
    public float Health = 100f;
    public float MaxHealth = 100f;
    public float Hunger = 80f;
    public float Thirst = 80f;
    public float Stamina = 100f;
    public bool FlashlightOn = true;
    public List<SlotData> Slots = new();

    public Dictionary ToDictionary() => new()
    {
        { "position", SaveJson.Vec(Position) },
        { "yaw", YawDegrees },
        { "health", Health },
        { "max_health", MaxHealth },
        { "hunger", Hunger },
        { "thirst", Thirst },
        { "stamina", Stamina },
        { "flashlight", FlashlightOn },
        { "slots", SaveJson.Encode(Slots, s => s.ToDictionary()) },
    };

    public static PlayerData FromDictionary(Dictionary d) => new()
    {
        Position = SaveJson.V(SaveJson.A(d, "position")),
        YawDegrees = SaveJson.F(d, "yaw"),
        Health = SaveJson.F(d, "health", 100f),
        MaxHealth = Mathf.Max(1f, SaveJson.F(d, "max_health", 100f)),
        Hunger = SaveJson.F(d, "hunger", 80f),
        Thirst = SaveJson.F(d, "thirst", 80f),
        Stamina = SaveJson.F(d, "stamina", 100f),
        FlashlightOn = SaveJson.B(d, "flashlight", true),
        Slots = SaveJson.DecodeList(d, "slots", SlotData.FromDictionary),
    };
}

/// <summary>Run-wide counters surfaced on the HUD and preserved across loads.</summary>
public class StatsData
{
    public int ZombieKills = 0;
    public int BanditKills = 0;
    public int SurvivorsLost = 0;
    public int CorpsesBuried = 0;
    public int ContainersSearched = 0;

    public Dictionary ToDictionary() => new()
    {
        { "zombie_kills", ZombieKills },
        { "bandit_kills", BanditKills },
        { "survivors_lost", SurvivorsLost },
        { "corpses_buried", CorpsesBuried },
        { "containers_searched", ContainersSearched },
    };

    public static StatsData FromDictionary(Dictionary d) => new()
    {
        ZombieKills = SaveJson.I(d, "zombie_kills"),
        BanditKills = SaveJson.I(d, "bandit_kills"),
        SurvivorsLost = SaveJson.I(d, "survivors_lost"),
        CorpsesBuried = SaveJson.I(d, "corpses_buried"),
        ContainersSearched = SaveJson.I(d, "containers_searched"),
    };
}

/// <summary>
/// Root of the save file: version, timestamp, and one section per subsystem.
///
/// A save is a plain snapshot of authoritative state (not a scene diff): the
/// arena scene is authored, so loading only needs to push the mutable numbers
/// back into the nodes that already exist.
/// </summary>
public class SaveData
{
    /// <summary>Bump when a section's shape changes; older files are rejected.</summary>
    public const int CurrentVersion = 1;

    public int Version = CurrentVersion;
    public long SavedAtUnix = 0;
    public string ScenePath = "";

    public ClockData Clock = new();
    public PlayerData Player = new();
    public PowerData Power = new();
    public CampData Camp = new();
    public WorldData World = new();
    public StatsData Stats = new();

    public Dictionary ToDictionary() => new()
    {
        { "version", Version },
        { "saved_at", SavedAtUnix },
        { "scene", ScenePath },
        { "clock", Clock.ToDictionary() },
        { "player", Player.ToDictionary() },
        { "power", Power.ToDictionary() },
        { "camp", Camp.ToDictionary() },
        { "world", World.ToDictionary() },
        { "stats", Stats.ToDictionary() },
    };

    public static SaveData FromDictionary(Dictionary d) => new()
    {
        Version = SaveJson.I(d, "version", CurrentVersion),
        SavedAtUnix = SaveJson.L(d, "saved_at"),
        ScenePath = SaveJson.S(d, "scene"),
        Clock = ClockData.FromDictionary(SaveJson.D(d, "clock")),
        Player = PlayerData.FromDictionary(SaveJson.D(d, "player")),
        Power = PowerData.FromDictionary(SaveJson.D(d, "power")),
        Camp = CampData.FromDictionary(SaveJson.D(d, "camp")),
        World = WorldData.FromDictionary(SaveJson.D(d, "world")),
        Stats = StatsData.FromDictionary(SaveJson.D(d, "stats")),
    };

    public string ToJson() => Json.Stringify(ToDictionary(), "  ");

    /// <summary>
    /// Parse a save file. Returns null for malformed JSON or an unknown version.
    ///
    /// Never throws: a corrupt or hand-edited save must degrade to "no save"
    /// rather than abort whatever load flow called us.
    /// </summary>
    public static SaveData? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var parsed = Json.ParseString(json);
            if (parsed.VariantType != Variant.Type.Dictionary) return null;

            var data = FromDictionary(parsed.AsGodotDictionary());
            if (data.Version != CurrentVersion)
            {
                GD.Print($"[SaveData] Ignoring save with unsupported version " +
                         $"{data.Version} (expected {CurrentVersion}).");
                return null;
            }
            return data;
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"[SaveData] Malformed save rejected: {e.Message}");
            return null;
        }
    }
}
