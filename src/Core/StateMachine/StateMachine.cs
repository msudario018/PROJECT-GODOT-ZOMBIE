using Godot;
using System.Collections.Generic;

namespace ZombieApocalypse.Core.StateMachine;

/// <summary>
/// Generic hierarchical state machine controller.
/// Add <see cref="State"/>-derived nodes as children; set <see cref="InitialState"/>
/// to the starting state path. The machine automatically discovers child states,
/// subscribes to their Transitioned signals, and delegates frame callbacks.
/// </summary>
/// <example>
/// Scene tree:
///   StateMachine
///     ├── Idle   (extends State)
///     ├── Chase  (extends State)
///     └── Attack (extends State)
/// </example>
public partial class StateMachine : Node
{
    /// <summary>
    /// Path to the initial state node. Must be a direct child of this StateMachine.
    /// If unset, the first child State is used as the initial state.
    /// </summary>
    [Export] public NodePath? InitialState;

    private State? _currentState;
    private readonly Dictionary<string, State> _states = new();

    /// <summary>Name of the currently active state, or "None" if no state is active.</summary>
    public string CurrentStateName => _currentState?.Name ?? "None";

    /// <summary>The currently active State instance, or null.</summary>
    public State? CurrentState => _currentState;

    public override void _Ready()
    {
        // Discover all child State nodes and subscribe to their transition signals
        foreach (var child in GetChildren())
        {
            if (child is State state)
            {
                _states[state.Name] = state;
                state.Transitioned += OnStateTransitioned;
            }
        }

        // Activate the initial state
        if (InitialState != null && !InitialState.IsEmpty)
        {
            _currentState = GetNode<State>(InitialState);
        }
        else if (_states.Count > 0)
        {
            // Fall back to the first discovered child state
            var enumerator = _states.Values.GetEnumerator();
            if (enumerator.MoveNext())
                _currentState = enumerator.Current;
            enumerator.Dispose();
        }

        _currentState?.Enter();

        if (_currentState != null)
            GD.Print($"[StateMachine] {GetParent()?.Name ?? "?"} → Initial state: {_currentState.Name}");
    }

    public override void _Process(double delta)
    {
        _currentState?.Update(delta);
    }

    public override void _PhysicsProcess(double delta)
    {
        _currentState?.PhysicsUpdate(delta);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        _currentState?.HandleInput(@event);
    }

    /// <summary>
    /// Transition to a target state by name. Calls Exit on the current state
    /// and Enter on the new state with optional message data.
    /// </summary>
    /// <param name="targetStateName">Name of the target State child node.</param>
    /// <param name="msg">Optional data to pass to the new state's Enter method.</param>
    public void TransitionTo(string targetStateName, Dictionary<string, Variant>? msg = null)
    {
        if (!_states.TryGetValue(targetStateName, out var targetState))
        {
            GD.PrintErr($"[StateMachine] State '{targetStateName}' not found on {GetParent()?.Name ?? "?"}. " +
                         $"Available: [{string.Join(", ", _states.Keys)}]");
            return;
        }

        var previousName = _currentState?.Name ?? "None";
        _currentState?.Exit();
        _currentState = targetState;
        _currentState.Enter(msg);

        GD.Print($"[StateMachine] {GetParent()?.Name ?? "?"}: {previousName} → {_currentState.Name}");
    }

    /// <summary>
    /// Force-reset to the initial state without triggering Exit on the current state.
    /// Use for hard resets (e.g., respawn, teleport).
    /// </summary>
    public void Reset()
    {
        _currentState = null;

        if (InitialState != null && !InitialState.IsEmpty)
            _currentState = GetNode<State>(InitialState);

        _currentState?.Enter();
    }

    /// <summary>
    /// Check whether a state with the given name exists in this machine.
    /// </summary>
    public bool HasState(string stateName) => _states.ContainsKey(stateName);

    private void OnStateTransitioned(string newStateName)
    {
        TransitionTo(newStateName);
    }
}
