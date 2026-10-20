using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Fans;

/// <summary>
/// A club's fans as they stand now: how many there are, how they feel, and the place they hoped for this season (`FAN-1`, `FAN-8`).
/// </summary>
/// <remarks>
/// One row per club, human or AI, because an opponent's strength and mood feed the expectation and the crowd of every match. The row
/// is only changed through <see cref="Apply"/> with a state the fan rules produced, and every change is written beside it as a
/// <see cref="ClubFanEvent"/> so the history the charts draw can be rebuilt from the events alone.
/// </remarks>
public sealed class ClubFanBase
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private ClubFanBase()
    {
    }

    /// <summary>Gets the club whose fans these are. It is the key: a club has exactly one fan base.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets the fans the club has.</summary>
    public int Fans { get; private set; }

    /// <summary>Gets how satisfied they are, from 0 (furious) to 10,000 (euphoric), in basis points.</summary>
    public int MoodBp { get; private set; }

    /// <summary>
    /// Gets the place the fans hope the club finishes this season, or null before it has been set (`FAN-8`). Until season objectives
    /// exist it is the club's rank by squad strength in its division when the season started.
    /// </summary>
    public int? SeasonTargetRank { get; private set; }

    /// <summary>Gets where the club stood against the target at the last weekly review (projected place minus target), or null before the first.</summary>
    public int? LastWeekGap { get; private set; }

    /// <summary>Gets the fan rule set version that wrote the row.</summary>
    public string RuleSetVersion { get; private set; } = string.Empty;

    /// <summary>Gets when the row was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Gets the fans as the rules read them.</summary>
    public FanBaseState State => new(Fans, MoodBp, LastWeekGap);

    /// <summary>Opens a club's fans.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="state">The opening numbers, <see cref="FanRuleSet.OpeningFor"/> for the club's tier.</param>
    /// <param name="now">The current instant.</param>
    public static ClubFanBase Open(Guid clubId, FanBaseState state, DateTimeOffset now)
    {
        Validate(state);

        return new ClubFanBase
        {
            ClubId = clubId,
            Fans = state.Fans,
            MoodBp = state.MoodBp,
            LastWeekGap = state.LastWeekGap,
            RuleSetVersion = FanRuleSet.Version,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Replaces the fans' numbers with the ones a fan rule produced.</summary>
    /// <param name="state">The new numbers.</param>
    /// <param name="now">The current instant.</param>
    public void Apply(FanBaseState state, DateTimeOffset now)
    {
        Validate(state);

        Fans = state.Fans;
        MoodBp = state.MoodBp;
        LastWeekGap = state.LastWeekGap;
        RuleSetVersion = FanRuleSet.Version;
        Touch(now);
    }

    /// <summary>Sets the place the fans hope for this season.</summary>
    /// <param name="rank">The target place, from 1, or null to clear it.</param>
    /// <param name="now">The current instant.</param>
    public void SetTarget(int? rank, DateTimeOffset now)
    {
        if (rank is { } place)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(place, 1);
        }

        SeasonTargetRank = rank;
        Touch(now);
    }

    private static void Validate(FanBaseState state)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(state.Fans, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(state.MoodBp);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(state.MoodBp, FanRuleSet.MoodMaxBp);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
