namespace TouchlineManager.Domain.Competition;

/// <summary>
/// One match between two clubs on one matchday: the unit a snapshot is taken for and a result is published
/// against (master plan §6.4).
/// </summary>
/// <remarks>
/// <para>
/// A fixture moves through <see cref="FixtureStatus"/> exactly once, in order, and the score exists only
/// in the last two states. The aggregate guards every transition — a scheduled fixture cannot be staged, a
/// published one cannot be re-staged — because the workflow that drives it (lock, resolve, publish) runs
/// from a durable job that may be retried at any boundary (§7.4, ADR-0003), and a retry must find an
/// already-done transition harmless rather than an illegal one.
/// </para>
/// <para>
/// The kickoff is denormalized from the matchday deliberately: the fixture list and the countdown both
/// read it, and a fixture's own row is what a list query wants. It is written once, from the matchday's
/// kickoff, and never moved — `CAL-11` forbids shifting a scheduled kickoff because simulation was late.
/// </para>
/// </remarks>
public sealed class Fixture
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private Fixture()
    {
    }

    /// <summary>Gets the identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the matchday (round) the fixture belongs to.</summary>
    public Guid MatchdayId { get; private set; }

    /// <summary>Gets the home club.</summary>
    public Guid HomeClubId { get; private set; }

    /// <summary>Gets the away club.</summary>
    public Guid AwayClubId { get; private set; }

    /// <summary>Gets the kickoff instant, in UTC (`CAL-2`, `CAL-11`).</summary>
    public DateTimeOffset KickoffAt { get; private set; }

    /// <summary>Gets the lifecycle state.</summary>
    public FixtureStatus Status { get; private set; }

    /// <summary>Gets the home score, present only once the result is staged or published.</summary>
    public int? HomeScore { get; private set; }

    /// <summary>Gets the away score, present only once the result is staged or published.</summary>
    public int? AwayScore { get; private set; }

    /// <summary>Gets the simulated match, present once the result is staged.</summary>
    public Guid? MatchId { get; private set; }

    /// <summary>Gets when the result became public.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Gets when the row was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Schedules a fixture.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="matchdayId">The matchday the fixture belongs to.</param>
    /// <param name="homeClubId">The home club.</param>
    /// <param name="awayClubId">The away club, which must differ from the home club.</param>
    /// <param name="kickoffAt">The kickoff, in UTC; the matchday's own kickoff (`CAL-2`).</param>
    /// <param name="now">The current instant.</param>
    public static Fixture Schedule(
        Guid id,
        Guid matchdayId,
        Guid homeClubId,
        Guid awayClubId,
        DateTimeOffset kickoffAt,
        DateTimeOffset now)
    {
        if (homeClubId == awayClubId)
        {
            throw new ArgumentException("A club cannot play itself.", nameof(awayClubId));
        }

        return new Fixture
        {
            Id = id,
            MatchdayId = matchdayId,
            HomeClubId = homeClubId,
            AwayClubId = awayClubId,
            KickoffAt = kickoffAt,
            Status = FixtureStatus.Scheduled,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Marks the fixture locked once its snapshot is frozen (`CAL-3`, §7.3).</summary>
    /// <param name="now">The current instant.</param>
    public void Lock(DateTimeOffset now)
    {
        if (Status != FixtureStatus.Scheduled)
        {
            throw new InvalidOperationException(
                $"Only a scheduled fixture can lock, not one in state '{Status}'.");
        }

        Status = FixtureStatus.Locked;

        Touch(now);
    }

    /// <summary>Marks a simulation attempt under way.</summary>
    /// <param name="now">The current instant.</param>
    public void BeginSimulation(DateTimeOffset now)
    {
        if (Status != FixtureStatus.Locked)
        {
            throw new InvalidOperationException(
                $"Only a locked fixture can be simulated, not one in state '{Status}'.");
        }

        Status = FixtureStatus.Simulating;

        Touch(now);
    }

    /// <summary>Stages a simulated result without publishing it (`MAT-7`).</summary>
    /// <param name="homeScore">The home score, zero or more.</param>
    /// <param name="awayScore">The away score, zero or more.</param>
    /// <param name="matchId">The simulated match.</param>
    /// <param name="now">The current instant.</param>
    /// <remarks>
    /// Accepts a fixture that is locked or simulating, and does nothing if the same result is already
    /// staged, so a retried simulation is idempotent rather than an error.
    /// </remarks>
    public void Stage(int homeScore, int awayScore, Guid matchId, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(homeScore);
        ArgumentOutOfRangeException.ThrowIfNegative(awayScore);

        if (matchId == Guid.Empty)
        {
            throw new ArgumentException("A staged result carries its match.", nameof(matchId));
        }

        if (Status is FixtureStatus.Staged or FixtureStatus.Published)
        {
            return;
        }

        if (Status is not (FixtureStatus.Locked or FixtureStatus.Simulating))
        {
            throw new InvalidOperationException(
                $"Only a locked or simulating fixture can be staged, not one in state '{Status}'.");
        }

        HomeScore = homeScore;
        AwayScore = awayScore;
        MatchId = matchId;
        Status = FixtureStatus.Staged;

        Touch(now);
    }

    /// <summary>Publishes the staged result, which is terminal (`MAT-7`).</summary>
    /// <param name="now">The current instant.</param>
    public void Publish(DateTimeOffset now)
    {
        if (Status == FixtureStatus.Published)
        {
            return;
        }

        if (Status != FixtureStatus.Staged)
        {
            throw new InvalidOperationException(
                $"Only a staged fixture can publish, not one in state '{Status}'.");
        }

        Status = FixtureStatus.Published;
        PublishedAt = now;

        Touch(now);
    }

    /// <summary>Voids the fixture under an operator reason (`MAT-10`).</summary>
    /// <param name="reason">Why it was voided; recorded by the caller in the audit log.</param>
    /// <param name="now">The current instant.</param>
    /// <remarks>
    /// A void holds no result, so a fixture that had staged one loses it. Publication is terminal, so a
    /// published fixture is refused rather than silently un-published.
    /// </remarks>
    public void Void(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Status == FixtureStatus.Published)
        {
            throw new InvalidOperationException("A published fixture cannot be voided; it is history (MAT-10).");
        }

        if (Status == FixtureStatus.Void)
        {
            return;
        }

        Status = FixtureStatus.Void;
        HomeScore = null;
        AwayScore = null;
        MatchId = null;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
