using Godot;
using System.Collections.Generic;
using Dictionary = Godot.Collections.Dictionary;

namespace ZombieApocalypse.Core.Persistence;

/// <summary>Generator, battery and cable state (the grid topology itself is scene-static).</summary>
public class PowerData
{
    public bool GeneratorRunning = true;
    public float GeneratorFuel = 2f;
    public float BatteryChargeWattHours = 0f;
    public List<string> SeveredCables = new();

    public Dictionary ToDictionary() => new()
    {
        { "generator_running", GeneratorRunning },
        { "generator_fuel", GeneratorFuel },
        { "battery_wh", BatteryChargeWattHours },
        { "severed_cables", SaveJson.Encode(SeveredCables, s => new Dictionary { { "name", s } }) },
    };

    public static PowerData FromDictionary(Dictionary d) => new()
    {
        GeneratorRunning = SaveJson.B(d, "generator_running", true),
        GeneratorFuel = Mathf.Max(0f, SaveJson.F(d, "generator_fuel")),
        BatteryChargeWattHours = Mathf.Max(0f, SaveJson.F(d, "battery_wh")),
        SeveredCables = SaveJson.DecodeList(d, "severed_cables", c => SaveJson.S(c, "name")),
    };
}

/// <summary>One survivor: identity, vitals, position, task and what they are carrying.</summary>
public class SurvivorData
{
    public string Name = "Survivor";
    public int Archetype = 0;
    public Vector3 Position = Vector3.Zero;
    public float Health = 100f;
    public bool Alive = true;
    public int Task = 0;
    public int TaskMode = 0;
    public List<SlotData> Slots = new();

    public Dictionary ToDictionary() => new()
    {
        { "name", Name },
        { "archetype", Archetype },
        { "position", SaveJson.Vec(Position) },
        { "health", Health },
        { "alive", Alive },
        { "task", Task },
        { "task_mode", TaskMode },
        { "slots", SaveJson.Encode(Slots, s => s.ToDictionary()) },
    };

    public static SurvivorData FromDictionary(Dictionary d) => new()
    {
        Name = SaveJson.S(d, "name", "Survivor"),
        Archetype = SaveJson.I(d, "archetype"),
        Position = SaveJson.V(SaveJson.A(d, "position")),
        Health = SaveJson.F(d, "health", 100f),
        Alive = SaveJson.B(d, "alive", true),
        Task = SaveJson.I(d, "task"),
        TaskMode = SaveJson.I(d, "task_mode"),
        Slots = SaveJson.DecodeList(d, "slots", SlotData.FromDictionary),
    };
}

/// <summary>Camp-level state: morale, the stockpile manifest and the airdrop.</summary>
public class CampData
{
    public float Morale = 50f;
    public List<SlotData> Stockpile = new();
    public bool AirdropDropped = false;
    public Vector3 AirdropPosition = Vector3.Zero;
    public List<SurvivorData> Survivors = new();

    public Dictionary ToDictionary() => new()
    {
        { "morale", Morale },
        { "stockpile", SaveJson.Encode(Stockpile, s => s.ToDictionary()) },
        { "airdrop_dropped", AirdropDropped },
        { "airdrop_position", SaveJson.Vec(AirdropPosition) },
        { "survivors", SaveJson.Encode(Survivors, s => s.ToDictionary()) },
    };

    public static CampData FromDictionary(Dictionary d) => new()
    {
        Morale = Mathf.Clamp(SaveJson.F(d, "morale", 50f), 0f, 100f),
        Stockpile = SaveJson.DecodeList(d, "stockpile", SlotData.FromDictionary),
        AirdropDropped = SaveJson.B(d, "airdrop_dropped"),
        AirdropPosition = SaveJson.V(SaveJson.A(d, "airdrop_position")),
        Survivors = SaveJson.DecodeList(d, "survivors", SurvivorData.FromDictionary),
    };
}

/// <summary>Searched/emptied state of a scene-placed loot container.</summary>
public class ContainerData
{
    public string Path = "";
    public bool Searched = false;

    public ContainerData() { }
    public ContainerData(string path, bool searched) { Path = path; Searched = searched; }

    public Dictionary ToDictionary() => new() { { "path", Path }, { "searched", Searched } };

    public static ContainerData FromDictionary(Dictionary d)
        => new(SaveJson.S(d, "path"), SaveJson.B(d, "searched"));
}

/// <summary>A corpse: where it is, how decomposed, and whether it was looted or buried.</summary>
public class CorpseData
{
    public Vector3 Position = Vector3.Zero;
    public int Stage = 0;
    public float AgeSeconds = 0f;
    public bool Searched = false;
    public bool Buried = false;

    public Dictionary ToDictionary() => new()
    {
        { "position", SaveJson.Vec(Position) },
        { "stage", Stage },
        { "age", AgeSeconds },
        { "searched", Searched },
        { "buried", Buried },
    };

    public static CorpseData FromDictionary(Dictionary d) => new()
    {
        Position = SaveJson.V(SaveJson.A(d, "position")),
        Stage = SaveJson.I(d, "stage"),
        AgeSeconds = Mathf.Max(0f, SaveJson.F(d, "age")),
        Searched = SaveJson.B(d, "searched"),
        Buried = SaveJson.B(d, "buried"),
    };
}

/// <summary>A player-built wall/door: type index, transform and remaining durability.</summary>
public class StructureData
{
    /// <summary>Build type index: 1 = wood fence, 2 = chain link, 3 = door.</summary>
    public int BuildType = 1;
    public Vector3 Position = Vector3.Zero;
    public float YawDegrees = 0f;
    public float Health = 250f;

    public Dictionary ToDictionary() => new()
    {
        { "type", BuildType },
        { "position", SaveJson.Vec(Position) },
        { "yaw", YawDegrees },
        { "health", Health },
    };

    public static StructureData FromDictionary(Dictionary d) => new()
    {
        BuildType = Mathf.Clamp(SaveJson.I(d, "type", 1), 1, 3),
        Position = SaveJson.V(SaveJson.A(d, "position")),
        YawDegrees = SaveJson.F(d, "yaw"),
        Health = SaveJson.F(d, "health", 250f),
    };
}

/// <summary>Scene-wide world state: containers, corpses, structures and explored fog.</summary>
public class WorldData
{
    public List<ContainerData> Containers = new();
    public List<CorpseData> Corpses = new();
    public List<StructureData> Structures = new();
    /// <summary>Run-length encoded revealed fog cells ("x,y;x,y:len;...").</summary>
    public string ExploredFog = "";

    public Dictionary ToDictionary() => new()
    {
        { "containers", SaveJson.Encode(Containers, c => c.ToDictionary()) },
        { "corpses", SaveJson.Encode(Corpses, c => c.ToDictionary()) },
        { "structures", SaveJson.Encode(Structures, s => s.ToDictionary()) },
        { "explored_fog", ExploredFog },
    };

    public static WorldData FromDictionary(Dictionary d) => new()
    {
        Containers = SaveJson.DecodeList(d, "containers", ContainerData.FromDictionary),
        Corpses = SaveJson.DecodeList(d, "corpses", CorpseData.FromDictionary),
        Structures = SaveJson.DecodeList(d, "structures", StructureData.FromDictionary),
        ExploredFog = SaveJson.S(d, "explored_fog"),
    };
}
