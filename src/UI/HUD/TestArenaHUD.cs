using Godot;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Vision;
using ZombieApocalypse.Entities.Player;
using ZombieApocalypse.Systems.Crafting;
using ZombieApocalypse.World.Environment;
using ZombieApocalypse.World.Power;
using ZombieApocalypse.World.Power.Distribution;
using ZombieApocalypse.World.Power.Generation;

namespace ZombieApocalypse.UI.HUD;

/// <summary>
/// Comprehensive HUD for Phase 1 - 6 systems:
/// Player Health, Hunger, Thirst, Stamina, Inventory & Weight, Crafting,
/// Combat (Weapons & Ammo), Vision (FoW & Flashlight Battery),
/// Defenses & Building, Day/Night Cycle, Power Grid island status,
/// and Zombie/Miasma counts.
/// </summary>
public partial class TestArenaHUD : CanvasLayer
{
    // ── UI Elements ──────────────────────────────────────────────────
    private ProgressBar _healthBar = null!;
    private Label _healthLabel = null!;
    private ProgressBar _hungerBar = null!;
    private Label _hungerLabel = null!;
    private ProgressBar _thirstBar = null!;
    private Label _thirstLabel = null!;
    private ProgressBar _staminaBar = null!;
    private Label _staminaLabel = null!;

    private Label _timeLabel = null!;
    private Label _powerLabel = null!;
    private Label _weaponLabel = null!;
    private Label _ammoLabel = null!;
    private Label _flashlightLabel = null!;
    private Label _inventorySummaryLabel = null!;
    private Label _zombieCountLabel = null!;
    private Label _statusLabel = null!;

    // Group counts are cached at 2 Hz instead of polled every frame.
    private float _countRefresh;
    private int _cachedZombieCount;
    private int _cachedMiasmaCount;

    // ── Player Component References ──────────────────────────────────
    private HealthComponent? _playerHealth;
    private PlayerStats? _playerStats;
    private InventoryComponent? _playerInventory;
    private CraftingSystem? _craftingSystem;
    private PlayerCombat? _playerCombat;
    private FieldOfView? _playerFov;

    public override void _Ready()
    {
        BuildUI();
        BuildPauseMenu();

        var player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
        if (player != null)
        {
            _playerHealth = player.GetNodeOrNull<HealthComponent>("HealthComponent");
            if (_playerHealth != null)
            {
                _playerHealth.HealthChanged += OnPlayerHealthChanged;
                UpdateHealthDisplay(_playerHealth.CurrentHealth, _playerHealth.MaxHealth);
            }

            _playerStats = player.GetNodeOrNull<PlayerStats>("PlayerStats");
            _playerInventory = player.GetNodeOrNull<InventoryComponent>("InventoryComponent");
            _craftingSystem = player.GetNodeOrNull<CraftingSystem>("CraftingSystem");

            _playerCombat = player.GetNodeOrNull<PlayerCombat>("PlayerCombat");
            if (_playerCombat != null)
            {
                _playerCombat.WeaponChanged += OnWeaponChanged;
                _playerCombat.AmmoChanged += OnAmmoChanged;
                UpdateWeaponDisplay(_playerCombat.CurrentWeapon.WeaponName, _playerCombat.CurrentMag, _playerCombat.CurrentReserve);
            }

            _playerFov = player.GetNodeOrNull<FieldOfView>("FieldOfView");
        }

        if (EventBus.Instance != null)
        {
            EventBus.Instance.OnSoundEmitted += OnSoundEmitted;
            EventBus.Instance.OnEntityKilled += OnEntityKilled;
        }
    }

    public override void _Process(double delta)
    {
        _countRefresh -= (float)delta;
        if (_countRefresh <= 0f)
        {
            _countRefresh = 0.5f;
            _cachedZombieCount = GetTree().GetNodesInGroup("zombies").Count;
            _cachedMiasmaCount = CorpseManager.Instance?.GetActiveMiasmaCount() ?? 0;
        }

        // 1. Zombie & Miasma Status
        _zombieCountLabel.Text = $"Active Zombies: {_cachedZombieCount} | Toxic Miasma Zones: {_cachedMiasmaCount}";
        if (_cachedMiasmaCount > 0)
            _zombieCountLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.45f, 0.2f));
        else
            _zombieCountLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.4f, 0.4f));

        // 2. Day/Night Clock
        if (DayNightCycle.Instance != null)
        {
            _timeLabel.Text = $"Time: {DayNightCycle.Instance.GetFormattedTime()}";
            _timeLabel.AddThemeColorOverride("font_color", DayNightCycle.Instance.IsNight 
                ? new Color(0.5f, 0.7f, 1.0f) 
                : new Color(1.0f, 0.9f, 0.4f));
        }

        // 3. Survival Needs (Hunger, Thirst, Stamina)
        if (_playerStats != null)
        {
            _hungerBar.Value = _playerStats.Hunger;
            _hungerLabel.Text = $"{Mathf.RoundToInt(_playerStats.Hunger)}%";

            _thirstBar.Value = _playerStats.Thirst;
            _thirstLabel.Text = $"{Mathf.RoundToInt(_playerStats.Thirst)}%";

            _staminaBar.Value = _playerStats.Stamina;
            _staminaLabel.Text = $"{Mathf.RoundToInt(_playerStats.Stamina)}%";
        }

        // 4. Inventory Quick Summary
        if (_playerInventory != null)
        {
            int cloth = _playerInventory.CountOf("cloth");
            int scrap = _playerInventory.CountOf("scrap");
            int wood = _playerInventory.CountOf("wood_plank");
            int bandage = _playerInventory.CountOf("bandage");
            int food = _playerInventory.CountOf("food_can");
            int water = _playerInventory.CountOf("water_bottle");

            string encumberedText = _playerInventory.IsEncumbered ? " [ENCUMBERED! -25% Speed]" : "";
            _inventorySummaryLabel.Text = $"Bag ({_playerInventory.CurrentWeightKg:F1}/{_playerInventory.MaxWeightKg:F0}kg{encumberedText}): " +
                $"Bandages: {bandage} | Food: {food} | Water: {water} | Cloth: {cloth} | Scrap: {scrap} | Wood: {wood}";

            _inventorySummaryLabel.AddThemeColorOverride("font_color", _playerInventory.IsEncumbered 
                ? new Color(1.0f, 0.35f, 0.35f) 
                : new Color(0.8f, 0.85f, 0.9f));
        }

        // 5. Flashlight Status & Battery
        if (_playerFov != null)
        {
            string flStatus = _playerFov.IsFlashlightActive ? "ACTIVE" : "OFF";
            int batt = Mathf.RoundToInt(_playerFov.CurrentBattery);
            _flashlightLabel.Text = $"Flashlight: {flStatus} ({batt}%)";
            _flashlightLabel.AddThemeColorOverride("font_color", _playerFov.IsFlashlightActive 
                ? (batt < 20 ? new Color(1f, 0.4f, 0.4f) : new Color(1f, 0.95f, 0.5f))
                : new Color(0.7f, 0.7f, 0.7f));
        }

        // 6. Reloading indicator
        if (_playerCombat != null && _playerCombat.IsReloading)
        {
            int pct = Mathf.RoundToInt(_playerCombat.ReloadProgress * 100f);
            _ammoLabel.Text = $"Reloading... ({pct}%)";
            _ammoLabel.AddThemeColorOverride("font_color", new Color(1f, 0.7f, 0.2f));
        }

        // 7. Power Grid: generation, load, island count
        var grid = PowerGrid.Instance;
        if (grid != null)
        {
            _powerLabel.Text = grid.GetSummaryLine();

            Color powerColor = grid.IslandCount > 1
                ? new Color(1.0f, 0.55f, 0.2f)                        // split grid
                : grid.TotalShedLoads > 0
                    ? new Color(1.0f, 0.35f, 0.35f)                   // brownout / shedding
                    : grid.TotalGenerationWatts > 0f
                        ? new Color(0.5f, 1.0f, 0.6f)                 // healthy
                        : new Color(0.6f, 0.6f, 0.6f);                // no source
            _powerLabel.AddThemeColorOverride("font_color", powerColor);
        }

        UpdatePhase7UI(delta);
        UpdatePauseMenu(delta);
    }

    partial void BuildPhase7UI(VBoxContainer vbox);
    partial void UpdatePhase7UI(double delta);
    partial void HandlePhase7Key(InputEventKey key);

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;

        // The pause menu swallows input while it is open (Esc closes it).
        if (ForwardPauseMenuKey(key)) return;

        // F1: Toggle Fog of War
        if (key.Keycode == Key.F1)
        {
            var renderer = FogOfWarRenderer.Instance;
            if (renderer != null)
            {
                renderer.ToggleFog();
                _statusLabel.Text = $"Fog of War: {(renderer.FogEnabled ? "ENABLED" : "DISABLED")}";
            }
        }

        // Phase 5 Quick Survival Consumables:
        // [7] Eat Food
        if (key.Keycode == Key.Key7)
        {
            if (_playerInventory != null && _playerInventory.TryUseItem("food_can", _playerStats, _playerHealth))
                _statusLabel.Text = "Ate Canned Food (+35 Hunger)";
            else
                _statusLabel.Text = "No Canned Food in inventory!";
        }

        // [8] Drink Water
        if (key.Keycode == Key.Key8)
        {
            if (_playerInventory != null && _playerInventory.TryUseItem("water_bottle", _playerStats, _playerHealth))
                _statusLabel.Text = "Drank Water Bottle (+40 Thirst)";
            else
                _statusLabel.Text = "No Water Bottle in inventory!";
        }

        // [9] Use Bandage
        if (key.Keycode == Key.Key9)
        {
            if (_playerInventory != null && _playerInventory.TryUseItem("bandage", _playerStats, _playerHealth))
                _statusLabel.Text = "Applied Bandage (+20 HP)";
            else
                _statusLabel.Text = "No Bandage in inventory!";
        }

        // Phase 5 Quick Crafting:
        // [C] Craft Bandage (2x Cloth)
        if (key.Keycode == Key.C)
        {
            if (_craftingSystem != null)
            {
                if (_craftingSystem.TryCraft("Bandage"))
                    _statusLabel.Text = "Crafted: 2× Bandages!";
                else
                    _statusLabel.Text = "Craft Failed: Need 2× Cloth!";
            }
        }

        // [V] Craft Improvised First Aid Kit
        if (key.Keycode == Key.V)
        {
            if (_craftingSystem != null)
            {
                if (_craftingSystem.TryCraft("Improvised First Aid Kit"))
                    _statusLabel.Text = "Crafted: First Aid Kit!";
                else
                    _statusLabel.Text = "Craft Failed: Need 3× Cloth + 1× Rope!";
            }
        }

        // [B] Craft Reinforced Ammo (4x Scrap -> 2x Ammo)
        if (key.Keycode == Key.B)
        {
            if (_craftingSystem != null)
            {
                if (_craftingSystem.TryCraft("Reinforce Ammo Box (9mm)"))
                    _statusLabel.Text = "Crafted: 2× 9mm Ammo Packs!";
                else
                    _statusLabel.Text = "Craft Failed: Need 4× Scrap Metal!";
            }
        }

        // Phase 6 Power Grid debug controls:
        // [X] Sever / repair the nearest power cable (demonstrates islanding)
        if (key.Keycode == Key.X)
        {
            var cable = FindNearestCable();
            if (cable != null)
            {
                cable.ToggleSevered();
                _statusLabel.Text = cable.IsSevered
                    ? $"Cable SEVERED ({cable.Name}) — perimeter island lost power!"
                    : $"Cable repaired ({cable.Name}) — grid reconnected.";
            }
            else
            {
                _statusLabel.Text = "No power cable in range.";
            }
        }

        // [G] Start / stop the combustion generator
        if (key.Keycode == Key.G)
        {
            var generator = GetTree().GetFirstNodeInGroup("power_nodes") as CombustionGenerator;
            if (generator != null)
            {
                generator.ToggleRunning();
                _statusLabel.Text = $"Generator: {(generator.Running ? "STARTED" : "STOPPED")}";
            }
        }

        HandlePhase7Key(key);
    }

    /// <summary>Closest power cable to the player (used by the [X] debug sever key).</summary>
    private WiringSegment? FindNearestCable()
    {
        var player = GetTree().GetFirstNodeInGroup("player") as Node3D;
        if (player == null) return null;

        WiringSegment? best = null;
        float bestDistSq = 8.0f * 8.0f;   // search radius

        foreach (var node in GetTree().GetNodesInGroup("power_wires"))
        {
            if (node is not WiringSegment cable || !GodotObject.IsInstanceValid(cable)) continue;

            float distSq = player.GlobalPosition.DistanceSquaredTo(cable.GlobalPosition);
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = cable;
            }
        }

        return best;
    }

    private void BuildUI()
    {
        var rootPanel = new PanelContainer();
        rootPanel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        rootPanel.Position = new Vector2(20, 20);
        rootPanel.FocusMode = Control.FocusModeEnum.None;
        rootPanel.MouseFilter = Control.MouseFilterEnum.Ignore;

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.FocusMode = Control.FocusModeEnum.None;
        margin.MouseFilter = Control.MouseFilterEnum.Ignore;
        rootPanel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 5);
        vbox.FocusMode = Control.FocusModeEnum.None;
        vbox.MouseFilter = Control.MouseFilterEnum.Ignore;
        margin.AddChild(vbox);

        // Header
        var title = new Label();
        title.Text = "PROJECT ZOMBIE - PHASE 5 SURVIVAL & LOOT";
        title.AddThemeFontSizeOverride("font_size", 15);
        title.AddThemeColorOverride("font_color", new Color(0.95f, 0.8f, 0.35f));
        vbox.AddChild(title);

        // Time / Day-Night
        _timeLabel = new Label();
        _timeLabel.Text = "Time: 12:00 - Day";
        _timeLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.9f, 0.4f));
        vbox.AddChild(_timeLabel);

        // ── 4 Survival Vitals Bars (HP, Hunger, Thirst, Stamina) ──────
        var grid = new GridContainer();
        grid.Columns = 3;
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 4);
        grid.FocusMode = Control.FocusModeEnum.None;
        grid.MouseFilter = Control.MouseFilterEnum.Ignore;
        vbox.AddChild(grid);

        // HP
        var hpLbl = new Label { Text = "HP:" };
        grid.AddChild(hpLbl);
        _healthBar = CreateBar(new Color(0.85f, 0.25f, 0.25f));
        grid.AddChild(_healthBar);
        _healthLabel = new Label { Text = "100 / 100" };
        grid.AddChild(_healthLabel);

        // Hunger
        var hungerLbl = new Label { Text = "Food:" };
        grid.AddChild(hungerLbl);
        _hungerBar = CreateBar(new Color(0.95f, 0.65f, 0.2f));
        grid.AddChild(_hungerBar);
        _hungerLabel = new Label { Text = "80%" };
        grid.AddChild(_hungerLabel);

        // Thirst
        var thirstLbl = new Label { Text = "Water:" };
        grid.AddChild(thirstLbl);
        _thirstBar = CreateBar(new Color(0.25f, 0.65f, 0.95f));
        grid.AddChild(_thirstBar);
        _thirstLabel = new Label { Text = "80%" };
        grid.AddChild(_thirstLabel);

        // Stamina
        var stamLbl = new Label { Text = "Stam:" };
        grid.AddChild(stamLbl);
        _staminaBar = CreateBar(new Color(0.25f, 0.85f, 0.45f));
        grid.AddChild(_staminaBar);
        _staminaLabel = new Label { Text = "100%" };
        grid.AddChild(_staminaLabel);

        // Combat Info (Weapon & Ammo)
        var combatBox = new HBoxContainer();
        combatBox.AddThemeConstantOverride("separation", 12);
        combatBox.FocusMode = Control.FocusModeEnum.None;
        combatBox.MouseFilter = Control.MouseFilterEnum.Ignore;
        vbox.AddChild(combatBox);

        _weaponLabel = new Label();
        _weaponLabel.Text = "Weapon: Rusty Crowbar";
        _weaponLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.9f, 0.6f));
        combatBox.AddChild(_weaponLabel);

        _ammoLabel = new Label();
        _ammoLabel.Text = "Ammo: --";
        _ammoLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.4f));
        combatBox.AddChild(_ammoLabel);

        // Flashlight Battery Info
        _flashlightLabel = new Label();
        _flashlightLabel.Text = "Flashlight: OFF (100%)";
        _flashlightLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        vbox.AddChild(_flashlightLabel);

        // Inventory Quick Summary
        _inventorySummaryLabel = new Label();
        _inventorySummaryLabel.Text = "Bag: Loading...";
        _inventorySummaryLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.85f, 0.9f));
        vbox.AddChild(_inventorySummaryLabel);

        // Power Grid (generation / load / island status)
        _powerLabel = new Label();
        _powerLabel.Text = "Power: offline";
        _powerLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.9f, 1.0f));
        vbox.AddChild(_powerLabel);

        BuildPhase7UI(vbox);

        // Zombie & Miasma count
        _zombieCountLabel = new Label();
        _zombieCountLabel.Text = "Active Zombies: 0 | Toxic Miasma Zones: 0";
        _zombieCountLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.4f, 0.4f));
        vbox.AddChild(_zombieCountLabel);

        // Status Feedback
        _statusLabel = new Label();
        _statusLabel.Text = "System Ready. Explore and survive.";
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.8f, 1.0f));
        vbox.AddChild(_statusLabel);

        // Controls help
        var controlsLabel = new Label();
        controlsLabel.Text = 
            "[WASD] Move | [Shift] Sprint (Consumes Stamina)\n" +
            "[E] Interact (Search Loot Crate, Bury/Burn Corpse, Toggle Door)\n" +
            "[4] Crowbar | [5] M9 Pistol | [6] Shotgun | [Q] Cycle Weapon\n" +
            "[LMB] Attack/Fire | [R] Reload | [F] Flashlight | [F1] Fog\n" +
            "[7] Eat Can | [8] Drink Water | [9] Use Bandage\n" +
            "[C] Craft Bandage | [V] Craft First Aid | [B] Craft Ammo\n" +
            "[X] Sever/Repair Cable | [G] Generator On/Off\n" +
            "[1] Wood Fence | [2] Chain Link | [3] Door | [RMB] Cancel Build";
        controlsLabel.AddThemeFontSizeOverride("font_size", 11);
        controlsLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f));
        vbox.AddChild(controlsLabel);

        AddChild(rootPanel);
    }

    private static ProgressBar CreateBar(Color fillColor)
    {
        var bar = new ProgressBar
        {
            CustomMinimumSize = new Vector2(140, 16),
            MinValue = 0,
            MaxValue = 100,
            Value = 100,
            ShowPercentage = false,
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        return bar;
    }

    private void UpdateHealthDisplay(float current, float max)
    {
        _healthBar.MaxValue = max;
        _healthBar.Value = current;
        _healthLabel.Text = $"{Mathf.RoundToInt(current)} / {Mathf.RoundToInt(max)}";
    }

    private void UpdateWeaponDisplay(string weaponName, int mag, int reserve)
    {
        _weaponLabel.Text = $"Weapon: {weaponName}";
        if (_playerCombat != null && _playerCombat.CurrentWeapon.IsRanged)
        {
            _ammoLabel.Text = $"Ammo: {mag} / {reserve}";
            _ammoLabel.AddThemeColorOverride("font_color", mag <= 2 ? new Color(1f, 0.3f, 0.3f) : new Color(1.0f, 0.85f, 0.4f));
        }
        else
        {
            _ammoLabel.Text = "Ammo: Melee (Infinite)";
            _ammoLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        }
    }

    private void OnPlayerHealthChanged(float current, float max)
    {
        UpdateHealthDisplay(current, max);
    }

    private void OnWeaponChanged(string weaponName)
    {
        if (_playerCombat != null)
            UpdateWeaponDisplay(weaponName, _playerCombat.CurrentMag, _playerCombat.CurrentReserve);
    }

    private void OnAmmoChanged(int currentMag, int reserve)
    {
        if (_playerCombat != null)
            UpdateWeaponDisplay(_playerCombat.CurrentWeapon.WeaponName, currentMag, reserve);
    }

    private void OnSoundEmitted(Vector3 pos, float radius, ZombieApocalypse.Core.Data.AudioSourceType type)
    {
        _statusLabel.Text = $"Last Sound: {type} (Radius: {radius:F0}m)";
    }

    private void OnEntityKilled(Node entity)
    {
        _statusLabel.Text = $"Killed: {entity.Name} (Fallen Corpse Created)";
    }
}
