using Godot;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Data;

namespace ZombieApocalypse.Core.Components;

/// <summary>
/// Reusable acoustic emitter component.
/// Attached to entities (player, zombies, bandits, weapons, generators)
/// to broadcast acoustic disturbance events into the world.
/// </summary>
public partial class AudioEmitterComponent : Node3D
{
    [ExportGroup("Noise Radii")]
    [Export] public float FootstepWalkRadius = 4.0f;
    [Export] public float FootstepSprintRadius = 12.0f;
    [Export] public float MeleeSwingRadius = 6.0f;
    [Export] public float GunshotRadius = 45.0f;
    [Export] public float GunshotSuppressedRadius = 7.0f;
    [Export] public float ZombieGroanRadius = 15.0f;
    [Export] public float ZombieScreamRadius = 50.0f;

    /// <summary>
    /// Emit a sound event with a custom radius and type.
    /// </summary>
    public void EmitCustomSound(float radius, AudioSourceType type)
    {
        EventBus.Instance?.EmitSound(GlobalPosition, radius, type);
    }

    /// <summary>Emit footstep noise based on walking vs sprinting.</summary>
    public void EmitFootstep(bool isSprinting)
    {
        float radius = isSprinting ? FootstepSprintRadius : FootstepWalkRadius;
        var type = isSprinting ? AudioSourceType.FootstepSprint : AudioSourceType.Footstep;
        EventBus.Instance?.EmitSound(GlobalPosition, radius, type);
    }

    /// <summary>Emit melee weapon impact / swing noise.</summary>
    public void EmitMeleeSwing()
    {
        EventBus.Instance?.EmitSound(GlobalPosition, MeleeSwingRadius, AudioSourceType.MeleeImpact);
    }

    /// <summary>Emit gunshot acoustic signature.</summary>
    public void EmitGunshot(bool isSuppressed = false)
    {
        float radius = isSuppressed ? GunshotSuppressedRadius : GunshotRadius;
        var type = isSuppressed ? AudioSourceType.GunshotSuppressed : AudioSourceType.Gunshot;
        EventBus.Instance?.EmitSound(GlobalPosition, radius, type);
    }

    /// <summary>Emit ambient or alert zombie vocalization.</summary>
    public void EmitZombieVoice(bool isScreaming = false)
    {
        float radius = isScreaming ? ZombieScreamRadius : ZombieGroanRadius;
        EventBus.Instance?.EmitSound(GlobalPosition, radius, AudioSourceType.VoiceZombie);
    }
}
