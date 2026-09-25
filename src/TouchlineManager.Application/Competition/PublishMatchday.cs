using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Domain.Competition;

namespace TouchlineManager.Application.Competition;

/// <summary>What publishing a matchday did.</summary>
public enum PublishMatchdayOutcome
{
    /// <summary>The round's results and its table are public.</summary>
    Published = 0,

    /// <summary>The round was already published; nothing was done.</summary>
    AlreadyPublished = 1,

    /// <summary>The matchday does not exist.</summary>
    MatchdayNotFound = 2,

    /// <summary>At least one fixture has no staged result, so nothing was published.</summary>
    NotFullyStaged = 3,
}

/// <summary>The result of publishing a matchday.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Published">How many fixtures became public.</param>
/// <param name="TableRows">How many table rows the division now carries.</param>
public sealed record PublishMatchdayResult(PublishMatchdayOutcome Outcome, int Published, int TableRows);

/// <summary>
/// Publishes a division's round and applies its projections, all or nothing (`MAT-7`, `TBL-13`, §7.4).
/// </summary>
/// <remarks>
/// <para>
/// The whole point of the staged state is this method. Nine results exist privately, and this is the one
/// transaction in which they become public together and the table moves with them. A failure before the
/// commit leaves the world exactly as it was — nine staged results, no published scoreline, an unmoved
/// table — which is what "never publish five of nine" means in practice (§7.4.7).
/// </para>
/// <para>
/// The table is <em>rebuilt</em> rather than incremented: the division's published results are read back
/// and ranked from scratch, so a row cannot drift away from the fixtures it claims to summarise, and an
/// operator repairing a projection runs the same code path the live publication does (`TBL-13`).
/// </para>
/// <para>
/// Publication is serializable. It reads a whole division's results and rewrites its table, and the
/// concurrency rule names matchday publication explicitly: two transactions that both read the table and
/// wrote it in turn would give the second one a table missing the first one's round (`CONC-2`). A
/// serialization failure is a transient job failure, and the retry re-runs the same deterministic work.
/// </para>
/// </remarks>
public sealed class PublishMatchday
{
    private readonly IMatchdayRepository _matchdays;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public PublishMatchday(
        IMatchdayRepository matchdays,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _matchdays = matchdays;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>Publishes one matchday.</summary>
    /// <param name="matchdayId">The matchday to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<PublishMatchdayResult> ExecuteAsync(
        Guid matchdayId,
        CancellationToken cancellationToken)
    {
        var workload = await _matchdays.LoadMatchdayAsync(matchdayId, cancellationToken);

        if (workload is null)
        {
            return new PublishMatchdayResult(PublishMatchdayOutcome.MatchdayNotFound, 0, 0);
        }

        if (workload.Matchday.PublicationStatus == MatchdayPublicationStatus.Published)
        {
            return new PublishMatchdayResult(PublishMatchdayOutcome.AlreadyPublished, 0, 0);
        }

        if (!workload.AllFixturesStaged)
        {
            // Refused rather than partly published. The resolver's job will finish the round and ask again.
            return new PublishMatchdayResult(PublishMatchdayOutcome.NotFullyStaged, 0, 0);
        }

        var now = _clock.UtcNow;

        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.Serializable,
            cancellationToken);

        var published = 0;

        foreach (var fixture in workload.Fixtures.Where(fixture => fixture.Status == FixtureStatus.Staged))
        {
            fixture.Publish(now);
            published++;
        }

        // Committed before the table is read, so the results this round just published are part of what the
        // rebuild sees. Reading the table's inputs before that would rank a division missing its own round.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var tableRows = await RebuildTableAsync(workload.Matchday.DivisionSeasonId, now, cancellationToken);

        workload.Matchday.Publish(now);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new PublishMatchdayResult(PublishMatchdayOutcome.Published, published, tableRows);
    }

    /// <summary>
    /// Recomputes the division's table from its published results and writes it back (`TBL-13`).
    /// </summary>
    /// <returns>How many rows the table carries.</returns>
    private async Task<int> RebuildTableAsync(
        Guid divisionSeasonId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var source = await _matchdays.LoadTableSourceAsync(divisionSeasonId, cancellationToken);

        if (source is null)
        {
            return 0;
        }

        var lines = StandingsCalculator.Rank(
            source.ClubIds,
            source.Outcomes,
            clubId => StandingsCalculator.DrawKeyOf(source.TieDrawSeed, clubId));

        var stored = (await _matchdays.LoadStandingsAsync(divisionSeasonId, cancellationToken))
            .ToDictionary(standing => standing.ClubId);

        foreach (var line in lines)
        {
            if (stored.TryGetValue(line.ClubId, out var standing))
            {
                standing.Rebuild(line, now);

                continue;
            }

            _matchdays.AddStanding(Standing.Create(Guid.CreateVersion7(), divisionSeasonId, line, now));
        }

        return lines.Count;
    }
}
