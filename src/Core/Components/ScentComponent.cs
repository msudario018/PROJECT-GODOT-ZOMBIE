namespace ZombieApocalypse.Core.Components;

using Godot;
using System.Collections.Generic;

/// <summary>
/// A single drop of blood scent left on the ground. Scent fades over time and
/// is stronger the fresher it is.
/// </summary>
public struct ScentPuff
{
    public Vector3 Position;
    public float Strength;      // 0..1, decays with age
    public float AgeSeconds;
    public ScentComponent? Source;   // who left it (null once the owner is gone)
}

/// <summary>
/// Blood-scent trail for any entity (the player when bleeding, corpses, wounded
/// survivors). The host reports bleeding via <see cref="ReportBleeding"/>; this
/// component samples the host's position on an interval, drops puffs into a
/// shared world registry, and exposes a scent-strength query for AI.
///
/// This is the scent half of the detection model — <c>SensorySystem</c>
/// consumes it, and unlike sight it ignores light level and walls, so a bleeding
/// player can be tracked through the dark at the cost of leaving a trail.
/// </summary>
public partial class ScentComponent : Node
{
    /// <summary>Process-wide registry so AI can query scent without a direct reference.</summary>
    public static readonly List<ScentPuff> WorldScent = new();

    [ExportGroup("Emission")]
    /// <summary>Seconds between scent samples while bleeding.</summary>
    [Export] public float DropInterval = 0.6f;
    /// <summary>How long a single puff stays detectable.</summary>
    [Export] public float ScentLifetime = 25f;
    /// <summary>Minimum movement between samples (m); stops stationary puffs stacking.</summary>
    [Export] public float MinMoveDistance = 0.8f;
    [Export] public float InitialStrength = 1.0f;
    /// <summary>Scale applied when the host is sprinting (sweat, panic).</summary>
    [Export] public float SprintStrengthMultiplier = 1.4f;

    [ExportGroup("Integration")]
    /// <summary>Register puffs in the shared world registry (off for pure local tests).</summary>
    [Export] public bool PublishToWorld = true;
    /// <summary>Expose this entity to scent-seeking AI.</summary>
    [Export] public bool IsScentSource = true;

    [Signal] public delegate void ScentDroppedEventHandler(Vector3 position, float strength);

    private float _dropTimer;
    private Vector3 _lastDropPosition;
    private bool _hasDropped;

    /// <summary>
    /// World position of the host entity. This component is a plain Node (not
    /// Node3D), so it reads its position from the parent rather than owning one.
    /// </summary>
    private Vector3 HostPosition => (GetParent() as Node3D)?.GlobalPosition ?? Vector3.Zero;

    /// <summary>True while the host is losing blood and laying a trail.</summary>
    public bool IsBleeding { get; private set; }
    /// <summary>True when the host is sprinting (drops a stronger trail).</summary>
    public bool IsSprinting { get; set; }
    /// <summary>Number of puffs this component has dropped (test hook).</summary>
    public int PuffsDropped { get; private set; }
    /// <summary>Live strength of the freshest puff this component owns.</summary>
    public float CurrentScentStrength { get; private set; }

    public override void _Ready()
    {
        // Register the HOST as a scent source: AI searches for entities, not for
        // the component hanging off them.
        if (GetParent() is Node3D host) host.AddToGroup("scent_sources");
        _lastDropPosition = HostPosition;
    }

    public override void _ExitTree()
    {
        // Drop our puffs so a removed host does not leave a permanent trail.
        if (PublishToWorld) WorldScent.RemoveAll(p => p.Source == this);
    }

    /// <summary>Called by the host when it starts or stops losing blood.</summary>
    public void SetBleeding(bool bleeding)
    {
        if (IsBleeding == bleeding) return;
        IsBleeding = bleeding;

        if (bleeding)
            GD.Print($"[ScentComponent] {GetParent()?.Name ?? "?"} starts bleeding — laying a scent trail.");
    }

    /// <summary>Report blood loss: sets the bleeding flag and emits a puff now.</summary>
    public void ReportBleeding(float amount)
    {
        if (amount <= 0f) return;
        SetBleeding(true);
        DropPuff();
    }

    public override void _Process(double delta)
    {
        if (!IsBleeding) return;

        _dropTimer += (float)delta;
        if (_dropTimer < DropInterval) return;

        _dropTimer = 0f;

        // Only drop when the host actually moved, so standing still does not
        // build an ever-stronger pile of scent at one point.
        if (_hasDropped && HostPosition.DistanceTo(_lastDropPosition) < MinMoveDistance)
            return;

        DropPuff();
    }

    /// <summary>Emit one scent puff at the host's current position.</summary>
    public void DropPuff()
    {
        float strength = Mathf.Clamp(
            IsSprinting ? InitialStrength * SprintStrengthMultiplier : InitialStrength, 0f, 2f);

        var puff = new ScentPuff
        {
            Position = HostPosition,
            Strength = strength,
            AgeSeconds = 0f,
            Source = this,
        };

        if (PublishToWorld)
        {
            WorldScent.Add(puff);
            if (WorldScent.Count > 400) WorldScent.RemoveRange(0, WorldScent.Count - 400);
        }

        _lastDropPosition = HostPosition;
        _hasDropped = true;
        PuffsDropped++;
        CurrentScentStrength = strength;

        EmitSignal(SignalName.ScentDropped, puff.Position, strength);
    }

    /// <summary>Decay all world scent. Call once per frame from the world, or a test.</summary>
    public static void AgeWorldScent(float dt)
    {
        for (int i = WorldScent.Count - 1; i >= 0; i--)
        {
            var puff = WorldScent[i];
            puff.AgeSeconds += dt;

            float lifetime = 25f;
            if (puff.Source is ScentComponent live && GodotObject.IsInstanceValid(live))
                lifetime = live.ScentLifetime;

            if (puff.AgeSeconds >= lifetime)
            {
                WorldScent.RemoveAt(i);
                continue;
            }

            // Strength falls linearly to zero across the puff's lifetime.
            puff.Strength = Mathf.Max(0f, puff.Strength * (1f - dt / Mathf.Max(0.1f, lifetime)));
            WorldScent[i] = puff;
        }
    }

    /// <summary>Remove every scent puff (test isolation / new run).</summary>
    public static void ClearWorldScent() => WorldScent.Clear();

    /// <summary>
    /// Total scent strength at a position. Puffs accumulate, which is how a
    /// trail leads a tracker along its path.
    /// </summary>
    public static float SampleScentStrength(Vector3 position, float radius)
    {
        float total = 0f;
        float radiusSq = radius * radius;

        for (int i = 0; i < WorldScent.Count; i++)
        {
            var puff = WorldScent[i];
            if (puff.Strength <= 0.01f) continue;

            float dx = puff.Position.X - position.X;
            float dz = puff.Position.Z - position.Z;
            if (dx * dx + dz * dz > radiusSq) continue;

            total += puff.Strength;
        }

        return total;
    }

    /// <summary>
    /// The freshest, strongest scent point within a radius — where a tracker
    /// should head next. Returns false when there is nothing to follow.
    /// </summary>
    public static bool TryFindStrongestScent(Vector3 from, float radius,
        out Vector3 position, out float strength)
    {
        position = Vector3.Zero;
        strength = 0f;
        bool found = false;
        float radiusSq = radius * radius;

        for (int i = 0; i < WorldScent.Count; i++)
        {
            var puff = WorldScent[i];
            if (puff.Strength <= strength) continue;

            float dx = puff.Position.X - from.X;
            float dz = puff.Position.Z - from.Z;
            if (dx * dx + dz * dz > radiusSq) continue;

            position = puff.Position;
            strength = puff.Strength;
            found = true;
        }

        return found;
    }
}
