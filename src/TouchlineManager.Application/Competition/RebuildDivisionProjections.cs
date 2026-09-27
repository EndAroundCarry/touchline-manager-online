using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Competition;

namespace TouchlineManager.Application.Competition;

/// <summary>What reconciling a division's projections found, and what was done about it.</summary>
public enum ProjectionRebuildOutcome
{
    /// <summary>No division-season exists with the requested identity.</summary>
    DivisionSeasonNotFound = 0,

    /// <summary>The stored projections already equal what the published results compute.</summary>
    Reconciled = 1,

    /// <summary>A dry run found drift; nothing was written.</summary>
    DriftDetected = 2,

    /// <summary>Drift was found and the projections were rebuilt to the computed values.</summary>
    Rebuilt = 3,
}

/// <summary>The result of a projection rebuild (`TBL-13`, master plan §7.2).</summary>
/// <param name="Outcome">What the reconciliation found and did.</param>
/// <param name="StandingsChecked">How many clubs the table was checked against.</param>
/// <param name="StandingsDrifted">How many table rows differed or were missing.</param>
/// <param name="PlayerStatsChecked">How many player lines the published results compute.</param>
/// <param name="PlayerStatsDrifted">How many stored player lines differed, were missing, or had no source.</param>
/// <param name="Applied">Whether the projections were written back.</param>
public sealed record ProjectionRebuildResult(
    ProjectionRebuildOutcome Outcome,
    int StandingsChecked,
    int StandingsDrifted,
    int PlayerStatsChecked,
    int PlayerStatsDrifted,
    bool Applied)
{
    /// <summary>Gets whether anything differed from what the published results compute.</summary>
    public bool HasDrift => StandingsDrifted > 0 || PlayerStatsDrifted > 0;
}

/// <summary>
/// Reconciles, and optionally rebuilds, a division-season's projections from its published results
/// (`TBL-13`, `STA-1`, master plan §7.2).
/// </summary>
/// <remarks>
/// <para>
/// The table and the players' season statistics are caches of facts the world already stores — the published
/// fixtures and the match results and events they were played from — so a projection that has drifted is
/// repaired by recomputing it rather than by editing a column. This runs the <em>same</em> arithmetic the
/// live publication runs: <see cref="StandingsCalculator.Rank"/> over the division's published outcomes for
/// the table, and <see cref="SeasonStatisticsCalculator"/> folded with
/// <see cref="SeasonStatisticsCalculator.Aggregate"/> over every published result for the statistics. A
/// repair therefore cannot introduce an answer the live path would not have produced, which is what `TBL-13`
/// asks and what the exit criterion "rebuilt tables/stats equal live projections" means.
/// </para>
/// <para>
/// <paramref name="apply"/> chooses the two tools apart. Left false it is the reconciliation read: it
/// reports what differs — a row that drifted, a line missing, a line no published result supports — and
/// writes nothing, which is what an operator inspects before authorising a repair. Set true it reads and
/// rewrites inside one serializable transaction, the isolation and the read-inside-the-transaction shape the
/// publication uses, because it recomputes a whole division and a peer publication must not interleave
/// between the read and the write.
/// </para>
/// <para>
/// The discipline accumulation is deliberately not rebuilt here. It is derived from the same events, but its
/// consequence is a suspension already served against specific fixtures, and replaying the accumulation
/// without replaying the service would leave a player's bookings and their absences disagreeing. That
/// reconciliation belongs with the season rollover that owns the accumulation's reset (`DIS-3`), and it is
/// recorded as deferred rather than half-built.
/// </para>
/// </remarks>
public sealed class RebuildDivisionProjections
{
    private readonly IMatchdayRepository _matchdays;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public RebuildDivisionProjections(
        IMatchdayRepository matchdays,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _matchdays = matchdays;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>Reconciles a division-season's projections, and rebuilds them when asked to.</summary>
    /// <param name="divisionSeasonId">The division-season whose projections are checked.</param>
    /// <param name="apply">Whether to write the computed values back, or only report the drift.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ProjectionRebuildResult> ExecuteAsync(
        Guid divisionSeasonId,
        bool apply,
        CancellationToken cancellationToken)
    {
        if (divisionSeasonId == Guid.Empty)
        {
            throw new ArgumentException("A projection rebuild names a division-season.", nameof(divisionSeasonId));
        }

        if (!apply)
        {
            var readOnly = await LoadAsync(divisionSeasonId, cancellationToken);

            return readOnly is null
                ? new ProjectionRebuildResult(
                    ProjectionRebuildOutcome.DivisionSeasonNotFound,
                    0,
                    0,
                    0,
                    0,
                    Applied: false)
                : Describe(readOnly, applied: false);
        }

        // Read inside the transaction, so the drift the rebuild corrects is the drift that exists at the
        // moment it is written, not one a concurrent publication has since invalidated.
        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.Serializable,
            cancellationToken);

        var reconciliation = await LoadAsync(divisionSeasonId, cancellationToken);

        if (reconciliation is null)
        {
            return new ProjectionRebuildResult(
                ProjectionRebuildOutcome.DivisionSeasonNotFound,
                0,
                0,
                0,
                0,
                Applied: false);
        }

        if (!reconciliation.HasDrift)
        {
            // Nothing to write; the transaction is disposed without committing.
            return Describe(reconciliation, applied: false);
        }

        var now = _clock.UtcNow;

        foreach (var line in reconciliation.ExpectedTable)
        {
            if (reconciliation.StoredStandings.TryGetValue(line.ClubId, out var standing))
            {
                standing.Rebuild(line, now);

                continue;
            }

            _matchdays.AddStanding(Standing.Create(Guid.CreateVersion7(), divisionSeasonId, line, now));
        }

        foreach (var line in reconciliation.ExpectedStats)
        {
            if (reconciliation.StoredStats.TryGetValue((line.PlayerId, line.ClubId), out var stat))
            {
                stat.Rebuild(line, now);

                continue;
            }

            _matchdays.AddPlayerSeasonStat(
                PlayerSeasonStat.Create(Guid.CreateVersion7(), divisionSeasonId, line, now));
        }

        // A stored line no published result supports is a projection of a fact that is no longer true, so the
        // rebuild removes it: a cache is left equal to its source, not merely close to it (master plan §5).
        foreach (var key in reconciliation.StoredStats.Keys.Where(key => !reconciliation.SupportedStats.Contains(key)))
        {
            _matchdays.RemovePlayerSeasonStat(reconciliation.StoredStats[key]);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Describe(reconciliation, applied: true);
    }

    private static ProjectionRebuildResult Describe(Reconciliation reconciliation, bool applied) =>
        new(
            reconciliation.HasDrift
                ? applied ? ProjectionRebuildOutcome.Rebuilt : ProjectionRebuildOutcome.DriftDetected
                : ProjectionRebuildOutcome.Reconciled,
            reconciliation.ExpectedTable.Count,
            reconciliation.StandingsDrifted,
            reconciliation.ExpectedStats.Count,
            reconciliation.PlayerStatsDrifted,
            applied);

    /// <summary>Loads a division-season's projections and what the published results compute for them.</summary>
    private async Task<Reconciliation?> LoadAsync(Guid divisionSeasonId, CancellationToken cancellationToken)
    {
        var source = await _matchdays.LoadTableSourceAsync(divisionSeasonId, cancellationToken);

        if (source is null)
        {
            return null;
        }

        var expectedTable = StandingsCalculator.Rank(
            source.ClubIds,
            source.Outcomes,
            clubId => StandingsCalculator.DrawKeyOf(source.TieDrawSeed, clubId));

        var storedStandings = (await _matchdays.LoadStandingsAsync(divisionSeasonId, cancellationToken))
            .ToDictionary(standing => standing.ClubId);

        var standingsDrifted = expectedTable.Count(line =>
            !storedStandings.TryGetValue(line.ClubId, out var standing) || StandingsDiffer(standing, line));

        var expectedStats = SeasonStatisticsCalculator.Aggregate(
            SeasonStatisticsCalculator.Calculate(
                await _matchdays.LoadDivisionMatchLoadsAsync(divisionSeasonId, cancellationToken),
                await _matchdays.LoadDivisionStatEventsAsync(divisionSeasonId, cancellationToken)));

        var storedStats = (await _matchdays.LoadDivisionPlayerSeasonStatsAsync(divisionSeasonId, cancellationToken))
            .ToDictionary(stat => (stat.PlayerId, stat.ClubId));

        var supportedStats = expectedStats
            .Select(line => (line.PlayerId, line.ClubId))
            .ToHashSet();

        var playerStatsDrifted =
            expectedStats.Count(line =>
                !storedStats.TryGetValue((line.PlayerId, line.ClubId), out var stat) || StatsDiffer(stat, line))
            + storedStats.Keys.Count(key => !supportedStats.Contains(key));

        return new Reconciliation(
            expectedTable,
            storedStandings,
            standingsDrifted,
            expectedStats,
            storedStats,
            supportedStats,
            playerStatsDrifted);
    }

    private static bool StandingsDiffer(Standing standing, StandingLine line) =>
        standing.Played != line.Played
        || standing.Won != line.Won
        || standing.Drawn != line.Drawn
        || standing.Lost != line.Lost
        || standing.GoalsFor != line.GoalsFor
        || standing.GoalsAgainst != line.GoalsAgainst
        || standing.Points != line.Points
        || standing.YellowCards != line.YellowCards
        || standing.RedCards != line.RedCards
        || standing.Rank != line.Rank;

    private static bool StatsDiffer(PlayerSeasonStat stat, PlayerSeasonStatLine line) =>
        stat.Appearances != line.Appearances
        || stat.Starts != line.Starts
        || stat.MinutesPlayed != line.MinutesPlayed
        || stat.Goals != line.Goals
        || stat.Assists != line.Assists
        || stat.Shots != line.Shots
        || stat.ShotsOnTarget != line.ShotsOnTarget
        || stat.Saves != line.Saves
        || stat.YellowCards != line.YellowCards
        || stat.RedCards != line.RedCards
        || stat.RatingBasisPointsTotal != line.RatingBasisPointsTotal
        || stat.RatedAppearances != line.RatedAppearances;

    private sealed record Reconciliation(
        IReadOnlyList<StandingLine> ExpectedTable,
        Dictionary<Guid, Standing> StoredStandings,
        int StandingsDrifted,
        IReadOnlyList<PlayerSeasonStatLine> ExpectedStats,
        Dictionary<(Guid PlayerId, Guid ClubId), PlayerSeasonStat> StoredStats,
        HashSet<(Guid PlayerId, Guid ClubId)> SupportedStats,
        int PlayerStatsDrifted)
    {
        public bool HasDrift => StandingsDrifted > 0 || PlayerStatsDrifted > 0;
    }
}
