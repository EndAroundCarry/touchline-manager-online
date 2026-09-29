using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.Competition;

/// <summary>One active tier of the closing season, as the rollover reads it (`PR-5`).</summary>
/// <param name="TierNumber">The tier's number, ascending down the pyramid.</param>
/// <param name="DivisionId">The durable division.</param>
/// <param name="DivisionDisplayName">The name managers see.</param>
/// <param name="DivisionSeason">The division's instance in the closing season.</param>
/// <param name="RankedClubIds">The tier's clubs, best first, exactly as the finalized table ranks them (`TBL-12`).</param>
/// <param name="Entries">The division-season's club entries, closed by the finalize phase (`PR-4`).</param>
public sealed record RolloverTier(
    int TierNumber,
    Guid DivisionId,
    string DivisionDisplayName,
    DivisionSeason DivisionSeason,
    IReadOnlyList<Guid> RankedClubIds,
    IReadOnlyList<ClubSeasonEntry> Entries)
{
    /// <summary>The 1-based final position of a club, from the row the projection stored (`TBL-12`).</summary>
    public int RankOf(Guid clubId)
    {
        for (var index = 0; index < RankedClubIds.Count; index++)
        {
            if (RankedClubIds[index] == clubId)
            {
                return index + 1;
            }
        }

        return 0;
    }
}

/// <summary>One country's active tiers in the closing season (`PR-1`).</summary>
/// <param name="CountryId">The country.</param>
/// <param name="Code">The country's code.</param>
/// <param name="DisplayName">The country's name.</param>
/// <param name="Tiers">The country's active tiers, ascending by tier.</param>
public sealed record RolloverCountry(
    Guid CountryId,
    string Code,
    string DisplayName,
    IReadOnlyList<RolloverTier> Tiers);

/// <summary>The closing season's shape (`PR-5`).</summary>
/// <param name="Countries">Every country, each with its active tiers, signed order, and entries.</param>
public sealed record SeasonRolloverPlan(IReadOnlyList<RolloverCountry> Countries);

/// <summary>One division-season's reconciliation read, for preflight (`TBL-13`).</summary>
/// <param name="TierNumber">The tier.</param>
/// <param name="DivisionSeasonId">The division's instance in the closing season.</param>
/// <param name="Result">What the dry-run reconciliation found; nothing was written.</param>
public sealed record DivisionReconciliation(
    int TierNumber,
    Guid DivisionSeasonId,
    ProjectionRebuildResult Result);

/// <summary>
/// Reads the closing season's shape for the season rollover (`PR-5`, master plan §7.5).
/// </summary>
/// <remarks>
/// Shared by the rollover machine and its operator preview, so a dry run and the run it previews read the
/// same plan from the same queries and cannot drift (ADR-0031, ADR-0034). It reads only; it stages nothing.
/// </remarks>
public sealed class SeasonRolloverPlanLoader
{
    private readonly IWorldRepository _world;
    private readonly IMatchdayRepository _matchdays;
    private readonly RebuildDivisionProjections _rebuild;

    /// <summary>Initializes the loader.</summary>
    public SeasonRolloverPlanLoader(
        IWorldRepository world,
        IMatchdayRepository matchdays,
        RebuildDivisionProjections rebuild)
    {
        _world = world;
        _matchdays = matchdays;
        _rebuild = rebuild;
    }

    /// <summary>Reads the closing season's shape: every active tier with its entries and final ordering.</summary>
    /// <param name="world">The world whose season is closing.</param>
    /// <param name="season">The closing season.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">An active division has no instance in the season.</exception>
    public async Task<SeasonRolloverPlan> LoadAsync(
        GameWorld world,
        Season season,
        CancellationToken cancellationToken)
    {
        var countries = await _world.ListCountriesAsync(world.Id, cancellationToken);

        // Every division-season of the closing season, keyed by their durable division, so each tier's
        // instance is one lookup rather than a query per tier.
        var divisionSeasons = (await _world.ListDivisionSeasonsAsync(season.Id, cancellationToken))
            .ToDictionary(divisionSeason => divisionSeason.DivisionId);

        var plan = new List<RolloverCountry>(countries.Count);

        foreach (var country in countries)
        {
            var divisions = await _world.ListActiveDivisionsAsync(country.Id, cancellationToken);
            var tiers = new List<RolloverTier>(divisions.Count);

            foreach (var division in divisions)
            {
                if (!divisionSeasons.TryGetValue(division.Id, out var divisionSeason))
                {
                    throw new InvalidOperationException(
                        $"Active division {division.Id} has no instance in season {season.Id} (PR-5).");
                }

                var ranked = (await _matchdays.LoadStandingsAsync(divisionSeason.Id, cancellationToken))
                    .OrderBy(standing => standing.Rank)
                    .Select(standing => standing.ClubId)
                    .ToList();

                var entries = await _world.ListClubSeasonEntriesAsync(divisionSeason.Id, cancellationToken);

                tiers.Add(new RolloverTier(
                    division.TierNumber,
                    division.Id,
                    division.DisplayName,
                    divisionSeason,
                    ranked,
                    entries));
            }

            plan.Add(new RolloverCountry(country.Id, country.Code, country.DisplayName, tiers));
        }

        return new SeasonRolloverPlan(plan);
    }

    /// <summary>Counts the closing season's rounds that are not yet published (`PR-4`).</summary>
    /// <param name="plan">The plan to inspect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<int> CountUnpublishedAsync(
        SeasonRolloverPlan plan,
        CancellationToken cancellationToken)
    {
        var count = 0;

        foreach (var tier in plan.Countries.SelectMany(country => country.Tiers))
        {
            var matchdays = await _matchdays.LoadDivisionMatchdaysAsync(tier.DivisionSeason.Id, cancellationToken);

            count += matchdays.Count(matchday => matchday.PublicationStatus != MatchdayPublicationStatus.Published);
        }

        return count;
    }

    /// <summary>Reconciles every division's projections, reporting the ones that have drifted (`TBL-13`).</summary>
    /// <param name="plan">The plan to reconcile.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One reason per division-season that does not reconcile; empty when all do.</returns>
    public async Task<List<string>> DetectDriftAsync(
        SeasonRolloverPlan plan,
        CancellationToken cancellationToken)
    {
        var drift = new List<string>();

        foreach (var reconciliation in await ReconcileAsync(plan, cancellationToken))
        {
            if (reconciliation.Result.Outcome != ProjectionRebuildOutcome.Reconciled)
            {
                drift.Add(
                    $"division-season {reconciliation.DivisionSeasonId} did not reconcile "
                    + $"({reconciliation.Result.Outcome})");
            }
        }

        return drift;
    }

    /// <summary>
    /// Reconciles each division-season's projections with a read-only dry run (`TBL-13`, ADR-0020), so
    /// preflight can report the drift an operator would need to repair before the rollover runs.
    /// </summary>
    /// <param name="plan">The plan to reconcile.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<DivisionReconciliation>> ReconcileAsync(
        SeasonRolloverPlan plan,
        CancellationToken cancellationToken)
    {
        var reconciliations = new List<DivisionReconciliation>();

        foreach (var tier in plan.Countries.SelectMany(country => country.Tiers))
        {
            var result = await _rebuild.ExecuteAsync(tier.DivisionSeason.Id, apply: false, cancellationToken);

            reconciliations.Add(new DivisionReconciliation(tier.TierNumber, tier.DivisionSeason.Id, result));
        }

        return reconciliations;
    }

    /// <summary>Projects a country's active tiers into the shape the movement rule reads (`PR-1`).</summary>
    /// <param name="country">The country.</param>
    public static List<TierStandings> ToTiers(RolloverCountry country) =>
        country.Tiers
            .Select(tier => new TierStandings(tier.TierNumber, tier.RankedClubIds))
            .ToList();
}
