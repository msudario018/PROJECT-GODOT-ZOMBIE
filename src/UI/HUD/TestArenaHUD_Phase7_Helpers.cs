using Godot;
using ZombieApocalypse.Entities.Bandits;
using ZombieApocalypse.Entities.NPC;
using ZombieApocalypse.Entities.Survivors;
using ZombieApocalypse.World.WorldEvents;

namespace ZombieApocalypse.UI.HUD;

public partial class TestArenaHUD
{
    private void CachePhase7Nodes()
    {
        _moraleSystem = GetTree().GetFirstNodeInGroup("morale") as MoraleSystem
            ?? GetNodeOrNull<MoraleSystem>("../MoraleSystem");
        _banditSpawner = GetTree().GetFirstNodeInGroup("bandit_spawners") as BanditSpawner
            ?? GetNodeOrNull<BanditSpawner>("../BanditSpawner");
        _airdropEvent = GetNodeOrNull<AirdropEvent>("../AirdropEvent");

        if (_moraleSystem != null)
            OnCampMoraleChanged(_moraleSystem.Morale);
    }

    private void EnsureBanditSpawner()
    {
        if (_banditSpawner != null && GodotObject.IsInstanceValid(_banditSpawner)) return;

        _banditSpawner = GetTree().GetFirstNodeInGroup("bandit_spawners") as BanditSpawner;
        if (_banditSpawner != null) return;

        _banditSpawner = new BanditSpawner { Name = "BanditSpawner" };
        GetParent().AddChild(_banditSpawner);
    }

    private void EnsureAirdropEvent()
    {
        if (_airdropEvent != null && GodotObject.IsInstanceValid(_airdropEvent)) return;

        _airdropEvent = GetNodeOrNull<AirdropEvent>("../AirdropEvent");
        if (_airdropEvent != null) return;

        _airdropEvent = new AirdropEvent { Name = "AirdropEvent" };
        GetParent().AddChild(_airdropEvent);
    }

    private SurvivorBase? FindDemoSurvivor()
    {
        if (_demoSurvivor != null && GodotObject.IsInstanceValid(_demoSurvivor))
            return _demoSurvivor;

        _demoSurvivor = GetTree().GetFirstNodeInGroup("survivors") as SurvivorBase;
        return _demoSurvivor;
    }

    private SurvivorBase? SpawnDemoSurvivor()
    {
        var existing = GetTree().GetNodesInGroup("survivors");
        if (existing.Count >= 4) return null;

        var archetypes = new[] { SurvivorArchetype.CombatMedic, SurvivorArchetype.CombatVeteran, SurvivorArchetype.ScavengerScout };
        var archetype = archetypes[existing.Count % archetypes.Length];

        var survivor = new SurvivorBase
        {
            Name = $"Survivor_{existing.Count + 1}",
            SurvivorName = $"Survivor {existing.Count + 1}",
            Archetype = archetype,
            CampAnchor = new Vector3(-6f, 0f, 10f),
            Position = new Vector3(-6f + existing.Count * 1.5f, 0f, 10f),
        };
        GetParent().AddChild(survivor);

        _demoSurvivor = survivor;
        return survivor;
    }

    private static Label? FindControlsLabel(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Label label && label.Text.Contains("[WASD]"))
                return label;
            var found = FindControlsLabel(child);
            if (found != null) return found;
        }
        return null;
    }

    private void OnCampMoraleChanged(float morale)
    {
        if (_moraleBar != null)
        {
            _moraleBar.MaxValue = 100;
            _moraleBar.Value = morale;
        }
        if (_moraleLabel != null)
            _moraleLabel.Text = $"{Mathf.RoundToInt(morale)}";
    }

    private void OnAirdropSpawned(Vector3 position)
    {
        if (_airdropLabel != null)
        {
            _airdropLabel.Text = $"✈ AIRDROP at ({position.X:F0}, {position.Z:F0}) — contested!";
            _airdropLabel.Visible = true;
        }
        _airdropBannerTimer = 12f;
        _statusLabel.Text = "Military airdrop inbound — raiders will contest it!";
    }

    private void OnBanditRaidIncoming(Node squad)
    {
        _statusLabel.Text = $"⚠ Bandit raid incoming: {squad.Name}!";
    }

    private void OnBanditSquadEliminated(Node squad)
    {
        _statusLabel.Text = $"Squad eliminated: {squad.Name}. Camp morale rises.";
    }
}
