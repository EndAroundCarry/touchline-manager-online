using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Competition;

/// <summary>
/// One round of one division's season: the nine fixtures played together, and the whole unit that locks,
/// resolves, and publishes (`CAL-10`, `MAT-7`).
/// </summary>
/// <remarks>
/// <para>
/// The matchday owns the two deadlines rather than the fixtures doing so individually, because the round
/// is indivisible: every fixture in it kicks off at the same instant, and the publication status is a
/// property of the round rather than of a fixture. A fixture that carried its own lock time could be
/// locked out of step with its round.
/// </para>
/// <para>
/// The lock instant is derived from the kickoff and <see cref="WorldRuleSet.TeamSheetLockMinutes"/>
/// (`CAL-3`) at creation, so the two can never disagree — the database also refuses a lock after the
/// kickoff it belongs to.
/// </para>
/// </remarks>
public sealed class Matchday
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private Matchday()
    {
    }

    /// <summary>Gets the identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the division-season this round belongs to.</summary>
    public Guid DivisionSeasonId { get; private set; }

    /// <summary>Gets the round number, 1-based within the season (`CAL-1`).</summary>
    public int RoundNumber { get; private set; }

    /// <summary>Gets when team sheets lock for this round (`CAL-3`).</summary>
    public DateTimeOffset LockAt { get; private set; }

    /// <summary>Gets when the round kicks off, the same instant for every fixture in it (`CAL-2`).</summary>
    public DateTimeOffset KickoffAt { get; private set; }

    /// <summary>Gets the publication state of the round as a unit (`MAT-7`).</summary>
    public MatchdayPublicationStatus PublicationStatus { get; private set; }

    /// <summary>Gets when the row was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Schedules a round at its kickoff, deriving the lock instant from it (`CAL-3`).</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="divisionSeasonId">The division-season the round belongs to.</param>
    /// <param name="roundNumber">The round number, 1–34.</param>
    /// <param name="kickoffAt">The kickoff instant, in UTC (`CAL-2`).</param>
    /// <param name="now">The current instant.</param>
    public static Matchday Schedule(
        Guid id,
        Guid divisionSeasonId,
        int roundNumber,
        DateTimeOffset kickoffAt,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(roundNumber, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(roundNumber, WorldRuleSet.MatchdaysPerSeason);

        return new Matchday
        {
            Id = id,
            DivisionSeasonId = divisionSeasonId,
            RoundNumber = roundNumber,
            LockAt = kickoffAt.AddMinutes(-WorldRuleSet.TeamSheetLockMinutes),
            KickoffAt = kickoffAt,
            PublicationStatus = MatchdayPublicationStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Marks every fixture staged and validated, which is the gate before publication (`MAT-7`).</summary>
    /// <param name="now">The current instant.</param>
    /// <remarks>
    /// Idempotent: a retried resolution that finds the round already staged does not fail, because the job
    /// that calls this is at-least-once (§7.1).
    /// </remarks>
    public void MarkStaged(DateTimeOffset now)
    {
        if (PublicationStatus == MatchdayPublicationStatus.Staged
            || PublicationStatus == MatchdayPublicationStatus.Published)
        {
            return;
        }

        PublicationStatus = MatchdayPublicationStatus.Staged;

        Touch(now);
    }

    /// <summary>Publishes the whole round. Publication is terminal (`MAT-7`).</summary>
    /// <param name="now">The current instant.</param>
    /// <remarks>
    /// Refuses a round that has not staged, so publication can never run ahead of resolution. Calling it
    /// again on an already-published round is a no-op rather than an error, so a retried publication is
    /// safe.
    /// </remarks>
    public void Publish(DateTimeOffset now)
    {
        if (PublicationStatus == MatchdayPublicationStatus.Published)
        {
            return;
        }

        if (PublicationStatus != MatchdayPublicationStatus.Staged)
        {
            throw new InvalidOperationException(
                "A matchday publishes only once every fixture has staged (MAT-7).");
        }

        PublicationStatus = MatchdayPublicationStatus.Published;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
