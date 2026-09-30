namespace ZombieApocalypse.Entities.Zombies.Archetypes;

using Godot;

/// <summary>
/// Phase 8 archetype: Screamer / Siren.
///
/// Profile:
///   - Skittish and evasive: keeps its distance, backs away when cornered.
///   - Its real weapon is the scream: spotting a target alerts every zombie in
///     the sector instead of chasing alone, turning one spotted survivor into a
///     camp-wide emergency.
///   - Fragile (70 HP) and slow, so a player who closes the gap wins easily —
///     which is the counterplay. Kill it first or the sector is lost.
/// </summary>
public partial class ScreamerSiren : ZombieBase
{
    [ExportGroup("Scream")]
    /// <summary>Radius the alert propagates to.</summary>
    [Export] public float AlertRadius = 45f;
    /// <summary>Seconds between screams while a target is visible.</summary>
    [Export] public float ScreamCooldown = 8f;
    /// <summary>How long an alerted zombie stays hunting.</summary>
    [Export] public float AlertDuration = 20f;

    [ExportGroup("Evasion")]
    /// <summary>Distance the siren tries to keep from a spotted target.</summary>
    [Export] public float PreferredDistance = 9f;
    /// <summary>Below this range it flees instead of holding ground.</summary>
    [Export] public float FleeDistance = 4.5f;

    /// <summary>How many zombies the last scream alerted.</summary>
    public int LastAlertCount { get; private set; }
    /// <summary>Seconds until this siren can scream again.</summary>
    public float ScreamCooldownRemaining { get; private set; }
    /// <summary>True while a target is spotted and the siren is close enough to see it.</summary>
    public bool IsScreaming { get; private set; }
    /// <summary>Position of the last scream (tests / HUD threat readout).</summary>
    public Vector3 LastScreamPosition { get; private set; }

    public override void _Ready()
    {
        ArchetypeName = "Screamer";
        MoveSpeed = 2.6f;              // slower than a shambler on purpose
        AttackDamage = 8.0f;           // a bite, not a threat
        AttackRange = 1.4f;
        AttackCooldown = 1.8f;
        TurnSpeed = 10f;
        HordePressureForce = 0.5f;     // contributes little to a breach

        base._Ready();

        Health.SetMaxHealth(70.0f, true);

        if (Sensory != null)
        {
            // Excellent hearing, mediocre sight: it finds you before you see it.
            Sensory.SightRange = 20.0f;
            Sensory.SightAngleDeg = 130.0f;
            Sensory.HearingSensitivity = 2.0f;
            Sensory.ScentRange = 0f;    // alerts rather than tracks
        }

        AddToGroup("screamers");
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        if (IsDead) return;

        float dt = (float)delta;
        ScreamCooldownRemaining = Mathf.Max(0f, ScreamCooldownRemaining - dt);

        // A partially constructed siren may have no sensory node yet.
        bool seesTarget = GetNodeOrNull<ZombieAI.SensorySystem>("SensorySystem")?.HasTarget ?? false;
        if (seesTarget && ScreamCooldownRemaining <= 0f)
            Scream();
    }

    /// <summary>
    /// Emit the sector alert: a loud voice event plus a direct alert to every
    /// zombie in range. Returns how many zombies were alerted.
    /// </summary>
    public int Scream()
    {
        if (IsDead) return 0;

        ScreamCooldownRemaining = ScreamCooldown;
        IsScreaming = true;
        LastScreamPosition = GlobalPosition;

        // A loud voice: AcousticPropagation wakes the local zombies even without
        // the direct alert below.
        AudioEmitter?.EmitZombieVoice(true);

        int alerted = AlertSector();
        GD.Print($"[ScreamerSiren] SCREAM at {GlobalPosition:F1} — alerted {alerted} zombie(s) " +
                 $"within {AlertRadius:F0}m.");
        return alerted;
    }

    /// <summary>Wake every other zombie inside <see cref="AlertRadius"/>.</summary>
    private int AlertSector()
    {
        int alerted = 0;
        var tree = GetTree();

        foreach (var node in tree.GetNodesInGroup("zombies"))
        {
            if (node is not ZombieBase other || other == this) continue;
            if (!GodotObject.IsInstanceValid(other) || other.IsDead) continue;
            if (other.GlobalPosition.DistanceTo(GlobalPosition) > AlertRadius) continue;

            // Point them at whatever the siren saw; if it saw nothing, they get
            // the siren's own position, which is still a useful rally point.
            Vector3 seen = Sensory?.LastKnownTargetPosition ?? Vector3.Zero;
            Vector3 rallyPoint = seen != Vector3.Zero ? seen : GlobalPosition;

            other.AlertTo(rallyPoint, AlertDuration);
            alerted++;
        }

        LastAlertCount = alerted;
        return alerted;
    }

    /// <summary>Stop screaming (called by states when the target is lost).</summary>
    public void StopScreaming() => IsScreaming = false;

    /// <summary>
    /// Movement advice for the AI states: back away when the target is too close
    /// and circle at range otherwise. Returns the desired velocity.
    /// </summary>
    public Vector3 GetEvasiveVelocity(Vector3 targetPosition, float delta)
    {
        Vector3 away = GlobalPosition - targetPosition;
        away.Y = 0f;

        float distance = away.Length();
        if (distance < 0.01f) return Vector3.Zero;

        away /= distance;

        // Too close: full sprint directly away. Otherwise hold at range.
        if (distance < FleeDistance)
        {
            float speed = EffectiveMoveSpeed * 1.25f;
            FaceDirection(-away, delta);
            return new Vector3(away.X * speed, Velocity.Y, away.Z * speed);
        }

        float scale = distance < PreferredDistance ? 0.8f : 0.25f;
        Vector3 strafe = new Vector3(-away.Z, 0f, away.X);   // circle-strafe
        Vector3 desired = (away * scale + strafe * 0.7f).Normalized();

        float pace = EffectiveMoveSpeed * 0.7f;
        FaceDirection(desired, delta);
        return new Vector3(desired.X * pace, Velocity.Y, desired.Z * pace);
    }
}
