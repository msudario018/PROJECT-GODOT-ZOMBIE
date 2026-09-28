using Godot;
using System.Collections.Generic;

namespace ZombieApocalypse.Core.StateMachine;

/// <summary>
/// Abstract base state for the hierarchical state machine framework.
/// Each concrete state is a Node added as a child of a <see cref="StateMachine"/> node.
/// Override the virtual methods to define per-state behavior.
/// </summary>
/// <remarks>
/// Usage: Create a class that extends State, override Enter/Exit/Update/PhysicsUpdate,
/// and emit the <see cref="Transitioned"/> signal to request a state change.
/// The parent StateMachine listens for this signal and handles the transition.
/// </remarks>
public abstract partial class State : Node
{
    /// <summary>
    /// Emitted when this state wants to transition to another state.
    /// The parent <see cref="StateMachine"/> listens for this signal.
    /// </summary>
    /// <param name="newStateName">Name of the target State node to transition to.</param>
    [Signal]
    public delegate void TransitionedEventHandler(string newStateName);

    /// <summary>
    /// Called when the state machine enters this state.
    /// Use for initialization, animation triggers, and resource allocation.
    /// </summary>
    /// <param name="msg">Optional data dictionary passed from the previous state or transition trigger.</param>
    public virtual void Enter(Dictionary<string, Variant>? msg = null) { }

    /// <summary>
    /// Called when the state machine exits this state.
    /// Use for cleanup, timer cancellation, and resource release.
    /// </summary>
    public virtual void Exit() { }

    /// <summary>
    /// Called every frame (via _Process) while this state is active.
    /// Use for non-physics logic: timers, animations, UI updates.
    /// </summary>
    /// <param name="delta">Frame delta time in seconds.</param>
    public virtual void Update(double delta) { }

    /// <summary>
    /// Called every physics frame (via _PhysicsProcess) while this state is active.
    /// Use for movement, collision queries, and physics-dependent calculations.
    /// </summary>
    /// <param name="delta">Physics delta time in seconds.</param>
    public virtual void PhysicsUpdate(double delta) { }

    /// <summary>
    /// Called for unhandled input events while this state is active.
    /// Use for state-specific input handling (e.g., attack in combat state).
    /// </summary>
    /// <param name="event">The unhandled input event.</param>
    public virtual void HandleInput(InputEvent @event) { }
}
