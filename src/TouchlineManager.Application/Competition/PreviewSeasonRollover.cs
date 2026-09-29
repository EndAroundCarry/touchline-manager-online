using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.Competition;

/// <summary>One club's movement a rollover would apply (`PR-3`).</summary>
/// <param name="ClubId">The club.</param>
/// <param name="FromTier">The tier it played in.</param>
/// <param name="ToTier">The tier it would play in next.</param>
/// <param name="IsPromoted">Whether it would go up.</param>
/// <param name="IsRelegated">Whether it would go down.</param>
public sealed record SeasonRolloverMovement(Guid ClubId, int FromTier, int ToTier, bool IsPromoted, bool IsRelegated);

/// <summary>One country's movements in a previewed rollover.</summary>
/// <param name="CountryId">The country.</param>
/// <param name="Code">The country's code.</param>
/// <param name="DisplayName">The country's name.</param>
/// <param name="Movements">One movement per club, promoted and relegated clubs included.</param>
public sealed record SeasonRolloverPreviewCountry(
    Guid CountryId,
    string Code,
    string DisplayName,
    IReadOnlyList<SeasonRolloverMovement> Movements);

/// <summary>One division-season's preflight reconciliation (`TBL-13`).</summary>
/// <param name="DivisionSeasonId">The division-season.</param>
/// <param name="TierNumber">The tier.</param>
/// <param name="Outcome">The reconciliation outcome, as a stable code.</param>
/// <param name="StandingsDrifted">How many table rows differed.</param>
/// <param name="PlayerStatsDrifted">How many player lines differed or had no source.</param>
public sealed record SeasonRolloverDivisionReconciliation(
    Guid DivisionSeasonId,
    int TierNumber,
    string Outcome,
    int StandingsDrifted,
    int PlayerStatsDrifted);

/// <summary>The preflight a rollover would run (`PR-4`, master plan §7.5 step 1).</summary>
/// <param name="UnpublishedMatchdays">Rounds of the closing season not yet published.</param>
/// <param name="Reconciles">Whether every division-season reconciles with its published results.</param>
/// <param name="Problem">Why the plan could not be read at all, when it could not.</param>
/// <param name="Divisions">The per-division reconciliation reads.</param>
public sealed record SeasonRolloverPreflight(
    int UnpublishedMatchdays,
    bool Reconciles,
    string? Problem,
    IReadOnlyList<SeasonRolloverDivisionReconciliation> Divisions);

/// <summary>The totals a previewed rollover would produce.</summary>
/// <param name="Countries">How many countries would move.</param>
/// <param name="Clubs">How many clubs would be placed.</param>
/// <param name="Promotions">How many clubs would go up.</param>
/// <param name="Relegations">How many clubs would go down.</param>
/// <param name="PositionAwardsMinor">The position awards the finalize phase would pay, in minor units.</param>
public sealed record SeasonRolloverPreviewTotals(
    int Countries,
    int Clubs,
    int Promotions,
    int Relegations,
    long PositionAwardsMinor);

/// <summary>What a rollover would do, read without writing anything (ADR-0034).</summary>
/// <param name="WorldId">The world.</param>
/// <param name="SeasonId">The season that would close.</param>
/// <param name="SeasonLabel">The season's label.</param>
/// <param name="SeasonStatus">The season's lifecycle state, as a stable code.</param>
/// <param name="RolloverPhase">The rollover's checkpoint, or null when none has started.</param>
/// <param name="FailureReason">Why a failed rollover stopped, when it did.</param>
/// <param name="Preflight">The preflight checks the run would perform.</param>
/// <param name="NextSeasonExists">Whether the next season already exists (the move phase ran).</param>
/// <param name="Countries">The movement plan per country.</param>
/// <param name="Totals">The plan's totals.</param>
/// <param name="Ready">Whether preflight passes and the rollover is not already completed.</param>
public sealed record SeasonRolloverPreview(
    Guid WorldId,
    Guid SeasonId,
    string SeasonLabel,
    string SeasonStatus,
    string? RolloverPhase,
    string? FailureReason,
    SeasonRolloverPreflight Preflight,
    bool NextSeasonExists,
    IReadOnlyList<SeasonRolloverPreviewCountry> Countries,
    SeasonRolloverPreviewTotals Totals,
    bool Ready);

/// <summary>
/// Previews a season rollover: what it would move and pay, and whether it would pass preflight — the
/// operator dry-run (`PR-4`, master plan §7.5, ADR-0034).
/// </summary>
/// <remarks>
/// <para>
/// It reads the same plan the rollover machine reads, through the same loader, and computes movement with
/// the same pure rule (<see cref="PromotionRelegation"/>, `promotion-relegation-v1`) and reconciliation with
/// the same dry run (`RebuildDivisionProjections`), so the preview cannot describe a rollover the machine
/// would not run. It writes nothing and takes no transaction or lock (ADR-0020).
/// </para>
/// <para>
/// Reachable only from the non-production diagnostics trigger (§17.12), like the other worker-only
/// workflows' controls. The rollover itself stays worker-only (ADR-0031 §7).
/// </para>
/// </remarks>
public sealed class PreviewSeasonRollover
{
    private readonly IWorldRepository _world;
    private readonly ISeasonRolloverRepository _rollovers;
    private readonly SeasonRolloverPlanLoader _plans;

    /// <summary>Initializes the use case.</summary>
    public PreviewSeasonRollover(
        IWorldRepository world,
        ISeasonRolloverRepository rollovers,
        SeasonRolloverPlanLoader plans)
    {
        _world = world;
        _rollovers = rollovers;
        _plans = plans;
    }

    /// <summary>Reads what a rollover would do for a season, or the world's current one when none is named.</summary>
    /// <param name="seasonId">The season to preview, or null for the world's current season.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The preview, or null when the world or the named season does not exist.</returns>
    public async Task<SeasonRolloverPreview?> ExecuteAsync(Guid? seasonId, CancellationToken cancellationToken)
    {
        var world = await _world.FindWorldAsync(cancellationToken);

        if (world is null)
        {
            return null;
        }

        var season = seasonId is { } named
            ? await _world.FindSeasonByIdAsync(named, cancellationToken)
            : await _world.FindSeasonAsync(world.Id, world.CurrentSeasonNumber, cancellationToken);

        if (season is null || season.WorldId != world.Id)
        {
            return null;
        }

        var rollover = await _rollovers.FindBySeasonAsync(world.Id, season.Id, cancellationToken);
        var nextSeason = await _world.FindSeasonAsync(world.Id, season.SequenceNumber + 1, cancellationToken);
        var phase = rollover is null ? null : (SeasonRolloverPhase?)rollover.Phase;

        SeasonRolloverPlan plan;

        try
        {
            plan = await _plans.LoadAsync(world, season, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            // The season's shape is not rollover-ready. A preview reports it rather than failing, because
            // that is exactly the fact an operator opened the preview to learn (PR-5).
            return Empty(world, season, rollover, nextSeason is not null, exception.Message);
        }

        var unpublished = await _plans.CountUnpublishedAsync(plan, cancellationToken);
        var reconciliations = await _plans.ReconcileAsync(plan, cancellationToken);
        var reconciles = reconciliations.All(
            reconciliation => reconciliation.Result.Outcome == ProjectionRebuildOutcome.Reconciled);

        var countries = new List<SeasonRolloverPreviewCountry>(plan.Countries.Count);
        var movements = new List<SeasonRolloverMovement>();
        var awards = 0L;

        foreach (var country in plan.Countries)
        {
            var countryMovements = PromotionRelegation
                .Compute(SeasonRolloverPlanLoader.ToTiers(country))
                .Select(movement => new SeasonRolloverMovement(
                    movement.ClubId,
                    movement.FromTier,
                    movement.ToTier,
                    movement.IsPromoted,
                    movement.IsRelegated))
                .ToList();

            movements.AddRange(countryMovements);

            countries.Add(new SeasonRolloverPreviewCountry(
                country.CountryId,
                country.Code,
                country.DisplayName,
                countryMovements));

            awards += PositionAwards(country);
        }

        var preflight = new SeasonRolloverPreflight(
            unpublished,
            reconciles,
            Problem: null,
            reconciliations
                .Select(reconciliation => new SeasonRolloverDivisionReconciliation(
                    reconciliation.DivisionSeasonId,
                    reconciliation.TierNumber,
                    reconciliation.Result.Outcome.ToString(),
                    reconciliation.Result.StandingsDrifted,
                    reconciliation.Result.PlayerStatsDrifted))
                .ToList());

        var totals = new SeasonRolloverPreviewTotals(
            plan.Countries.Count,
            movements.Count,
            movements.Count(movement => movement.IsPromoted),
            movements.Count(movement => movement.IsRelegated),
            awards);

        return new SeasonRolloverPreview(
            world.Id,
            season.Id,
            season.DisplayLabel,
            season.Status.ToCode(),
            phase is null ? null : phase.Value.ToCode(),
            rollover?.FailureReason,
            preflight,
            nextSeason is not null,
            countries,
            totals,
            // Preflight decides readiness: a season with unpublished rounds or drifted projections would be
            // refused, and a completed rollover has nothing to do (PR-4, TBL-13).
            unpublished == 0 && reconciles && phase != SeasonRolloverPhase.Completed);
    }

    private static long PositionAwards(RolloverCountry country)
    {
        var awards = 0L;

        foreach (var tier in country.Tiers)
        {
            for (var index = 0; index < tier.RankedClubIds.Count; index++)
            {
                var rank = index + 1;

                if (rank > WorldRuleSet.ClubsPerDivision)
                {
                    continue;
                }

                awards += WorldRuleSet.PositionAwardMinorFor(tier.TierNumber, rank);
            }
        }

        return awards;
    }

    private static SeasonRolloverPreview Empty(
        GameWorld world,
        Season season,
        SeasonRollover? rollover,
        bool nextSeasonExists,
        string problem) =>
        new(
            world.Id,
            season.Id,
            season.DisplayLabel,
            season.Status.ToCode(),
            rollover is null ? null : rollover.Phase.ToCode(),
            rollover?.FailureReason,
            new SeasonRolloverPreflight(0, Reconciles: false, problem, []),
            nextSeasonExists,
            [],
            new SeasonRolloverPreviewTotals(0, 0, 0, 0, 0),
            Ready: false);
}
