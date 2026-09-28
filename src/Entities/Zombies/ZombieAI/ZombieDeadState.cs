using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Autoloads;

namespace ZombieApocalypse.Entities.Zombies.ZombieAI;

/// <summary>
/// Death state.
/// Disables collision, broadcasts kill event to <see cref="EventBus"/>,
/// and plays death sink/fade.
/// </summary>
public partial class ZombieDeadState : ZombieBaseState
{
    public override void Enter(Dictionary<string, Variant>? msg = null)
    {
        Zombie.Velocity = Vector3.Zero;

        // Disable collisions
        Zombie.CollisionLayer = 0;
        Zombie.CollisionMask = 0;

        // Broadcast to EventBus
        EventBus.Instance?.EmitEntityKilled(Zombie);

        // Visual death sequence: tip over and sink
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(Zombie.Mesh, "rotation:z", Mathf.Pi * 0.45f, 0.4f);
        tween.TweenProperty(Zombie.Mesh, "position:y", 0.1f, 0.4f);

        // Clean up after sinking
        tween.Chain().TweenInterval(4.0f);
        tween.Chain().TweenProperty(Zombie.Mesh, "scale", Vector3.Zero, 0.5f);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(Zombie))
            {
                Zombie.QueueFree();
            }
        }));
    }
}
