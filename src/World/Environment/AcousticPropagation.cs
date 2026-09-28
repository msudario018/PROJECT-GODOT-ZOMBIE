using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Core.Utilities;
using ZombieApocalypse.Entities.Zombies.ZombieAI;

namespace ZombieApocalypse.World.Environment;

/// <summary>
/// World acoustic propagation simulation.
/// 
/// Intercepts raw sound events from <see cref="EventBus"/>, calculates
/// environmental attenuation (rain, indoor absorption, surface reflection),
/// and alerts all zombie sensory systems within the propagated radius.
/// </summary>
public partial class AcousticPropagation : Node
{
    public static AcousticPropagation? Instance { get; private set; }

    [ExportGroup("Environmental Modifiers")]
    [Export] public float IndoorAbsorptionFactor = 0.4f;
    [Export] public float RainAttenuationFactor = 0.6f;
    [Export] public float MetalAmplificationFactor = 1.5f;

    [ExportGroup("State")]
    [Export] public bool IsRaining = false;

    // Track active sound listeners (SensorySystem instances register themselves)
    private readonly HashSet<SensorySystem> _listeners = new();

    public override void _Ready()
    {
        Instance = this;

        if (EventBus.Instance != null)
        {
            EventBus.Instance.OnSoundEmitted += ProcessSoundEvent;
        }

        GD.Print("[AcousticPropagation] Initialized.");
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;

        if (EventBus.Instance != null)
        {
            EventBus.Instance.OnSoundEmitted -= ProcessSoundEvent;
        }
    }

    /// <summary>
    /// Register a sensory listener that should receive acoustic notifications.
    /// </summary>
    public void RegisterListener(SensorySystem listener)
    {
        _listeners.Add(listener);
    }

    /// <summary>
    /// Deregister a sensory listener (e.g., zombie died or despawned).
    /// </summary>
    public void DeregisterListener(SensorySystem listener)
    {
        _listeners.Remove(listener);
    }

    /// <summary>
    /// Process an acoustic event and notify all nearby listeners.
    /// </summary>
    private void ProcessSoundEvent(Vector3 origin, float baseRadius, AudioSourceType type)
    {
        if (baseRadius <= 0.1f) return;

        float finalRadius = baseRadius;

        // Apply weather attenuation
        if (IsRaining)
        {
            finalRadius *= RainAttenuationFactor;
        }

        float finalRadiusSq = finalRadius * finalRadius;

        // Propagate to registered sensory systems
        foreach (var listener in _listeners)
        {
            if (!GodotObject.IsInstanceValid(listener))
                continue;

            var listenerPos = listener.GlobalPosition;
            float distSq = MathUtils.DistanceSquaredXZ(origin, listenerPos);

            if (distSq <= finalRadiusSq)
            {
                listener.OnHeardSound(origin, finalRadius, type);
            }
        }
    }
}
