using Godot;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Entities.Player;

namespace ZombieApocalypse.UI.HUD;

/// <summary>
/// Debug HUD overlay for testing Phase 1 & 2 systems:
/// Player Health, Movement, Acoustic Footsteps, Melee Combat, and Zombie counts.
/// </summary>
public partial class TestArenaHUD : CanvasLayer
{
    private ProgressBar _healthBar = null!;
    private Label _healthLabel = null!;
    private Label _zombieCountLabel = null!;
    private Label _statusLabel = null!;
    private HealthComponent? _playerHealth;

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
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.F1)
        {
            var renderer = ZombieApocalypse.Core.Vision.FogOfWarRenderer.Instance;
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

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        rootPanel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        margin.AddChild(vbox);

        // Title
        var title = new Label();
        title.Text = "PROJECT ZOMBIE - PHASE 2 TEST ARENA";
        title.AddThemeFontSizeOverride("font_size", 16);
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.75f, 0.3f));
        vbox.AddChild(title);

        // Health Bar container
        var hpBox = new HBoxContainer();
        hpBox.AddThemeConstantOverride("separation", 10);
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
        hpBox.AddChild(_healthBar);

        _healthLabel = new Label();
        _healthLabel.Text = "100 / 100";
        hpBox.AddChild(_healthLabel);

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
        controlsLabel.Text = "Controls:\n[WASD] Move (Iso)\n[Shift] Sprint (Loud Noise!)\n[LMB / Space] Melee Attack\n[E] Interact (Open/Close Door)\n[1] Build Scrap Fence | [2] Chain Link | [3] Door\n[R] Rotate Building | [RMB/Esc] Cancel\n[F1] Toggle Fog of War | [F] Flashlight";
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

    private void OnPlayerHealthChanged(float current, float max)
    {
        UpdateHealthDisplay(current, max);
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
