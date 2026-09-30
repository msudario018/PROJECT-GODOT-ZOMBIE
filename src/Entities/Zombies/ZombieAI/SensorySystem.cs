using Godot;
using ZombieApocalypse.Core.Components;
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

    [ExportGroup("Smell")]
    /// <summary>How far this tracker can follow a blood-scent trail (0 = cannot smell).</summary>
    [Export] public float ScentRange = 0.0f;
    /// <summary>Scent strength at which the trail is considered detectable.</summary>
    [Export] public float ScentThreshold = 0.2f;

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
    [Signal] public delegate void ScentTrackedEventHandler(Vector3 position, float strength);

    public Node3D? CurrentTarget { get; private set; }
    public Vector3 LastKnownTargetPosition { get; private set; }
    public Vector3 LastHeardSoundPosition { get; private set; }
    /// <summary>The blood-scent point this tracker is currently following, if any.</summary>
    public Vector3 TrackedScentPosition { get; private set; }
    /// <summary>True while a scent trail is driving this tracker's movement.</summary>
    public bool IsTrackingScent { get; private set; }
    public bool HasTarget => CurrentTarget != null && GodotObject.IsInstanceValid(CurrentTarget);
    /// <summary>True when this tracker can follow scent at all.</summary>
    public bool CanTrackScent => ScentRange > 0.01f;

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
            ScanForScent();
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
    /// Follow the strongest blood-scent puff within <see cref="ScentRange"/>.
    ///
    /// Scent deliberately bypasses the vision cone and the line-of-sight raycast:
    /// that is the point of the mechanic. A bleeding target can be tracked through
    /// walls and in total darkness, so bleeding under pressure is a real cost.
    ///
    /// Returns true when a trail was found; the caller should steer toward
    /// <see cref="TrackedScentPosition"/> until it is reached and re-queried.
    /// </summary>
    public bool TryTrackScent()
    {
        if (!CanTrackScent)
        {
            IsTrackingScent = false;
            return false;
        }

        // Night sharpens the sense of smell, the same way it sharpens hearing.
        float range = ScentRange * (DayNightCycle.Instance?.ZombieNightMultiplier ?? 1f);

        if (!ScentComponent.TryFindStrongestScent(GlobalPosition, range,
                out Vector3 position, out float strength)
            || strength < ScentThreshold)
        {
            IsTrackingScent = false;
            return false;
        }

        bool isNew = !IsTrackingScent
                     || TrackedScentPosition.DistanceTo(position) > 0.5f;

        IsTrackingScent = true;
        TrackedScentPosition = position;

        if (isNew)
        {
            EmitSignal(SignalName.ScentTracked, position, strength);
            GD.Print($"[SensorySystem] Blood scent picked up (strength {strength:F2}) at {position:F1}.");
        }

        return true;
    }

    /// <summary>
    /// Scans for the nearest valid prey: the player, survivors and bandits
    /// are all potential targets (previously zombies only ever saw the player).
    /// </summary>
    private void ScanForTargets()
    {
        var tree = GetTree();
        Node3D? bestDetected = null;
        Vector3 bestPos = Vector3.Zero;
        float bestDistSq = float.MaxValue;

        foreach (var group in new[] { "player", "survivors", "bandits" })
        {
            foreach (var node in tree.GetNodesInGroup(group))
            {
                if (node is not Node3D candidate || !GodotObject.IsInstanceValid(candidate)) continue;
                if (candidate is Zombies.ZombieBase) continue;
                if (candidate is Survivors.SurvivorBase survivor && !survivor.IsAlive) continue;
                if (candidate is Bandits.BanditBase bandit && bandit.IsDead) continue;

                var health = candidate.GetNodeOrNull<Core.Components.HealthComponent>("HealthComponent");
                if (health != null && !health.IsAlive) continue;

                Vector3 myPos = GlobalPosition;
                Vector3 targetPos = candidate.GlobalPosition;
                float distSq = MathUtils.DistanceSquaredXZ(myPos, targetPos);
                if (distSq >= bestDistSq) continue;

                if (!DetectCandidate(myPos, targetPos, distSq)) continue;

                bestDistSq = distSq;
                bestDetected = candidate;
                bestPos = targetPos;
            }
        }

        if (bestDetected != null)
        {
            LastKnownTargetPosition = bestPos;
            if (CurrentTarget != bestDetected)
            {
                CurrentTarget = bestDetected;
                EmitSignal(SignalName.TargetSpotted, bestDetected);
            }
        }
        else if (CurrentTarget != null)
        {
            CurrentTarget = null;
            EmitSignal(SignalName.TargetLost);
        }
    }

    /// <summary>
    /// Scent pass, run after vision. Losing line of sight does not end tracking:
    /// if a trail is available the tracker keeps a last-known scent position, so
    /// a wounded target that ducked behind a wall is still followed.
    /// </summary>
    private void ScanForScent()
    {
        if (!CanTrackScent) return;

        if (!TryTrackScent() && CurrentTarget == null)
        {
            // Trail gone and nothing seen: fall back to the last scent we knew.
            if (IsTrackingScent)
            {
                IsTrackingScent = false;
                LastKnownTargetPosition = TrackedScentPosition;
            }
        }
    }

    /// <summary>Proximity + vision-cone + line-of-sight check for one candidate.</summary>
    private bool DetectCandidate(Vector3 myPos, Vector3 targetPos, float distSq)
    {
        float sightRange = EffectiveSightRange;

        // 1. Proximity check (immediate awareness in close radius)
        if (distSq <= ProximityRange * ProximityRange)
        {
            return CheckLineOfSight(myPos, targetPos);
        }

        // 2. Vision cone check
        if (distSq <= sightRange * sightRange)
        {
            Vector3 forward = -GlobalTransform.Basis.Z;
            forward.Y = 0f;
            if (forward.LengthSquared() > 0.001f)
            {
                float facingAngle = Mathf.Atan2(forward.X, forward.Z);
                float halfAngleRad = Mathf.DegToRad(SightAngleDeg * 0.5f);

                if (MathUtils.IsInCone(myPos, facingAngle, halfAngleRad, targetPos, sightRange))
                {
                    return CheckLineOfSight(myPos, targetPos);
                }
            }
        }

        return false;
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
