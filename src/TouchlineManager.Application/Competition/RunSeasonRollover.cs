using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Comms;
using TouchlineManager.Application.Finance;
using TouchlineManager.Application.Squad;
using TouchlineManager.Application.World.Generation;
using TouchlineManager.Domain.Comms;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.Competition;

/// <summary>What happened when a season was rolled over.</summary>
public enum SeasonRolloverOutcome
{
    /// <summary>The closing season was sealed, the next season created, and the world advanced (`PR-4`, `PR-5`).</summary>
    Completed = 0,

    /// <summary>The rollover had already completed; the run is a no-op.</summary>
    AlreadyCompleted = 1,
}

/// <summary>The result of a rollover run.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="WorldId">The world.</param>
/// <param name="SeasonId">The season that closed.</param>
/// <param name="NextSeasonId">The season that opened, once it exists.</param>
/// <param name="Countries">How many countries were rolled over.</param>
/// <param name="ClubsPlaced">How many clubs were placed into the next season.</param>
/// <param name="Promotions">How many clubs went up.</param>
/// <param name="Relegations">How many clubs went down.</param>
public sealed record SeasonRolloverResult(
    SeasonRolloverOutcome Outcome,
    Guid WorldId,
    Guid SeasonId,
    Guid? NextSeasonId,
    int Countries,
    int ClubsPlaced,
    int Promotions,
    int Relegations);

/// <summary>
/// Runs one season's rollover: freeze, finalize, move, and complete (master plan §7.5, ADR-0031).
/// </summary>
/// <remarks>
/// <para>
/// The plan calls rollover a resumable state machine with checkpoints rather than one opaque transaction.
/// This is that machine: each phase commits its own work together with the phase it reached on the
/// <see cref="SeasonRollover"/> row, and a worker that dies between phases is retried by the queue and
/// resumes at the next one. The world-scoped advisory lock makes the whole thing a singleton, so movement
/// and the game-year increment cannot interleave with a peer (ADR-0003).
/// </para>
/// <para>
/// Movement applies to clubs, not managers, so a manager stays with their club (`PR-3`), and the closing
/// season's entries, fixtures, and tables are never rewritten (`PR-6`): the next season is new rows, not an
/// edit of the old ones. Every phase is idempotent — a repeated freeze, a repeated close, a repeated
/// division-season creation — because at-least-once delivery will repeat them.
/// </para>
/// <para>
/// Two failure shapes are distinguished deliberately. A season that is not yet fully played is a
/// <em>transient</em> fault: a normal exception leaves the job to be retried and the row untouched. Drift in
/// a projection is a <em>defect</em>: the rollover is marked failed and the job dead-letters for an operator,
/// because re-running it in a loop would turn a diagnosable fault into a hot loop.
/// </para>
/// </remarks>
public sealed partial class RunSeasonRollover
{
    private readonly IClock _clock;
    private readonly IWorldRepository _world;
    private readonly ISeasonRolloverRepository _rollovers;
    private readonly IClubRepository _clubs;
    private readonly IClubTenureRepository _tenures;
    private readonly IClubAccountRepository _accounts;
    private readonly IMatchdayRepository _matchdays;
    private readonly DivisionScheduleGenerator _schedules;
    private readonly RebuildDivisionProjections _rebuild;
    private readonly SettleSeasonFinances _finances;
    private readonly SettleSquadContinuity _continuity;
    private readonly IInboxRepository _inbox;
    private readonly IAdvisoryLock _locks;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RunSeasonRollover> _logger;

    /// <summary>Initializes the use case.</summary>
    public RunSeasonRollover(
        IClock clock,
        IWorldRepository world,
        ISeasonRolloverRepository rollovers,
        IClubRepository clubs,
        IClubTenureRepository tenures,
        IClubAccountRepository accounts,
        IMatchdayRepository matchdays,
        DivisionScheduleGenerator schedules,
        RebuildDivisionProjections rebuild,
        SettleSeasonFinances finances,
        SettleSquadContinuity continuity,
        IInboxRepository inbox,
        IAdvisoryLock locks,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork,
        ILogger<RunSeasonRollover> logger)
    {
        _clock = clock;
        _world = world;
        _rollovers = rollovers;
        _clubs = clubs;
        _tenures = tenures;
        _accounts = accounts;
        _matchdays = matchdays;
        _schedules = schedules;
        _rebuild = rebuild;
        _finances = finances;
        _continuity = continuity;
        _inbox = inbox;
        _locks = locks;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>Runs one season's rollover to completion, or resumes a partial one.</summary>
    /// <param name="seasonId">The closing season.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SeasonRolloverResult> ExecuteAsync(Guid seasonId, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var world = await _world.FindWorldAsync(cancellationToken)
            ?? throw new PermanentJobFailureException("The world does not exist, so no season can roll over.");
        var season = await _world.FindSeasonByIdAsync(seasonId, cancellationToken)
            ?? throw new PermanentJobFailureException($"Season {seasonId} does not exist (PR-4).");

        if (season.WorldId != world.Id)
        {
            throw new PermanentJobFailureException($"Season {seasonId} does not belong to the world (PR-4).");
        }

        if (await EnsureStartedAsync(world, season, now, cancellationToken))
        {
            return new SeasonRolloverResult(
                SeasonRolloverOutcome.AlreadyCompleted,
                world.Id,
                season.Id,
                null,
                0,
                0,
                0,
                0);
        }

        await FreezeAsync(world, season, now, cancellationToken);

        var finalizePlan = await LoadPlanAsync(world, season, cancellationToken);
        await FinalizeAsync(world, season, finalizePlan, now, cancellationToken);

        var movePlan = await LoadPlanAsync(world, season, cancellationToken);
        await SquadsAsync(world, season, now, cancellationToken);
        var nextSeasonId = await MoveAsync(world, season, movePlan, now, cancellationToken);

        await CompleteAsync(world, season, nextSeasonId, now, cancellationToken);

        var promotions = movePlan.Countries
            .SelectMany(country => PromotionRelegation.Compute(ToTiers(country)))
            .Count(movement => movement.IsPromoted);
        var relegations = movePlan.Countries
            .SelectMany(country => PromotionRelegation.Compute(ToTiers(country)))
            .Count(movement => movement.IsRelegated);

        LogRolledOver(world.Id, season.Id, nextSeasonId, movePlan.Countries.Count, promotions, relegations);

        return new SeasonRolloverResult(
            SeasonRolloverOutcome.Completed,
            world.Id,
            season.Id,
            nextSeasonId,
            movePlan.Countries.Count,
            movePlan.Countries.Sum(country => country.Tiers.Sum(tier => tier.Entries.Count)),
            promotions,
            relegations);
    }

    /// <summary>
    /// Creates the rollover row if it does not exist, or reports that it has already finished.
    /// </summary>
    /// <returns>True when the rollover has already completed and this run should stop.</returns>
    private async Task<bool> EnsureStartedAsync(
        GameWorld world,
        Season season,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);
        await _locks.AcquireAsync(AdvisoryLockKey.SeasonRollover(world.Id), cancellationToken);

        var existing = await _rollovers.FindBySeasonAsync(world.Id, season.Id, cancellationToken);

        if (existing is not null)
        {
            if (existing.Phase == SeasonRolloverPhase.Completed)
            {
                await transaction.RollbackAsync(cancellationToken);

                return true;
            }

            if (existing.Phase == SeasonRolloverPhase.Failed)
            {
                await transaction.RollbackAsync(cancellationToken);

                // An operator decision is needed before a failed rollover runs again (Stage 14); the queue
                // cannot re-enqueue a dead-lettered job, so this is a loud stop rather than a retry.
                throw new PermanentJobFailureException(
                    $"The rollover of season {season.Id} is failed and awaits an operator (ADR-0031): "
                    + existing.FailureReason);
            }

            await transaction.RollbackAsync(cancellationToken);

            return false;
        }

        _rollovers.Add(SeasonRollover.Start(Guid.CreateVersion7(), world.Id, season.Id, now));
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return false;
    }

    /// <summary>
    /// Preflight and freeze: refuse to roll over a season that is not finished, freeze claims, and record
    /// the checkpoint (`PR-4`).
    /// </summary>
    private async Task FreezeAsync(
        GameWorld world,
        Season season,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);
        await _locks.AcquireAsync(AdvisoryLockKey.SeasonRollover(world.Id), cancellationToken);

        var rollover = await RequireRolloverAsync(world, season, cancellationToken);

        if (SeasonRolloverPhaseRules.IsAtLeast(rollover.Phase, SeasonRolloverPhase.Frozen))
        {
            await transaction.RollbackAsync(cancellationToken);

            return;
        }

        var plan = await LoadPlanAsync(world, season, cancellationToken);

        var unpublished = await CountUnpublishedAsync(plan, cancellationToken);

        if (unpublished > 0)
        {
            await transaction.RollbackAsync(cancellationToken);

            // The season is not finished yet. This is a race with a late publication, not a defect, so the
            // exception is transient and the queue retries the job.
            throw new InvalidOperationException(
                $"Season {season.Id} still has {unpublished} unplayed matchdays; the rollover will retry (PR-4).");
        }

        var drift = await DetectDriftAsync(plan, cancellationToken);

        if (drift.Count > 0)
        {
            var reason = string.Join("; ", drift);

            rollover.Fail(reason, now);
            RecordAudit(WorldAuditActions.RolloverFailed, rollover.Id, reason);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            LogFailed(reason);

            throw new PermanentJobFailureException(
                $"Season {season.Id} did not reconcile and cannot roll over: {reason} (PR-4, TBL-13).");
        }

        if (season.Status == SeasonStatus.Active)
        {
            season.BeginRollover(now);
        }

        rollover.Freeze(now);
        RecordAudit(WorldAuditActions.RolloverStarted, rollover.Id, $"{season.DisplayLabel}");

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogFrozen(season.Id, plan.Countries.Count);
    }

    /// <summary>
    /// Finalizes the standings and closes every entry with its final rank and movement (`PR-4`).
    /// </summary>
    private async Task FinalizeAsync(
        GameWorld world,
        Season season,
        RolloverPlan plan,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);
        await _locks.AcquireAsync(AdvisoryLockKey.SeasonRollover(world.Id), cancellationToken);

        var rollover = await RequireRolloverAsync(world, season, cancellationToken);

        if (SeasonRolloverPhaseRules.IsAtLeast(rollover.Phase, SeasonRolloverPhase.Finalized))
        {
            await transaction.RollbackAsync(cancellationToken);

            return;
        }

        foreach (var country in plan.Countries)
        {
            var movements = PromotionRelegation.Compute(ToTiers(country))
                .ToDictionary(movement => movement.ClubId);

            foreach (var tier in country.Tiers)
            {
                tier.DivisionSeason.Complete(now);

                var clubIds = tier.Entries.Select(entry => entry.ClubId).ToList();
                var reputationOf = (await _clubs.LoadAsync(clubIds, cancellationToken))
                    .ToDictionary(club => club.Id, club => club.Reputation);
                var cashOf = (await _accounts.LoadAsync(clubIds, cancellationToken))
                    .ToDictionary(account => account.ClubId, account => account.CashMinor);

                foreach (var entry in tier.Entries)
                {
                    if (entry.FinalRank is not null)
                    {
                        // Already closed by an earlier attempt; closing it twice would bump its version.
                        continue;
                    }

                    var movement = movements[entry.ClubId];

                    entry.Close(
                        tier.RankOf(entry.ClubId),
                        movement.IsPromoted,
                        movement.IsRelegated,
                        reputationOf[entry.ClubId],
                        cashOf[entry.ClubId],
                        now);
                }
            }
        }

        var settlement = plan.Countries
            .SelectMany(country => country.Tiers)
            .SelectMany(tier => tier.Entries.Select(entry => new SeasonSettlementClub(
                entry.ClubId,
                tier.TierNumber,
                tier.RankOf(entry.ClubId))))
            .Where(club => club.FinalRank >= 1)
            .ToList();

        await _finances.SettleAsync(world.Id, season, settlement, now, cancellationToken);

        rollover.Finalize(now);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogFinalized(season.Id);
    }

    /// <summary>
    /// Resolves the closing season's contracts: retirements, expiries, unmanaged-club renewals, and the
    /// emergency replacements that keep every club legal (`CON-6`, `CON-8`, `SQ-8`).
    /// </summary>
    /// <remarks>
    /// It ensures the next season exists first, because an emergency replacement's registration names the
    /// season it is effective in; the move phase then finds the season already created rather than making a
    /// second one.
    /// </remarks>
    private async Task SquadsAsync(
        GameWorld world,
        Season season,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);
        await _locks.AcquireAsync(AdvisoryLockKey.SeasonRollover(world.Id), cancellationToken);

        var rollover = await RequireRolloverAsync(world, season, cancellationToken);

        if (SeasonRolloverPhaseRules.IsAtLeast(rollover.Phase, SeasonRolloverPhase.Squads))
        {
            await transaction.RollbackAsync(cancellationToken);

            return;
        }

        var nextSeason = await EnsureNextSeasonAsync(world, season, now, cancellationToken);

        var result = await _continuity.ExecuteAsync(world, season, nextSeason, now, cancellationToken);

        rollover.SettleSquads(now);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogSquadsSettled(season.Id, result.Retired, result.Announced, result.Renewed, result.Released, result.Replacements);
    }

    /// <summary>
    /// Creates the next season and places every club into it with a fresh schedule (`PR-5`).
    /// </summary>
    /// <returns>The next season's identity.</returns>
    private async Task<Guid> MoveAsync(
        GameWorld world,
        Season season,
        RolloverPlan plan,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);
        await _locks.AcquireAsync(AdvisoryLockKey.SeasonRollover(world.Id), cancellationToken);

        var rollover = await RequireRolloverAsync(world, season, cancellationToken);

        if (SeasonRolloverPhaseRules.IsAtLeast(rollover.Phase, SeasonRolloverPhase.Moved))
        {
            await transaction.RollbackAsync(cancellationToken);

            return rollover.NextSeasonId
                ?? throw new PermanentJobFailureException(
                    $"Rollover {rollover.Id} is moved but names no next season (PR-5).");
        }

        var nextSeason = await EnsureNextSeasonAsync(world, season, now, cancellationToken);
        var seed = world.Id.ToString("D");
        var openClubIds = (await _tenures.ListOpenClubIdsAsync(cancellationToken)).ToHashSet();

        foreach (var country in plan.Countries)
        {
            var movements = PromotionRelegation.Compute(ToTiers(country));

            var byDestination = movements
                .GroupBy(movement => movement.ToTier)
                .ToDictionary(group => group.Key, group => group.Select(movement => movement.ClubId).ToList());

            // The clubs whose tier changed this rollover, so a manager can be told where their club is going
            // (PR-1, COM-1). A stationary club is not a story.
            var moved = movements
                .Where(movement => !movement.IsStationary)
                .ToDictionary(movement => movement.ClubId);

            foreach (var tier in country.Tiers)
            {
                var clubs = byDestination.TryGetValue(tier.TierNumber, out var placed) ? placed : [];

                if (clubs.Count != WorldRuleSet.ClubsPerDivision)
                {
                    throw new InvalidOperationException(
                        $"Tier {tier.TierNumber} of country {country.Code} would hold {clubs.Count} clubs "
                        + $"instead of {WorldRuleSet.ClubsPerDivision} (PR-1).");
                }

                // A stable order, so the schedule is reproducible from the seed (CAL-8).
                var ordered = clubs.OrderBy(clubId => clubId).ToList();

                var nextDivisionSeason = await EnsureNextDivisionSeasonAsync(
                    world,
                    nextSeason,
                    country,
                    tier,
                    seed,
                    now,
                    cancellationToken);

                var existing = await _world.ListClubSeasonEntriesAsync(nextDivisionSeason.Id, cancellationToken);

                if (existing.Count > 0)
                {
                    // Already moved by an earlier attempt.
                    continue;
                }

                foreach (var clubId in ordered)
                {
                    _world.AddClubSeasonEntry(ClubSeasonEntry.Enter(
                        Guid.CreateVersion7(),
                        nextDivisionSeason.Id,
                        nextSeason.Id,
                        clubId,
                        openClubIds.Contains(clubId) ? ClubControlType.Human : ClubControlType.Ai,
                        now));
                }

                _schedules.Generate(nextDivisionSeason, ordered, nextSeason, now, bootstrapCutoff: null);

                // Only where the clubs are actually placed, so a redelivery that finds the entries present
                // skips both the placement and the message (at-least-once).
                await NotifyMovementsAsync(country, tier, ordered, moved, now, cancellationToken);
            }
        }

        rollover.Move(nextSeason.Id, now);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return nextSeason.Id;
    }

    /// <summary>
    /// Tells the manager of each club that changed tier where their club is going (`PR-1`, `COM-1`).
    /// </summary>
    /// <remarks>
    /// Runs inside the move transaction, next to the placement it announces, so the message commits with the
    /// movement or not at all. Only the clubs placed in this destination tier are considered, and only the
    /// attended ones get a message: an AI club has nobody to tell. A redelivery that finds the destination's
    /// entries already present skips both the placement and this call (at-least-once).
    /// </remarks>
    private async Task NotifyMovementsAsync(
        RolloverCountry country,
        RolloverTier destination,
        IReadOnlyList<Guid> placedClubIds,
        Dictionary<Guid, ClubMovement> moved,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var moving = placedClubIds.Where(moved.ContainsKey).ToList();

        if (moving.Count == 0)
        {
            return;
        }

        var targets = (await _inbox.FindClubTargetsAsync(moving, cancellationToken))
            .ToDictionary(target => target.ClubId);

        foreach (var clubId in moving)
        {
            if (!targets.TryGetValue(clubId, out var target)
                || target.ManagerId is not { } managerId
                || managerId == Guid.Empty)
            {
                // An AI club has nobody to tell (COM-1).
                continue;
            }

            var movement = moved[clubId];
            var fromDivision = country.Tiers
                .FirstOrDefault(tier => tier.TierNumber == movement.FromTier)?.DivisionDisplayName
                ?? destination.DivisionDisplayName;

            var draft = InboxTemplates.Movement(
                target.Name,
                fromDivision,
                destination.DivisionDisplayName,
                movement.IsPromoted);

            _inbox.Add(InboxMessage.Record(
                Guid.CreateVersion7(),
                managerId,
                draft.Category,
                draft.TemplateKey,
                draft.ParametersJson,
                draft.RelatedEntityId,
                now));
        }
    }

    /// <summary>
    /// Seals the closing season, advances the world's season pointer, and activates the next season
    /// (`PR-6`, `TIME-3`).
    /// </summary>
    private async Task CompleteAsync(
        GameWorld world,
        Season season,
        Guid nextSeasonId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);
        await _locks.AcquireAsync(AdvisoryLockKey.SeasonRollover(world.Id), cancellationToken);

        var rollover = await RequireRolloverAsync(world, season, cancellationToken);

        if (rollover.Phase == SeasonRolloverPhase.Completed)
        {
            await transaction.RollbackAsync(cancellationToken);

            return;
        }

        var nextSeason = await _world.FindSeasonByIdAsync(nextSeasonId, cancellationToken)
            ?? throw new PermanentJobFailureException($"The next season {nextSeasonId} vanished before activation.");

        // The pointer moves in the same transaction as the seal and the seal of the rollover row, so a crash
        // leaves none of the three applied and the retry re-runs this phase from the same checkpoint.
        world.AdvanceToNextSeason(now);

        if (nextSeason.Status == SeasonStatus.Scheduled)
        {
            nextSeason.Activate(now);
        }

        season.Complete(now);
        rollover.Complete(now);

        RecordAudit(WorldAuditActions.RolloverCompleted, rollover.Id, nextSeason.DisplayLabel);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogCompleted(season.Id, nextSeasonId, nextSeason.DisplayLabel);
    }

    private async Task<Season> EnsureNextSeasonAsync(
        GameWorld world,
        Season season,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var nextSequence = season.SequenceNumber + 1;
        var existing = await _world.FindSeasonAsync(world.Id, nextSequence, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        // The next season begins on the first configured matchday after the rollover window closes (`CAL-6`).
        var firstMatchday = SeasonCalendar.FirstMatchdayOnOrAfter(
            DateOnly.FromDateTime(season.RolloverEndsAt.UtcDateTime));

        var next = Season.Create(
            Guid.CreateVersion7(),
            world.Id,
            nextSequence,
            season.GameYear + 1,
            season.RuleSetVersion,
            firstMatchday,
            now);

        _world.AddSeason(next);

        return next;
    }

    private async Task<DivisionSeason> EnsureNextDivisionSeasonAsync(
        GameWorld world,
        Season nextSeason,
        RolloverCountry country,
        RolloverTier tier,
        string seed,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await _world.FindDivisionSeasonAsync(tier.DivisionId, nextSeason.Id, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var seeds = DivisionScheduleGenerator.SeedsFor(seed, country.Code, nextSeason.SequenceNumber, tier.TierNumber);

        var divisionSeason = DivisionSeason.Create(
            Guid.CreateVersion7(),
            tier.DivisionId,
            nextSeason.Id,
            seeds.ScheduleSeed,
            seeds.TieDrawSeed,
            seeds.TieDrawHash,
            now);

        divisionSeason.Activate(now);
        _world.AddDivisionSeason(divisionSeason);

        return divisionSeason;
    }

    private async Task<SeasonRollover> RequireRolloverAsync(
        GameWorld world,
        Season season,
        CancellationToken cancellationToken) =>
        await _rollovers.FindBySeasonAsync(world.Id, season.Id, cancellationToken)
        ?? throw new PermanentJobFailureException($"The rollover of season {season.Id} vanished (ADR-0031).");

    /// <summary>Reads the closing season's shape: every active tier with its entries and final ordering.</summary>
    private async Task<RolloverPlan> LoadPlanAsync(
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
                    throw new PermanentJobFailureException(
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

        return new RolloverPlan(plan);
    }

    private async Task<int> CountUnpublishedAsync(RolloverPlan plan, CancellationToken cancellationToken)
    {
        var count = 0;

        foreach (var tier in plan.Countries.SelectMany(country => country.Tiers))
        {
            var matchdays = await _matchdays.LoadDivisionMatchdaysAsync(tier.DivisionSeason.Id, cancellationToken);

            count += matchdays.Count(matchday => matchday.PublicationStatus != MatchdayPublicationStatus.Published);
        }

        return count;
    }

    private async Task<List<string>> DetectDriftAsync(RolloverPlan plan, CancellationToken cancellationToken)
    {
        var drift = new List<string>();

        foreach (var tier in plan.Countries.SelectMany(country => country.Tiers))
        {
            var reconciliation = await _rebuild.ExecuteAsync(tier.DivisionSeason.Id, apply: false, cancellationToken);

            if (reconciliation.Outcome != ProjectionRebuildOutcome.Reconciled)
            {
                drift.Add($"division-season {tier.DivisionSeason.Id} did not reconcile ({reconciliation.Outcome})");
            }
        }

        return drift;
    }

    private static List<TierStandings> ToTiers(RolloverCountry country) =>
        country.Tiers
            .Select(tier => new TierStandings(tier.TierNumber, tier.RankedClubIds))
            .ToList();

    private void RecordAudit(string action, Guid rolloverId, string reason) =>
        _audit.Record(new AuditEntry(
            action,
            AuditActorTypes.Service,
            _requestContext.ActorUserId,
            AuditTargetTypes.SeasonRollover,
            rolloverId,
            _requestContext.CorrelationId,
            IpHash: null,
            Reason: reason));

    [LoggerMessage(
        EventId = 5200,
        Level = LogLevel.Information,
        Message = "Froze season {SeasonId} for rollover: {Countries} countries (PR-4).")]
    private partial void LogFrozen(Guid seasonId, int countries);

    [LoggerMessage(
        EventId = 5201,
        Level = LogLevel.Information,
        Message = "Finalized standings and closed entries for season {SeasonId} (PR-4).")]
    private partial void LogFinalized(Guid seasonId);

    [LoggerMessage(
        EventId = 5205,
        Level = LogLevel.Information,
        Message = "Settled contracts for season {SeasonId}: {Retired} retired, {Announced} announced, "
            + "{Renewed} renewed, {Released} released, {Replacements} replacements (CON-6).")]
    private partial void LogSquadsSettled(
        Guid seasonId,
        int retired,
        int announced,
        int renewed,
        int released,
        int replacements);

    [LoggerMessage(
        EventId = 5202,
        Level = LogLevel.Information,
        Message = "Completed the rollover of season {SeasonId}: next season {NextSeasonId} ({Label}) is active (PR-6).")]
    private partial void LogCompleted(Guid seasonId, Guid nextSeasonId, string label);

    [LoggerMessage(
        EventId = 5203,
        Level = LogLevel.Error,
        Message = "Season rollover failed and awaits an operator: {Reason} (PR-4).")]
    private partial void LogFailed(string reason);

    [LoggerMessage(
        EventId = 5204,
        Level = LogLevel.Information,
        Message = "Rolled over world {WorldId} from season {SeasonId} to {NextSeasonId}: "
            + "{Countries} countries, {Promotions} promoted, {Relegations} relegated (PR-1).")]
    private partial void LogRolledOver(
        Guid worldId,
        Guid seasonId,
        Guid nextSeasonId,
        int countries,
        int promotions,
        int relegations);

    private sealed record RolloverTier(
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

    private sealed record RolloverCountry(
        Guid CountryId,
        string Code,
        string DisplayName,
        IReadOnlyList<RolloverTier> Tiers);

    private sealed record RolloverPlan(IReadOnlyList<RolloverCountry> Countries);
}
