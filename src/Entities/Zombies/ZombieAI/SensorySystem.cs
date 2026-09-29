using Godot;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Core.Utilities;
using ZombieApocalypse.World.Environment;

namespace ZombieApocalypse.Entities.Zombies.ZombieAI;

/// <summary>
/// Sensory simulation for zombies and hostile AI.
/// 
/// Integrates:
///   1. Vision Cone (distance + FOV angle + raycast line-of-sight check through obstacles)
///   2. Close Proximity Sense (360-degree awareness within close personal space)
///   3. Hearing (listens to acoustic propagation events via <see cref="AcousticPropagation"/>)
/// </summary>
public partial class SensorySystem : Node3D
{
    [ExportGroup("Vision")]
    [Export] public float SightRange = 15.0f;
    [Export] public float SightAngleDeg = 110.0f;
    [Export] public float ProximityRange = 2.5f;
    [Export(PropertyHint.Layers3DPhysics)] public uint VisionObstacleMask = 1 | 8; // Layer 1 (World) & Layer 4 (VisionOccluder)

    [ExportGroup("Hearing")]
    [Export] public float HearingSensitivity = 1.0f;

    /// <summary>
    /// Night-frenzy modifiers from <see cref="DayNightCycle"/>: at night zombies
    /// see ~35% further and hear ~35% better.
    /// </summary>
    public float EffectiveSightRange =>
        SightRange * (DayNightCycle.Instance?.ZombieNightMultiplier ?? 1f);

    public float EffectiveHearingSensitivity =>
        HearingSensitivity * (DayNightCycle.Instance?.ZombieNightMultiplier ?? 1f);

    [Signal] public delegate void TargetSpottedEventHandler(Node3D target);
    [Signal] public delegate void TargetLostEventHandler();
    [Signal] public delegate void SoundHeardEventHandler(Vector3 position, int soundType);

    public Node3D? CurrentTarget { get; private set; }
    public Vector3 LastKnownTargetPosition { get; private set; }
    public Vector3 LastHeardSoundPosition { get; private set; }
    public bool HasTarget => CurrentTarget != null && GodotObject.IsInstanceValid(CurrentTarget);

    // Target search interval to prevent excessive raycasts
    private float _timeSinceLastSense = 0f;
    private const float SenseInterval = 0.15f; // ~6.6 checks per second

    public override void _Ready()
    {
        AcousticPropagation.Instance?.RegisterListener(this);
    }

    public override void _ExitTree()
    {
        AcousticPropagation.Instance?.DeregisterListener(this);
    }

    public override void _PhysicsProcess(double delta)
    {
        _timeSinceLastSense += (float)delta;
        if (_timeSinceLastSense >= SenseInterval)
        {
            _timeSinceLastSense = 0f;
            ScanForTargets();
        }
    }

    /// <summary>
    /// Called by <see cref="AcousticPropagation"/> when an audible sound occurs in range.
    /// </summary>
    public void OnHeardSound(Vector3 origin, float radius, AudioSourceType type)
    {
        LastHeardSoundPosition = origin;
        EmitSignal(SignalName.SoundHeard, origin, (int)type);
    }

    /// <summary>
    /// Scans for potential targets (e.g., players) in the scene.
    /// </summary>
    private void ScanForTargets()
    {
        var player = GetTree().GetFirstNodeInGroup("player") as Node3D;
        if (player == null || !GodotObject.IsInstanceValid(player))
        {
            if (HasTarget)
            {
                CurrentTarget = null;
                EmitSignal(SignalName.TargetLost);
            }
            return;
        }

        Vector3 myPos = GlobalPosition;
        Vector3 targetPos = player.GlobalPosition;
        float distSq = MathUtils.DistanceSquaredXZ(myPos, targetPos);

        bool detected = false;
        float sightRange = EffectiveSightRange;

        // 1. Proximity check (immediate awareness in close radius)
        if (distSq <= ProximityRange * ProximityRange)
        {
            detected = CheckLineOfSight(myPos, targetPos);
        }
        // 2. Vision cone check
        else if (distSq <= sightRange * sightRange)
        {
            // Calculate forward direction in XZ plane
            Vector3 forward = -GlobalTransform.Basis.Z;
            forward.Y = 0f;
            if (forward.LengthSquared() > 0.001f)
            {
                float facingAngle = Mathf.Atan2(forward.X, forward.Z);
                float halfAngleRad = Mathf.DegToRad(SightAngleDeg * 0.5f);

                if (MathUtils.IsInCone(myPos, facingAngle, halfAngleRad, targetPos, sightRange))
                {
                    detected = CheckLineOfSight(myPos, targetPos);
                }
            }
        }

        if (detected)
        {
            LastKnownTargetPosition = targetPos;
            if (CurrentTarget != player)
            {
                CurrentTarget = player;
                EmitSignal(SignalName.TargetSpotted, player);
            }
        }
        else if (CurrentTarget != null)
        {
            // If lost line of sight or out of range
            CurrentTarget = null;
            EmitSignal(SignalName.TargetLost);
        }
    }

    /// <summary>
    /// Raycasts to verify no walls or vision occluders block line of sight.
    /// </summary>
    private bool CheckLineOfSight(Vector3 from, Vector3 to)
    {
        var space = GetWorld3D()?.DirectSpaceState;
        if (space == null) return false;

        // Raise eye level above floor
        Vector3 rayFrom = from + Vector3.Up * 1.5f;
        Vector3 rayTo = to + Vector3.Up * 1.0f;

        var query = PhysicsRayQueryParameters3D.Create(rayFrom, rayTo, VisionObstacleMask);
        var hit = space.IntersectRay(query);

        // If no obstacle hit, line of sight is clear
        return hit.Count == 0;
    }
}
