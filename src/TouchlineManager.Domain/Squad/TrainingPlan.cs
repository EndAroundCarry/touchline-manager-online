namespace TouchlineManager.Domain.Squad;

/// <summary>The club-wide training emphasis (`TRN-1`).</summary>
public enum TrainingFocus
{
    /// <summary>Spread evenly across every family.</summary>
    Balanced = 0,

    /// <summary>Emphasis on recovering condition and clearing fatigue.</summary>
    Recovery = 1,

    /// <summary>Emphasis on the physical family.</summary>
    Fitness = 2,

    /// <summary>Emphasis on attacking technique.</summary>
    Attacking = 3,

    /// <summary>Emphasis on defensive technique.</summary>
    Defending = 4,

    /// <summary>Emphasis on the technical family.</summary>
    Technical = 5,

    /// <summary>Emphasis on the mental family.</summary>
    Tactical = 6,
}

/// <summary>How hard the club trains.</summary>
/// <remarks>
/// The trade-off — more development against more fatigue and injury risk — is a training rule that
/// arrives with the progression job in a later Stage 4 milestone; this type names and stores the choice.
/// </remarks>
public enum TrainingIntensity
{
    /// <summary>Light: least development, least load.</summary>
    Light = 0,

    /// <summary>Normal.</summary>
    Normal = 1,

    /// <summary>Intense: most development, most load.</summary>
    Intense = 2,
}

/// <summary>Stable codes and storage representation for training focus and intensity.</summary>
public static class TrainingPlans
{
    /// <summary>The longest focus code, so a column can be sized to hold every value.</summary>
    public const int MaxFocusCodeLength = 9;

    /// <summary>The longest intensity code, so a column can be sized to hold every value.</summary>
    public const int MaxIntensityCodeLength = 7;

    /// <summary>Converts a focus to its stable code.</summary>
    /// <param name="focus">The training focus.</param>
    public static string ToCode(this TrainingFocus focus) => focus switch
    {
        TrainingFocus.Balanced => "balanced",
        TrainingFocus.Recovery => "recovery",
        TrainingFocus.Fitness => "fitness",
        TrainingFocus.Attacking => "attacking",
        TrainingFocus.Defending => "defending",
        TrainingFocus.Technical => "technical",
        TrainingFocus.Tactical => "tactical",
        _ => throw new ArgumentOutOfRangeException(nameof(focus), focus, "Unknown training focus."),
    };

    /// <summary>Parses a stable code back to its focus.</summary>
    /// <param name="code">The stable code.</param>
    public static TrainingFocus FromCode(string code) => code switch
    {
        "balanced" => TrainingFocus.Balanced,
        "recovery" => TrainingFocus.Recovery,
        "fitness" => TrainingFocus.Fitness,
        "attacking" => TrainingFocus.Attacking,
        "defending" => TrainingFocus.Defending,
        "technical" => TrainingFocus.Technical,
        "tactical" => TrainingFocus.Tactical,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown training focus code."),
    };

    /// <summary>Converts an intensity to its stable code.</summary>
    /// <param name="intensity">The training intensity.</param>
    public static string ToCode(this TrainingIntensity intensity) => intensity switch
    {
        TrainingIntensity.Light => "light",
        TrainingIntensity.Normal => "normal",
        TrainingIntensity.Intense => "intense",
        _ => throw new ArgumentOutOfRangeException(nameof(intensity), intensity, "Unknown training intensity."),
    };

    /// <summary>Parses a stable code back to its intensity.</summary>
    /// <param name="code">The stable code.</param>
    public static TrainingIntensity IntensityFromCode(string code) => code switch
    {
        "light" => TrainingIntensity.Light,
        "normal" => TrainingIntensity.Normal,
        "intense" => TrainingIntensity.Intense,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown training intensity code."),
    };
}

/// <summary>
/// A club's training plan: its team focus and intensity, effective from a date (`TRN-1`, `TRN-3`).
/// </summary>
/// <remarks>
/// One current plan per club, updated in place with a bumped <see cref="Version"/>. The plan is a
/// versioned row rather than an append-only series because nothing needs the history at MVP; the daily
/// progression job reads the plan in force on the day it runs.
/// </remarks>
public sealed class TrainingPlan
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private TrainingPlan()
    {
    }

    /// <summary>Gets the plan identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owning club.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets the club-wide focus.</summary>
    public TrainingFocus TeamFocus { get; private set; }

    /// <summary>Gets how hard the club trains.</summary>
    public TrainingIntensity Intensity { get; private set; }

    /// <summary>Gets the date the plan takes effect from.</summary>
    public DateOnly EffectiveDate { get; private set; }

    /// <summary>Gets when the plan was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the plan was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Sets a club's training plan.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="clubId">The owning club.</param>
    /// <param name="teamFocus">The club-wide focus.</param>
    /// <param name="intensity">How hard the club trains.</param>
    /// <param name="effectiveDate">The date the plan takes effect from.</param>
    /// <param name="now">The current instant.</param>
    public static TrainingPlan Set(
        Guid id,
        Guid clubId,
        TrainingFocus teamFocus,
        TrainingIntensity intensity,
        DateOnly effectiveDate,
        DateTimeOffset now) => new()
        {
            Id = id,
            ClubId = clubId,
            TeamFocus = teamFocus,
            Intensity = intensity,
            EffectiveDate = effectiveDate,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

    /// <summary>Replaces the focus, intensity, and effective date.</summary>
    /// <param name="teamFocus">The club-wide focus.</param>
    /// <param name="intensity">How hard the club trains.</param>
    /// <param name="effectiveDate">The date the plan takes effect from.</param>
    /// <param name="now">The current instant.</param>
    public void Revise(
        TrainingFocus teamFocus,
        TrainingIntensity intensity,
        DateOnly effectiveDate,
        DateTimeOffset now)
    {
        TeamFocus = teamFocus;
        Intensity = intensity;
        EffectiveDate = effectiveDate;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
