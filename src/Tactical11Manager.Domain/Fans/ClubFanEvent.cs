namespace Tactical11Manager.Domain.Fans;

/// <summary>
/// One change to a club's fans, kept for ever: the history the Fan Base page draws its charts from (`FAN-10`).
/// </summary>
/// <remarks>
/// Append-only. A takeover, a new season or a correction writes a new row and never edits an old one, so the page can show the whole
/// club history with a marker where a manager took over, or only the current manager's.
/// </remarks>
public sealed class ClubFanEvent
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private ClubFanEvent()
    {
    }

    /// <summary>Gets the event identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the club whose fans changed.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets the season the change belongs to.</summary>
    public Guid SeasonId { get; private set; }

    /// <summary>Gets when the change was made.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Gets what moved the fans.</summary>
    public FanEventKind Kind { get; private set; }

    /// <summary>Gets the tenure that began, set on a takeover only.</summary>
    public Guid? TenureId { get; private set; }

    /// <summary>Gets the fixture, set on a match only.</summary>
    public Guid? FixtureId { get; private set; }

    /// <summary>Gets the Sunday of the week reviewed, set on a weekly review only. A club has one review a week.</summary>
    public DateOnly? WeekOf { get; private set; }

    /// <summary>Gets the fans before the change.</summary>
    public int FansBefore { get; private set; }

    /// <summary>Gets the fans after the change.</summary>
    public int FansAfter { get; private set; }

    /// <summary>Gets the mood before the change, in basis points.</summary>
    public int MoodBeforeBp { get; private set; }

    /// <summary>Gets the mood after the change, in basis points.</summary>
    public int MoodAfterBp { get; private set; }

    /// <summary>Gets the points the fans expected, in hundredths, set on a match only.</summary>
    public int? ExpectedPointsCentis { get; private set; }

    /// <summary>Gets the points the club won, set on a match only.</summary>
    public int? PointsWon { get; private set; }

    /// <summary>Gets the club's place minus the target place, set on a weekly review and a season reset.</summary>
    public int? Gap { get; private set; }

    /// <summary>Records a change.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="clubId">The club.</param>
    /// <param name="seasonId">The season.</param>
    /// <param name="occurredAt">When the change was made.</param>
    /// <param name="kind">What moved the fans.</param>
    /// <param name="before">The fans before.</param>
    /// <param name="after">The fans after.</param>
    /// <param name="tenureId">The tenure, on a takeover.</param>
    /// <param name="fixtureId">The fixture, on a match.</param>
    /// <param name="weekOf">The Sunday reviewed, on a weekly review.</param>
    /// <param name="expectedPointsCentis">The points expected, on a match.</param>
    /// <param name="pointsWon">The points won, on a match.</param>
    /// <param name="gap">The gap to the target, on a review or a season reset.</param>
    public static ClubFanEvent Record(
        Guid id,
        Guid clubId,
        Guid seasonId,
        DateTimeOffset occurredAt,
        FanEventKind kind,
        FanBaseState before,
        FanBaseState after,
        Guid? tenureId = null,
        Guid? fixtureId = null,
        DateOnly? weekOf = null,
        int? expectedPointsCentis = null,
        int? pointsWon = null,
        int? gap = null) =>
        new()
        {
            Id = id,
            ClubId = clubId,
            SeasonId = seasonId,
            OccurredAt = occurredAt,
            Kind = kind,
            TenureId = tenureId,
            FixtureId = fixtureId,
            WeekOf = weekOf,
            FansBefore = before.Fans,
            FansAfter = after.Fans,
            MoodBeforeBp = before.MoodBp,
            MoodAfterBp = after.MoodBp,
            ExpectedPointsCentis = expectedPointsCentis,
            PointsWon = pointsWon,
            Gap = gap,
        };
}
