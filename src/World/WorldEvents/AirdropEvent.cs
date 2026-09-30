using Godot;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Entities.Bandits;
using ZombieApocalypse.Entities.NPC;

namespace ZombieApocalypse.World.WorldEvents;

/// <summary>
/// Military supply-drop event. Spawns a lootable airdrop cache that bandits
/// actively contest, then emits <see cref="EventBus.OnAirdropSpawned"/>.
/// </summary>
public partial class AirdropEvent : Node3D
{
    [ExportGroup("Airdrop")]
    [Export] public bool AutoSpawnOnReady = false;
    [Export] public Vector3 DropPosition = new(10f, 0f, -10f);
    [Export] public float BanditAttractRadius = 40.0f;

    [ExportGroup("Cache Loot")]
    [Export] public int MedKits = 2;

    public Systems.Loot.LootContainer? Cache { get; private set; }
    public bool HasDropped { get; private set; }

    public override void _Ready()
    {
        AddToGroup("airdrop_events");
        if (AutoSpawnOnReady)
            TriggerDrop();
    }

    /// <summary>
    /// Re-create the event's world state after loading a save: either spawn the
    /// cache (and its contesting squad) again, or clear a cache that never
    /// dropped in the saved run.
    /// </summary>
    public void RestoreState(bool hasDropped, Vector3 dropPosition)
    {
        if (hasDropped)
        {
            DropPosition = dropPosition;
            if (!HasDropped)
                TriggerDrop();
        }
        else if (HasDropped)
        {
            Cache?.QueueFree();
            Cache = null;
            HasDropped = false;
        }
    }

    /// <summary>Drop the cache, attract a squad, and announce the event.</summary>
    public void TriggerDrop()
    {
        if (HasDropped) return;
        HasDropped = true;

        Cache = new Systems.Loot.LootContainer
        {
            Name = "AirdropCache",
            ContainerLabel = "Military Airdrop",
            ContainerType = Systems.Loot.LootContainer.ContainerArchetype.GeneralJunk,
            Position = DropPosition,
            MinRolls = 5,
            MaxRolls = 5,
            SearchTime = 2.5f,
            IsAirdropCache = true,
        };
        AddChild(Cache);
        Cache.SetAirdropLoot(MedKits, Ammo9mmPacks, FoodCans, ClothBundles);

        var squad = new BanditSquad
        {
            Name = "AirdropContestSquad",
            SquadName = "Airdrop Raiders",
            Tier = BanditTier.Scavenger,
            SquadSize = 2,
            SpawnOrigin = DropPosition + new Vector3(8f, 0f, 8f),
        };
        AddChild(squad);
        squad.Deploy();

        EventBus.Instance?.EmitAirdropSpawned(DropPosition);
        GD.Print($"[AirdropEvent] Supply drop landed at {DropPosition}. Raiders contesting the cache!");
    }

    [Export] public int Ammo9mmPacks = 3;
    [Export] public int FoodCans = 3;
    [Export] public int ClothBundles = 2;
}
