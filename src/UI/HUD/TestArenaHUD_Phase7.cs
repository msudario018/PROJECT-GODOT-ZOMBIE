using Godot;
using ZombieApocalypse.Entities.Bandits;
using ZombieApocalypse.Entities.NPC;
using ZombieApocalypse.Entities.Survivors;
using ZombieApocalypse.World.WorldEvents;

namespace ZombieApocalypse.UI.HUD;

/// <summary>
/// Phase 7 HUD additions: morale bar, survivor/squad counters, airdrop banner,
/// and the debug keys H (raid), J (airdrop), K (cycle survivor task).
/// Kept as a partial class so Phase 1-6 HUD code is untouched.
/// </summary>
public partial class TestArenaHUD
{
    private ProgressBar? _moraleBar;
    private Label? _moraleLabel;
    private Label? _npcLabel;
    private Label? _airdropLabel;
    private Label? _deathLabel;
    private Label? _weatherLabel;

    private MoraleSystem? _moraleSystem;
    private SurvivorBase? _demoSurvivor;
    private BanditSpawner? _banditSpawner;
    private AirdropEvent? _airdropEvent;
    private float _airdropBannerTimer;

    partial void BuildPhase7UI(VBoxContainer vbox)
    {
        var moraleRow = new HBoxContainer();
        moraleRow.AddThemeConstantOverride("separation", 6);
        vbox.AddChild(moraleRow);

        var moraleTitle = new Label { Text = "Camp Morale:" };
        moraleRow.AddChild(moraleTitle);

        _moraleBar = CreateBar(new Color(1.0f, 0.85f, 0.3f));
        moraleRow.AddChild(_moraleBar);

        _moraleLabel = new Label { Text = "--" };
        moraleRow.AddChild(_moraleLabel);

        _npcLabel = new Label { Text = "Survivors: 0 | Bandits: 0" };
        _npcLabel.AddThemeColorOverride("font_color", new Color(0.65f, 0.85f, 1.0f));
        vbox.AddChild(_npcLabel);

        _weatherLabel = new Label { Text = "" };
        _weatherLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.85f, 0.75f));
        vbox.AddChild(_weatherLabel);

        _airdropLabel = new Label { Text = "" };
        _airdropLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.7f, 0.25f));
        _airdropLabel.Visible = false;
        vbox.AddChild(_airdropLabel);

        // Full-screen death banner (Phase 1-6 fix: player death → GameOver)
        _deathLabel = new Label
        {
            Text = "YOU DIED\nPress [P] to restart",
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _deathLabel.AddThemeFontSizeOverride("font_size", 42);
        _deathLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.15f, 0.15f));
        _deathLabel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _deathLabel.OffsetTop = 220;
        AddChild(_deathLabel);

        CachePhase7Nodes();

        if (Core.Autoloads.EventBus.Instance != null)
        {
            Core.Autoloads.EventBus.Instance.OnCampMoraleChanged += OnCampMoraleChanged;
            Core.Autoloads.EventBus.Instance.OnAirdropSpawned += OnAirdropSpawned;
            Core.Autoloads.EventBus.Instance.OnBanditRaidIncoming += OnBanditRaidIncoming;
            Core.Autoloads.EventBus.Instance.OnBanditSquadEliminated += OnBanditSquadEliminated;
        }

        var controls = GetNodeOrNull<Label>("RootPanel/Margin/VBox/ControlsLabel");
        if (controls == null)
        {
            foreach (var child in GetChildren())
            {
                var found = FindControlsLabel(child);
                if (found != null) { controls = found; break; }
            }
        }
        if (controls != null)
            controls.Text += "\n[H] Spawn Raid | [J] Airdrop | [K] Cycle Survivor Task | [L] Spawn Survivor";

        if (Core.Persistence.SaveManager.Instance != null)
        {
            Core.Persistence.SaveManager.Instance.GameSaved += OnGameSaved;
            Core.Persistence.SaveManager.Instance.GameLoaded += OnGameLoaded;
            Core.Persistence.SaveManager.Instance.SaveFailed += OnSaveFailed;
        }
    }

    private void OnGameSaved(string path) => _statusLabel.Text = $"Game saved → {path.GetFile()}";
    private void OnGameLoaded(string path) => _statusLabel.Text = $"Game loaded ← {path.GetFile()}";
    private void OnSaveFailed(string reason) => _statusLabel.Text = $"Save failed: {reason}";

    partial void UpdatePhase7UI(double delta)
    {
        int survivorCount = GetTree().GetNodesInGroup("survivors").Count;
        int banditCount = GetTree().GetNodesInGroup("bandits").Count;
        if (_npcLabel != null)
            _npcLabel.Text = $"Survivors: {survivorCount} | Bandits: {banditCount}";

        if (_weatherLabel != null)
        {
            var weather = World.Environment.WeatherSystem.Instance;
            _weatherLabel.Text = weather != null
                ? $"Weather: {weather.CurrentWeather} ({weather.CurrentSeason} d{weather.DayOfSeason})"
                : "";
        }

        if (_airdropBannerTimer > 0f)
        {
            _airdropBannerTimer -= (float)delta;
            if (_airdropBannerTimer <= 0f && _airdropLabel != null)
                _airdropLabel.Visible = false;
        }

        if (_deathLabel != null)
            _deathLabel.Visible =
                Core.Autoloads.GameManager.Instance?.CurrentState == Core.Data.GameState.GameOver;
    }

    partial void HandlePhase7Key(InputEventKey key)
    {
        if (key.Keycode == Key.H)
        {
            EnsureBanditSpawner();
            if (_banditSpawner != null)
            {
                var squad = _banditSpawner.SpawnSquad(BanditTier.Scavenger, 3);
                _statusLabel.Text = $"Raid incoming: {squad.SquadName}!";
            }
        }
        else if (key.Keycode == Key.J)
        {
            EnsureAirdropEvent();
            _airdropEvent?.TriggerDrop();
            _statusLabel.Text = "Airdrop requested — watch the skies.";
        }
        else if (key.Keycode == Key.K)
        {
            var survivor = FindDemoSurvivor();
            var ai = survivor?.GetNodeOrNull<SurvivorAI>("SurvivorAI");
            if (survivor != null && ai != null)
            {
                var next = (SurvivorTask)(((int)survivor.TaskMode + 1) % 6);
                ai.SetMode(next);
                _statusLabel.Text = $"{survivor.SurvivorName} task mode → {next}";
            }
            else
            {
                _statusLabel.Text = "No survivor in camp yet — press [L] first.";
            }
        }
        else if (key.Keycode == Key.L)
        {
            var survivor = SpawnDemoSurvivor();
            _statusLabel.Text = survivor != null
                ? $"{survivor.SurvivorName} joined the camp!"
                : "Survivor cap reached.";
        }
    }
}
