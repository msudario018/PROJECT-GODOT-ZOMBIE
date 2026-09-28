using Godot;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Vision;
using ZombieApocalypse.Entities.Player;

namespace ZombieApocalypse.UI.HUD;

/// <summary>
/// Debug HUD overlay for testing Phase 1 - 4 systems:
/// Player Health, Movement, Combat (Weapons & Ammo), Vision (FoW & Flashlight Battery),
/// Defenses & Building, and Zombie counts.
/// </summary>
public partial class TestArenaHUD : CanvasLayer
{
    private ProgressBar _healthBar = null!;
    private Label _healthLabel = null!;
    private Label _weaponLabel = null!;
    private Label _ammoLabel = null!;
    private Label _flashlightLabel = null!;
    private Label _zombieCountLabel = null!;
    private Label _statusLabel = null!;

    private HealthComponent? _playerHealth;
    private PlayerCombat? _playerCombat;
    private FieldOfView? _playerFov;

    public override void _Ready()
    {
        BuildUI();

        var player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
        if (player != null)
        {
            _playerHealth = player.GetNodeOrNull<HealthComponent>("HealthComponent");
            if (_playerHealth != null)
            {
                _playerHealth.HealthChanged += OnPlayerHealthChanged;
                UpdateHealthDisplay(_playerHealth.CurrentHealth, _playerHealth.MaxHealth);
            }

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
        int zombieCount = GetTree().GetNodesInGroup("zombies").Count;
        _zombieCountLabel.Text = $"Active Zombies: {zombieCount}";

        // Update Flashlight Status & Battery
        if (_playerFov != null)
        {
            string flStatus = _playerFov.IsFlashlightActive ? "ACTIVE" : "OFF";
            int batt = Mathf.RoundToInt(_playerFov.CurrentBattery);
            _flashlightLabel.Text = $"Flashlight: {flStatus} ({batt}%)";
            _flashlightLabel.AddThemeColorOverride("font_color", _playerFov.IsFlashlightActive 
                ? (batt < 20 ? new Color(1f, 0.4f, 0.4f) : new Color(1f, 0.95f, 0.5f))
                : new Color(0.7f, 0.7f, 0.7f));
        }

        // Update Reloading indicator
        if (_playerCombat != null && _playerCombat.IsReloading)
        {
            int pct = Mathf.RoundToInt(_playerCombat.ReloadProgress * 100f);
            _ammoLabel.Text = $"Reloading... ({pct}%)";
            _ammoLabel.AddThemeColorOverride("font_color", new Color(1f, 0.7f, 0.2f));
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.F1)
        {
            var renderer = FogOfWarRenderer.Instance;
            if (renderer != null)
            {
                renderer.ToggleFog();
                _statusLabel.Text = $"Fog of War: {(renderer.FogEnabled ? "ENABLED" : "DISABLED")}";
            }
        }
    }

    private void BuildUI()
    {
        var rootPanel = new PanelContainer();
        rootPanel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        rootPanel.Position = new Vector2(20, 20);
        rootPanel.FocusMode = Control.FocusModeEnum.None;
        rootPanel.MouseFilter = Control.MouseFilterEnum.Ignore;

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.FocusMode = Control.FocusModeEnum.None;
        margin.MouseFilter = Control.MouseFilterEnum.Ignore;
        rootPanel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        vbox.FocusMode = Control.FocusModeEnum.None;
        vbox.MouseFilter = Control.MouseFilterEnum.Ignore;
        margin.AddChild(vbox);

        // Title
        var title = new Label();
        title.Text = "PROJECT ZOMBIE - PHASE 4 COMBAT & VISION";
        title.AddThemeFontSizeOverride("font_size", 16);
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.75f, 0.3f));
        vbox.AddChild(title);

        // Health Bar container
        var hpBox = new HBoxContainer();
        hpBox.AddThemeConstantOverride("separation", 10);
        hpBox.FocusMode = Control.FocusModeEnum.None;
        hpBox.MouseFilter = Control.MouseFilterEnum.Ignore;
        vbox.AddChild(hpBox);

        var hpTitle = new Label();
        hpTitle.Text = "HP:";
        hpBox.AddChild(hpTitle);

        _healthBar = new ProgressBar();
        _healthBar.CustomMinimumSize = new Vector2(180, 20);
        _healthBar.MinValue = 0;
        _healthBar.MaxValue = 100;
        _healthBar.Value = 100;
        _healthBar.ShowPercentage = false;
        _healthBar.FocusMode = Control.FocusModeEnum.None;
        _healthBar.MouseFilter = Control.MouseFilterEnum.Ignore;
        hpBox.AddChild(_healthBar);

        _healthLabel = new Label();
        _healthLabel.Text = "100 / 100";
        hpBox.AddChild(_healthLabel);

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

        // Zombie count
        _zombieCountLabel = new Label();
        _zombieCountLabel.Text = "Active Zombies: 0";
        _zombieCountLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.4f, 0.4f));
        vbox.AddChild(_zombieCountLabel);

        // Acoustic / Status log
        _statusLabel = new Label();
        _statusLabel.Text = "Sound: None";
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.8f, 1.0f));
        vbox.AddChild(_statusLabel);

        // Controls help
        var controlsLabel = new Label();
        controlsLabel.Text = "Controls:\n" +
            "[WASD] Move (Iso) | [Shift] Sprint (Loud Noise!)\n" +
            "[4] Crowbar | [5] M9 Pistol | [6] Shotgun | [Q] Cycle Weapon\n" +
            "[LMB] Attack / Fire | [R] Reload Weapon\n" +
            "[F] Flashlight Cone | [F1] Toggle Fog of War\n" +
            "[E] Interact (Open/Close Door)\n" +
            "[1] Wood Fence | [2] Chain Link | [3] Door | [RMB] Cancel Build";
        controlsLabel.AddThemeFontSizeOverride("font_size", 12);
        controlsLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f));
        vbox.AddChild(controlsLabel);

        AddChild(rootPanel);
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
        _statusLabel.Text = $"Killed: {entity.Name}";
    }
}
