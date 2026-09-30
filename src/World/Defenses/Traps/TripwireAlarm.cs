namespace ZombieApocalypse.World.Defenses.Traps;

using Godot;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Data;

/// <summary>
/// Tripwire Alarm: a taut wire that rings the camp when something crosses it.
///
/// Purely informational — it deals no damage. Its job is early warning: the
/// moment a horde touches the wire, every zombie on the map is alerted to the
/// breach position and the player gets an acoustic cue. Cheap enough to string
/// along a whole approach, which makes it the natural early-warning layer in
/// front of the punji trench line.
/// </summary>
public partial class TripwireAlarm : TrapBase
{
    [ExportGroup("Alarm")]
    /// <summary>Radius in which the whole sector is alerted to the breach.</summary>
    [Export] public float AlertRadius = 60f;
    /// <summary>How long alerted zombies stay agitated.</summary>
    [Export] public float AlertDuration = 25f;
    /// <summary>Noise radius of the bell.</summary>
    [Export] public float BellRadius = 45f;
    /// <summary>Seconds between repeat alerts while zombies keep crossing.</summary>
    [Export] public float ReTriggerInterval = 3f;

    /// <summary>How many times this wire has alerted the sector.</summary>
    public int AlertsRaised { get; private set; }
    /// <summary>True once at least one alert has been raised.</summary>
    public bool HasRaisedAlert => AlertsRaised > 0;

    protected override string TrapName => "tripwire";

    public override void _Ready()
    {
        TriggerRadius = 3.0f;
        ReArmCooldown = ReTriggerInterval;
        base._Ready();
    }

    protected override void ApplyEffect(Node3D victim)
    {
        AlertsRaised++;

        // The bell: audible by the player and by anything listening.
        EventBus.Instance?.EmitSound(GlobalPosition, BellRadius, AudioSourceType.Alarm);

        // Wake the sector. Unlike a scream this is not a mob aggro — it is a
        // rally call, so it points every zombie at the breach, not at the noise.
        int alerted = 0;
        foreach (var node in GetTree().GetNodesInGroup("zombies"))
        {
            if (node is not Entities.Zombies.ZombieBase zombie) continue;
            if (!GodotObject.IsInstanceValid(zombie) || zombie.IsDead) continue;
            if (zombie.GlobalPosition.DistanceTo(GlobalPosition) > AlertRadius) continue;

            zombie.AlertTo(GlobalPosition, AlertDuration);
            alerted++;
        }

        GD.Print($"[TripwireAlarm] BREACH at {GlobalPosition:F1} — alerted {alerted} zombie(s) " +
                 $"within {AlertRadius:F0}m.");
    }
}
