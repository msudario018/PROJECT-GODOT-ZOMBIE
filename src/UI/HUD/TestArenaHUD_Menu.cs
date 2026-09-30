namespace ZombieApocalypse.UI.HUD;

using Godot;
using System;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Persistence;

/// <summary>
/// Pause / settings menu (Stage 5).
///
/// Binds the service APIs that Stages 2-4 added but left unreachable from the
/// UI: audio mix, difficulty presets and save/load/delete. Visibility is driven
/// by <see cref="Core.Autoloads.GameManager.IsPaused"/>, so [Esc] keeps working
/// exactly as before and the menu can never disagree with the game state.
/// </summary>
public partial class TestArenaHUD
{
    private Control? _menuRoot;
    private Label? _menuDifficultyValue;
    private Label? _menuSaveInfo;
    private Button? _menuLoadButton;
    private Button? _menuDeleteButton;
    private bool _menuVisible;

    /// <summary>True while the pause menu is open.</summary>
    internal bool PauseMenuVisible => _menuVisible;

    internal void SetPauseMenuVisible(bool visible)
    {
        if (_menuRoot == null) return;

        _menuVisible = visible;
        _menuRoot.Visible = visible;

        if (visible)
        {
            RefreshMenuValues();
            _menuLoadButton?.GrabFocus();
        }
    }

    /// <summary>
    /// Consume gameplay keys while the menu is open. [Esc] is passed through so
    /// GameManager keeps owning pause toggling.
    /// </summary>
    internal bool ForwardPauseMenuKey(InputEventKey key)
    {
        if (!_menuVisible) return false;
        return key.Keycode != Key.Escape;
    }

    /// <summary>Called from the HUD frame loop to mirror the pause state.</summary>
    private void UpdatePauseMenu(double delta)
    {
        if (_menuRoot == null) return;

        bool shouldBeVisible = GameManager.Instance?.IsPaused ?? false;
        if (shouldBeVisible == _menuVisible) return;

        SetPauseMenuVisible(shouldBeVisible);
    }

    private void BuildPauseMenu()
    {
        if (_menuRoot != null) return;

        _menuRoot = new PanelContainer
        {
            Name = "PauseMenu",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _menuRoot.SetAnchorsPreset(Control.LayoutPreset.Center);
        _menuRoot.CustomMinimumSize = new Vector2(420, 0);
        _menuRoot.Position = new Vector2(-210, -220);

        _menuRoot.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.09f, 0.11f, 0.95f),
            BorderColor = new Color(0.35f, 0.55f, 0.7f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 14,
            ContentMarginBottom = 14,
        });

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        _menuRoot.AddChild(vbox);

        var header = new Label { Text = "— PAUSED —", HorizontalAlignment = HorizontalAlignment.Center };
        header.AddThemeFontSizeOverride("font_size", 26);
        vbox.AddChild(header);

        // ── Audio ───────────────────────────────────────────────────
        vbox.AddChild(SectionLabel("Audio"));
        vbox.AddChild(BuildVolumeRow("Master", ConfigManager.Instance?.MasterVolume ?? 0.8f,
            value => ConfigManager.Instance?.SetMasterVolume(value)));
        vbox.AddChild(BuildVolumeRow("Effects", ConfigManager.Instance?.SfxVolume ?? 0.9f,
            value => ConfigManager.Instance?.SetSfxVolume(value)));
        vbox.AddChild(BuildVolumeRow("Ambience", ConfigManager.Instance?.AmbientVolume ?? 0.5f,
            value => ConfigManager.Instance?.SetAmbientVolume(value)));

        // ── Difficulty ──────────────────────────────────────────────
        vbox.AddChild(SectionLabel("Difficulty"));

        var difficultyRow = new HBoxContainer();
        difficultyRow.AddThemeConstantOverride("separation", 8);
        vbox.AddChild(difficultyRow);

        var difficultyButton = new Button { Text = "Cycle Difficulty", CustomMinimumSize = new Vector2(170, 0) };
        difficultyButton.Pressed += () =>
        {
            ConfigManager.Instance?.CycleDifficulty();
            RefreshMenuValues();
        };
        difficultyRow.AddChild(difficultyButton);

        _menuDifficultyValue = new Label { Text = "--" };
        _menuDifficultyValue.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.4f));
        difficultyRow.AddChild(_menuDifficultyValue);

        // ── Persistence ─────────────────────────────────────────────
        vbox.AddChild(SectionLabel("Survival Record"));

        _menuSaveInfo = new Label { Text = "No save yet." };
        vbox.AddChild(_menuSaveInfo);

        var saveRow = new HBoxContainer();
        saveRow.AddThemeConstantOverride("separation", 8);
        vbox.AddChild(saveRow);

        var saveButton = new Button { Text = "Save  [F5]", CustomMinimumSize = new Vector2(130, 0) };
        saveButton.Pressed += () =>
        {
            SaveManager.Instance?.SaveGame("pause menu");
            RefreshMenuValues();
        };
        saveRow.AddChild(saveButton);

        _menuLoadButton = new Button { Text = "Load  [F9]", CustomMinimumSize = new Vector2(130, 0) };
        _menuLoadButton.Pressed += () => SaveManager.Instance?.LoadGame();
        saveRow.AddChild(_menuLoadButton);

        _menuDeleteButton = new Button { Text = "Delete Save", CustomMinimumSize = new Vector2(130, 0) };
        _menuDeleteButton.Pressed += () =>
        {
            SaveManager.Instance?.DeleteSave();
            RefreshMenuValues();
        };
        saveRow.AddChild(_menuDeleteButton);

        // ── Resume ──────────────────────────────────────────────────
        vbox.AddChild(new HSeparator());

        var resumeButton = new Button { Text = "Resume  [Esc]" };
        resumeButton.Pressed += () => GameManager.Instance?.TogglePause();
        vbox.AddChild(resumeButton);

        var footnote = new Label
        {
            Text = "Settings are written to user://settings.cfg immediately.",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        footnote.AddThemeFontSizeOverride("font_size", 11);
        footnote.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f));
        vbox.AddChild(footnote);

        AddChild(_menuRoot);
        RefreshMenuValues();
    }

    private static Label SectionLabel(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", new Color(0.6f, 0.8f, 0.95f));
        return label;
    }

    /// <summary>A labelled slider row wired straight to a ConfigManager mutator.</summary>
    private static HBoxContainer BuildVolumeRow(string title, float initial, Action<float> apply)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        row.AddChild(new Label { Text = title, CustomMinimumSize = new Vector2(90, 0) });

        var slider = new HSlider
        {
            MinValue = 0f,
            MaxValue = 1f,
            Step = 0.05f,
            Value = initial,
            CustomMinimumSize = new Vector2(200, 0),
        };

        var valueLabel = new Label
        {
            Text = $"{initial:P0}",
            CustomMinimumSize = new Vector2(48, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        slider.ValueChanged += value =>
        {
            valueLabel.Text = $"{value:P0}";
            apply((float)value);
        };

        row.AddChild(slider);
        row.AddChild(valueLabel);
        return row;
    }

    /// <summary>Re-read the config/save services into the menu widgets.</summary>
    internal void RefreshMenuValues()
    {
        if (_menuDifficultyValue != null && ConfigManager.Instance is { } config)
        {
            _menuDifficultyValue.Text =
                $"{config.Difficulty}  (zombie dmg ×{config.ZombieDamageMultiplier:F2}, " +
                $"loot ×{config.LootQuantityMultiplier:F2})";
        }

        bool hasSave = SaveManager.Instance?.HasSave() ?? false;

        if (_menuSaveInfo != null)
        {
            _menuSaveInfo.Text = hasSave
                ? $"Save: {SaveManager.Instance!.SavePath.GetFile()}"
                : "No save yet — press Save to record your run.";
        }

        if (_menuLoadButton != null) _menuLoadButton.Disabled = !hasSave;
        if (_menuDeleteButton != null) _menuDeleteButton.Disabled = !hasSave;
    }
}
