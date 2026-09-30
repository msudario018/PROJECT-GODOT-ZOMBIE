namespace ZombieApocalypse.Systems.Infection;

using Godot;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Data;

/// <summary>
/// Stages of a bite infection. The infection advances through these on a timer
/// and is irreversible once it reaches <see cref="Necrosis"/> without surgery.
/// </summary>
public enum InfectionStage
{
    /// <summary>Not infected.</summary>
    Uninfected,

    /// <summary>Fresh bite: no symptoms, timer running.</summary>
    Incubation,

    /// <summary>Fever: health drains and stamina caps lower.</summary>
    Fever,

    /// <summary>Spreading: heavy drain, the bite limb is lost.</summary>
    Infection,

    /// <summary>Terminal: death is imminent.</summary>
    Necrosis
}

/// <summary>Outcome of a bite, including the result of the infection roll.</summary>
public struct BiteResult
{
    public bool Infected;
    public float Roll;              // the raw random value, for tests/telemetry
    public InfectionStage Stage;
    public float IncubationSeconds; // the time this bite will take to mature
}

/// <summary>
/// Bite-infection model: incubation timers, staged debuffs and treatment.
///
/// Attach as a child of any entity that can be bitten. The system owns no
/// visuals; it publishes its state and lets the host (player HUD, survivor AI)
/// decide how to display it.
///
/// Treatment is a three-way choice with a genuine risk/reward curve:
///   - <see cref="ApplyTourniquet"/>  slows the timer and blunts the fever drain
///   - <see cref="ApplyAntibiotics"/> buys real time, but can fail outright
///   - <see cref="Amputate"/>         the only guaranteed cure, at a lasting cost
/// </summary>
public partial class InfectionSystem : Node
{
    [ExportGroup("Infection Odds")]
    /// <summary>Chance a bite infects, before mitigation.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float BaseInfectionChance = 0.55f;
    /// <summary>Seconds from bite to Fever.</summary>
    [Export] public float IncubationDuration = 90f;
    /// <summary>Seconds spent in Fever before the infection spreads.</summary>
    [Export] public float FeverDuration = 60f;
    /// <summary>Seconds spent in Infection before necrosis.</summary>
    [Export] public float SpreadDuration = 45f;

    [ExportGroup("Debuffs")]
    [Export] public float FeverHealthDrainPerSecond = 1.2f;
    [Export] public float InfectionHealthDrainPerSecond = 3.0f;
    [Export] public float NecrosisHealthDrainPerSecond = 8.0f;
    /// <summary>Stamina cap multiplier while fevered (legs hurt).</summary>
    [Export] public float FeverStaminaCapMultiplier = 0.6f;

    [ExportGroup("Treatment")]
    /// <summary>Chance antibiotics fully clear the infection.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float AntibioticCureChance = 0.35f;
    /// <summary>Seconds of incubation bought by one antibiotic dose.</summary>
    [Export] public float AntibioticTimeBonus = 45f;
    /// <summary>How much a tourniquet slows the timer (0-1).</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float TourniquetSlowFactor = 0.5f;
    /// <summary>Maximum number of tourniquets that can be stacked.</summary>
    [Export] public int MaxStackedTourniquets = 2;
    /// <summary>Long-term movement penalty after an amputation (0-1).</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float AmputationSpeedPenalty = 0.15f;

    [Signal] public delegate void StageChangedEventHandler(int newStage);
    [Signal] public delegate void SymptomPulsedEventHandler(int stage);
    [Signal] public delegate void TreatedEventHandler(string treatment, bool success);
    [Signal] public delegate void AmputatedEventHandler();

    /// <summary>Current infection stage.</summary>
    public InfectionStage Stage { get; private set; } = InfectionStage.Uninfected;
    /// <summary>Seconds elapsed in the current stage.</summary>
    public float StageTimer { get; private set; }
    /// <summary>Survival clock from the bite to necrosis, ignoring treatment.</summary>
    public float TotalElapsed { get; private set; }
    /// <summary>Number of tourniquets currently applied.</summary>
    public int TourniquetsApplied { get; private set; }
    /// <summary>True once a limb has been amputated to stop the spread.</summary>
    public bool HasBeenAmputated { get; private set; }
    /// <summary>Amputated limb label, e.g. "left arm".</summary>
    public string AmputatedLimb { get; private set; } = "";

    public bool IsInfected => Stage != InfectionStage.Uninfected;

    /// <summary>1.0 as the infection matures; 0 when clean.</summary>
    public float Severity => Stage switch
    {
        InfectionStage.Incubation => 0.2f,
        InfectionStage.Fever => 0.45f,
        InfectionStage.Infection => 0.75f,
        InfectionStage.Necrosis => 1f,
        _ => 0f
    };

    /// <summary>Health drained per second by the current stage.</summary>
    public float HealthDrainPerSecond => Stage switch
    {
        InfectionStage.Fever => FeverHealthDrainPerSecond,
        InfectionStage.Infection => InfectionHealthDrainPerSecond,
        InfectionStage.Necrosis => NecrosisHealthDrainPerSecond,
        _ => 0f
    };

    /// <summary>Stamina cap multiplier applied by the current stage.</summary>
    public float StaminaCapMultiplier => Stage switch
    {
        InfectionStage.Fever => FeverStaminaCapMultiplier,
        InfectionStage.Infection => 0.4f,
        InfectionStage.Necrosis => 0.25f,
        _ => 1f
    };

    /// <summary>Persistent movement penalty from amputation (0-1).</summary>
    public float MovementSpeedPenalty => HasBeenAmputated ? AmputationSpeedPenalty : 0f;

    /// <summary>
    /// Current rate of timer advance, slowed by applied tourniquets.
    /// 1.0 = real time, 0.5 = half speed.
    /// </summary>
    public float TimerRate =>
        1f - TourniquetSlowFactor * Mathf.Min(TourniquetsApplied, MaxStackedTourniquets);

    /// <summary>
    /// Seconds of untreated time left before necrosis (0 when clean).
    /// Time bought by antibiotics is subtracted, so a fresh bite that is
    /// immediately dosed genuinely survives longer.
    /// </summary>
    public float SecondsUntilNecrosis
    {
        get
        {
            if (!IsInfected) return 0f;
            float total = IncubationDuration + FeverDuration + SpreadDuration;
            return Mathf.Max(0f, total - TotalElapsed + _timeCredits);
        }
    }

    /// <summary>Seconds of survival bought by failed antibiotic doses.</summary>
    private float _timeCredits;

    /// <summary>
    /// Roll for infection on a bite. <paramref name="chanceOverride"/> lets the
    /// caller force a result (tests, scripted bites); null uses the export.
    /// </summary>
    public BiteResult RollBite(float? chanceOverride = null, float? timeBonus = null)
    {
        float chance = Mathf.Clamp(chanceOverride ?? BaseInfectionChance, 0f, 1f);
        float roll = GD.Randf();
        bool infected = roll < chance;

        var result = new BiteResult
        {
            Infected = infected,
            Roll = roll,
            Stage = InfectionStage.Uninfected,
            IncubationSeconds = 0f,
        };

        if (!infected)
        {
            GD.Print($"[InfectionSystem] Bite missed (roll {roll:F2} ≥ {chance:F2}). No infection.");
            return result;
        }

        result.IncubationSeconds = (timeBonus ?? 0f) + IncubationDuration;
        result.Stage = InfectionStage.Incubation;

        Stage = InfectionStage.Incubation;
        StageTimer = 0f;
        TotalElapsed = 0f;
        TourniquetsApplied = 0;
        HasBeenAmputated = false;
        AmputatedLimb = "";
        EmitSignal(SignalName.StageChanged, (int)Stage);

        GD.Print($"[InfectionSystem] Bite infected (roll {roll:F2} < {chance:F2}). " +
                 $"Incubation ~{result.IncubationSeconds:F0}s.");
        return result;
    }

    public override void _Process(double delta)
    {
        if (!IsInfected) return;

        float dt = (float)delta * TimerRate;
        StageTimer += dt;
        TotalElapsed += dt;

        EmitSignal(SignalName.SymptomPulsed, (int)Stage);
        PromoteIfDue();
    }

    /// <summary>Fast-forward the infection clock (tests, time skips, save load).</summary>
    public void AdvanceStageTimer(float seconds)
    {
        if (!IsInfected || seconds <= 0f) return;

        StageTimer += seconds;
        TotalElapsed += seconds;
        PromoteIfDue();
    }

    /// <summary>
    /// Move to the next stage when the current stage's duration has run out.
    /// Loops and carries the overflow forward, so a long time skip (sleep, save
    /// reload, test) lands on the stage the survivor would actually be in rather
    /// than one step further along.
    /// </summary>
    private void PromoteIfDue()
    {
        for (int guard = 0; guard < 8; guard++)
        {
            float duration = Stage switch
            {
                InfectionStage.Incubation => IncubationDuration,
                InfectionStage.Fever => FeverDuration,
                InfectionStage.Infection => SpreadDuration,
                _ => 0f,
            };

            if (Stage == InfectionStage.Necrosis || StageTimer < duration) return;

            // Carry the leftover time into the new stage.
            StageTimer -= duration;
            Stage = Stage switch
            {
                InfectionStage.Incubation => InfectionStage.Fever,
                InfectionStage.Fever => InfectionStage.Infection,
                _ => InfectionStage.Necrosis,
            };

            EmitSignal(SignalName.StageChanged, (int)Stage);
            GD.Print($"[InfectionSystem] Infection advanced to {Stage}.");
        }
    }

    // ── Treatment ─────────────────────────────────────────────────────

    /// <summary>
    /// Apply a tourniquet: slows the infection clock and blunts the fever drain,
    /// but cannot be stacked indefinitely and cures nothing.
    /// </summary>
    public bool ApplyTourniquet()
    {
        if (!IsInfected) return false;
        if (TourniquetsApplied >= MaxStackedTourniquets) return false;

        TourniquetsApplied++;
        EmitSignal(SignalName.Treated, "tourniquet", true);
        GD.Print($"[InfectionSystem] Tourniquet applied ({TourniquetsApplied}/{MaxStackedTourniquets}). " +
                 $"Timer now runs at {TimerRate:P0}.");
        return true;
    }

    /// <summary>
    /// Take antibiotics: usually buys time, occasionally clears the infection.
    /// <paramref name="forceCure"/> makes the outcome deterministic.
    /// </summary>
    public bool ApplyAntibiotics(bool? forceCure = null, float? timeBonus = null)
    {
        if (!IsInfected) return false;

        bool cured = forceCure ?? (GD.Randf() < AntibioticCureChance);
        if (cured)
        {
            ClearInfection();
            EmitSignal(SignalName.Treated, "antibiotics", true);
            GD.Print("[InfectionSystem] Antibiotics cleared the infection.");
            return true;
        }

        // A failed dose does not rewind the clock (there may be nothing to
        // rewind at a fresh bite); it banks extra survival time instead.
        _timeCredits += timeBonus ?? AntibioticTimeBonus;

        EmitSignal(SignalName.Treated, "antibiotics", false);
        GD.Print($"[InfectionSystem] Antibiotics bought time (cure failed). " +
                 $"Necrosis in ~{SecondsUntilNecrosis:F0}s.");
        return true;
    }

    /// <summary>
    /// Surgical amputation of the bitten limb: the only guaranteed cure, at the
    /// cost of a permanent movement penalty.
    /// </summary>
    public bool Amputate(string limb)
    {
        if (!IsInfected) return false;

        ClearInfection();
        HasBeenAmputated = true;
        AmputatedLimb = limb;
        EmitSignal(SignalName.Amputated);
        EmitSignal(SignalName.Treated, "amputation", true);
        GD.Print($"[InfectionSystem] Amputated {limb}. " +
                 $"Movement penalty {MovementSpeedPenalty:P0} is permanent.");
        return true;
    }

    /// <summary>Wipe the infection state (cure, surgery, or a fresh respawn).</summary>
    public void ClearInfection()
    {
        Stage = InfectionStage.Uninfected;
        StageTimer = 0f;
        TotalElapsed = 0f;
        _timeCredits = 0f;
        TourniquetsApplied = 0;
        EmitSignal(SignalName.StageChanged, (int)Stage);
    }

    /// <summary>Consume a treatment item from an inventory and apply it.</summary>
    public bool TreatWith(string itemId, InventoryComponent? inventory, string limb = "wound")
    {
        if (!IsInfected) return false;
        if (inventory != null && !inventory.Has(itemId, 1)) return false;

        bool handled = itemId switch
        {
            "tourniquet" => ApplyTourniquet(),
            "antibiotics" => ApplyAntibiotics(),
            "bone_saw" => Amputate(limb),
            _ => false,
        };

        if (handled) inventory?.TryRemove(itemId, 1);
        return handled;
    }
}
