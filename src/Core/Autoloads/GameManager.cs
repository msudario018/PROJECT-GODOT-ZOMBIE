using Godot;
using ZombieApocalypse.Core.Data;

namespace ZombieApocalypse.Core.Autoloads;

/// <summary>
/// Master game state manager. Handles game state transitions,
/// pause control, and programmatic input action registration.
/// 
/// Registered as Autoload "Game" (Priority 3).
/// </summary>
/// <remarks>
/// Input actions are registered here at startup instead of in project.godot
/// to keep mappings version-controlled in code and avoid fragile hand-edited
/// serialization of InputEvent objects.
/// </remarks>
public partial class GameManager : Node
{
    /// <summary>Singleton instance, set in _Ready.</summary>
    public static GameManager? Instance { get; private set; }

    /// <summary>Current top-level game state.</summary>
    public GameState CurrentState { get; private set; } = GameState.Playing;

    /// <summary>Whether the game is currently paused.</summary>
    public bool IsPaused => GetTree().Paused;

    public override void _Ready()
    {
        Instance = this;
        SetupInputActions();
        GD.Print("[GameManager] Initialized. Input actions registered.");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("pause"))
        {
            TogglePause();
            GetViewport().SetInputAsHandled();
            return;
        }

        // Restart from the death screen
        if (CurrentState == GameState.GameOver && @event.IsActionPressed("restart"))
        {
            GetTree().Paused = false;
            CurrentState = GameState.Playing;
            GetTree().ReloadCurrentScene();
            GD.Print("[GameManager] Restarting scene after death.");
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Called when the player's HealthComponent dies.</summary>
    public void HandlePlayerDeath()
    {
        if (CurrentState == GameState.GameOver) return;
        SetState(GameState.GameOver);
        GD.Print("[GameManager] PLAYER DIED — press [P] to restart.");
    }

    // ── Game State ─────────────────────────────────────────────────

    /// <summary>Toggle pause on/off. Only works during Playing/Paused states.</summary>
    public void TogglePause()
    {
        if (CurrentState != GameState.Playing && CurrentState != GameState.Paused)
            return;

        if (IsPaused)
        {
            GetTree().Paused = false;
            CurrentState = GameState.Playing;
            GD.Print("[GameManager] Resumed.");
        }
        else
        {
            GetTree().Paused = true;
            CurrentState = GameState.Paused;
            GD.Print("[GameManager] Paused.");
        }
    }

    /// <summary>Force a specific game state.</summary>
    public void SetState(GameState newState)
    {
        var old = CurrentState;
        CurrentState = newState;
        GD.Print($"[GameManager] State: {old} → {newState}");
    }

    // ── Input Action Registration ──────────────────────────────────

    /// <summary>
    /// Register all game input actions programmatically.
    /// This runs before any gameplay nodes process input.
    /// </summary>
    private void SetupInputActions()
    {
        // ── Movement (Primary & Aliases) ──
        RegisterAction("move_forward",  Key.W, Key.Up);
        RegisterAction("move_backward", Key.S, Key.Down);
        RegisterAction("move_left",     Key.A, Key.Left);
        RegisterAction("move_right",    Key.D, Key.Right);
        RegisterAction("move_up",       Key.W, Key.Up);
        RegisterAction("move_down",     Key.S, Key.Down);
        RegisterAction("sprint",        Key.Shift);

        // ── Combat ──
        RegisterAction("attack_primary",   Key.Space);
        RegisterAction("attack_secondary", Key.Q);
        RegisterAction("reload",           Key.R);

        // ── Interaction ──
        RegisterAction("interact", Key.E);
        RegisterAction("flashlight", Key.F);

        // ── UI ──
        RegisterAction("pause",     Key.Escape);
        RegisterAction("inventory", Key.Tab);
        RegisterAction("map",       Key.M);
        RegisterAction("restart",   Key.P);

        // ── Mouse Attack & Action ──
        RegisterMouseAction("attack",                MouseButton.Left);
        RegisterMouseAction("attack_mouse_primary",   MouseButton.Left);
        RegisterMouseAction("attack_mouse_secondary", MouseButton.Right);

        // Also add Space to "attack" action
        var spaceEv = new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space };
        InputMap.ActionAddEvent("attack", spaceEv);
    }

    /// <summary>Register a keyboard input action with one or more key bindings.</summary>
    private static void RegisterAction(string actionName, params Key[] keys)
    {
        if (!InputMap.HasAction(actionName))
        {
            InputMap.AddAction(actionName);
        }
        else
        {
            // Erase any corrupted or improperly serialized events
            InputMap.ActionEraseEvents(actionName);
        }

        foreach (var key in keys)
        {
            var ev = new InputEventKey
            {
                Keycode = key,
                PhysicalKeycode = key
            };
            InputMap.ActionAddEvent(actionName, ev);
        }
    }

    /// <summary>Register a mouse button input action.</summary>
    private static void RegisterMouseAction(string actionName, MouseButton button)
    {
        if (!InputMap.HasAction(actionName))
        {
            InputMap.AddAction(actionName);
        }
        else
        {
            InputMap.ActionEraseEvents(actionName);
        }

        var ev = new InputEventMouseButton
        {
            ButtonIndex = button
        };
        InputMap.ActionAddEvent(actionName, ev);
    }
}
