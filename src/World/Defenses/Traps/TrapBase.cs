namespace ZombieApocalypse.World.Defenses.Traps;

using Godot;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Data;

/// <summary>
/// Base class for deployable perimeter traps.
///
/// A trap is a cheap, single-use defensive tool. The shared contract is
/// <see cref="Arm"/>, <see cref="Trigger"/> and <see cref="Disarm"/>: it watches
/// a trigger volume, fires once, then spends itself (or, for a rearmable trap,
/// returns to a cooldown). Traps never block movement, so they can be dropped
/// behind a breach without trapping the player.
/// </summary>
public abstract partial class TrapBase : Node3D
{
    [ExportGroup("Trap")]
    /// <summary>Radius in which the trap detects and triggers.</summary>
    [Export] public float TriggerRadius = 2.5f;
    /// <summary>Only entities in this layer group trigger the trap.</summary>
    [Export] public string TriggerGroup = "zombies";
    /// <summary>Set false to place the trap already spent.</summary>
    [Export] public bool StartsArmed = true;
    /// <summary>Seconds before the trap can trigger again after firing.</summary>
    [Export] public float ReArmCooldown = 0f;

    [Signal] public delegate void TrippedEventHandler(Node3D victim);
    [Signal] public delegate void SpentEventHandler();

    /// <summary>True when the trap will trigger on the next victim.</summary>
    public bool IsArmed { get; private set; }
    /// <summary>True once a single-use trap has fired.</summary>
    public bool IsSpent { get; private set; }
    /// <summary>How many entities this trap has caught (test/telemetry hook).</summary>
    public int TriggerCount { get; private set; }
    /// <summary>Position of the last victim.</summary>
    public Vector3 LastVictimPosition { get; private set; }

    private float _cooldown;
    private Area3D? _triggerArea;

    public override void _Ready()
    {
        IsArmed = StartsArmed;
        IsSpent = false;
        _cooldown = 0f;

        BuildTriggerArea();
        AddToGroup("traps");
        AddToGroup($"trap_{TrapName}");
    }

    /// <summary>Short identifier used in the trap_<name> group.</summary>
    protected abstract string TrapName { get; }

    /// <summary>Build the detection sphere. Overridable for bespoke shapes.</summary>
    protected virtual void BuildTriggerArea()
    {
        _triggerArea = new Area3D { Name = "TriggerArea" };
        var shape = new CollisionShape3D
        {
            Shape = new SphereShape3D { Radius = TriggerRadius },
        };
        _triggerArea.AddChild(shape);
        AddChild(_triggerArea);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // The bedding-in delay doubles as the re-arm delay.
        if (_cooldown > 0f)
        {
            _cooldown -= dt;
            if (_cooldown <= 0f && !IsSpent) IsArmed = true;
            if (_cooldown > 0f) return;
        }

        if (!IsArmed || IsSpent) return;

        foreach (var node in GetTree().GetNodesInGroup(TriggerGroup))
        {
            if (node is not Node3D victim) continue;
            if (!GodotObject.IsInstanceValid(victim) || victim == GetParent()) continue;

            // Corpses and the trap's own owner never trigger it.
            if (victim.IsInGroup("corpses") || victim.IsInGroup("traps")) continue;

            if (GlobalPosition.DistanceTo(victim.GlobalPosition) > TriggerRadius) continue;

            Fire(victim);
            return;   // one victim per trigger
        }
    }

    /// <summary>
    /// Fire the trap at a victim, provided it is actually within the trigger
    /// radius. Range is enforced here rather than only in the physics tick so
    /// that scripted and test callers cannot bypass the trap's own contract.
    /// </summary>
    public bool Fire(Node3D victim)
    {
        if (!IsArmed || IsSpent) return false;
        if (victim == null || !GodotObject.IsInstanceValid(victim)) return false;
        if (GlobalPosition.DistanceTo(victim.GlobalPosition) > TriggerRadius) return false;

        TriggerCount++;
        LastVictimPosition = victim.GlobalPosition;
        EmitSignal(SignalName.Tripped, victim);

        ApplyEffect(victim);

        GD.Print($"[{GetType().Name}] Triggered on {victim.Name} at {victim.GlobalPosition:F1}.");

        if (ReArmCooldown <= 0f)
        {
            IsArmed = false;
            IsSpent = true;
            EmitSignal(SignalName.Spent);
        }
        else
        {
            _cooldown = ReArmCooldown;
        }

        return true;
    }

    /// <summary>The damage/effect payload — implemented per trap.</summary>
    protected abstract void ApplyEffect(Node3D victim);

    /// <summary>
    /// Delay before the trap can trigger again after firing. Also used at spawn
    /// to keep a freshly placed trap from catching its own owner.
    /// </summary>
    protected void SetSafeDisarmDelay(float seconds) => _cooldown = seconds;

    /// <summary>Disarm without triggering (the player picking it up).</summary>
    public void Disarm()
    {
        IsArmed = false;
        _cooldown = 0f;
    }

    /// <summary>Re-arm a spent (non-permanent) trap.</summary>
    public void ReArm()
    {
        IsSpent = false;
        IsArmed = true;
        _cooldown = 0f;
    }

    /// <summary>Find the victim's health component, if it has one.</summary>
    protected static HealthComponent? HealthOf(Node3D victim)
        => victim.GetNodeOrNull<HealthComponent>("HealthComponent");
}
