using Godot;
using ZombieApocalypse.Core.StateMachine;

namespace ZombieApocalypse.Entities.Zombies.ZombieAI;

/// <summary>
/// Base class for all zombie behavior states.
/// Provides typed reference to the owning <see cref="ZombieBase"/> entity.
/// </summary>
public abstract partial class ZombieBaseState : State
{
    protected ZombieBase Zombie = null!;

    public override void _Ready()
    {
        base._Ready();
        // State is child of StateMachine, StateMachine is child of ZombieBase
        Zombie = GetOwner<ZombieBase>() ?? (GetParent()?.GetParent() as ZombieBase)!;
    }
}
