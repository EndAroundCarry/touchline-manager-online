using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Comms;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.Squad;
using TouchlineManager.Application.World.Generation;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World;
using TouchlineManager.Domain.World.Generation;

namespace TouchlineManager.Application.World;

/// <summary>What happened when a tier was provisioned.</summary>
public enum ProvisionDivisionOutcome
{
    /// <summary>The tier was generated, backfilled, validated, and activated (`PYR-8`).</summary>
    Completed = 0,

    /// <summary>The request had already completed; the run is a no-op.</summary>
    AlreadyCompleted = 1,
}

/// <summary>The result of a provisioning run.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="CountryId">The country whose pyramid grew.</param>
/// <param name="TargetTier">The tier that was created.</param>
/// <param name="Clubs">How many clubs the tier holds.</param>
/// <param name="Players">How many players were created.</param>
/// <param name="Accounts">How many club accounts were opened.</param>
/// <param name="BackfilledRounds">How many already-passed matchdays were simulated as bootstrap (`PYR-6`).</param>
public sealed record ProvisionDivisionResult(
    ProvisionDivisionOutcome Outcome,
    Guid CountryId,
    int TargetTier,
    int Clubs,
    int Players,
    int Accounts,
    int BackfilledRounds);

/// <summary>
/// Generates a country's next tier, backfills the matchdays already passed this season, validates the
/// result, and activates the tier so its clubs become claimable (`PYR-4`…`PYR-8`).
/// </summary>
/// <remarks>
/// <para>
/// The request is created by a takeover inside the country lock (`PYR-2`); this worker turns it into a tier.
/// It is deliberately not one transaction. Generation commits first so a crash mid-backfill leaves a real,
/// inspectable tier and a resumable request rather than nothing; the backfill then runs the ordinary
/// lock → resolve → publish workflow once per passed round, in round order, so condition, fatigue, morale,
/// discipline, statistics, and gate receipts cascade exactly as a live matchday would (`PYR-6`, ADR-0005); and
/// activation commits last, only after validation (`PYR-8`).
/// </para>
/// <para>
/// It is idempotent on the request's identity: a retried run finds the tier the first attempt generated and
/// resumes at the next phase rather than generating a second. The country advisory lock and the unique
/// constraint on <c>(country, tier)</c> are the structural backstops (`PYR-3`).
/// </para>
/// </remarks>
public sealed partial class ProvisionDivision
{
    private readonly IClock _clock;
    private readonly IWorldRepository _world;
    private readonly IDivisionProvisioningRequestRepository _requests;
    private readonly IGenerationRunRepository _generationRuns;
    private readonly WorldGenerator _generator;
    private readonly EvaluateAiClubs _aiClubs;
    private readonly IMatchdayRepository _matchdays;
    private readonly LockMatchday _lock;
    private readonly ResolveMatchday _resolve;
    private readonly PublishMatchday _publish;
    private readonly RebuildDivisionProjections _rebuild;
    private readonly IOnboardingQueries _queries;
    private readonly IRosterQueries _roster;
    private readonly IAdvisoryLock _locks;
    private readonly CapacityEvaluator _capacity;
    private readonly PostNews _news;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ProvisionDivision> _logger;

    /// <summary>Initializes the use case.</summary>
    public ProvisionDivision(
        IClock clock,
        IWorldRepository world,
        IDivisionProvisioningRequestRepository requests,
        IGenerationRunRepository generationRuns,
        WorldGenerator generator,
        EvaluateAiClubs aiClubs,
        IMatchdayRepository matchdays,
        LockMatchday matchdayLock,
        ResolveMatchday resolve,
        PublishMatchday publish,
        RebuildDivisionProjections rebuild,
        IOnboardingQueries queries,
        IRosterQueries roster,
        IAdvisoryLock locks,
        CapacityEvaluator capacity,
        PostNews news,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork,
        ILogger<ProvisionDivision> logger)
    {
        _clock = clock;
        _world = world;
        _requests = requests;
        _generationRuns = generationRuns;
        _generator = generator;
        _aiClubs = aiClubs;
        _matchdays = matchdays;
        _lock = matchdayLock;
        _resolve = resolve;
        _publish = publish;
        _rebuild = rebuild;
        _queries = queries;
        _roster = roster;
        _locks = locks;
        _capacity = capacity;
        _news = news;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>Runs one provisioning request to completion, or resumes a partial run.</summary>
    /// <param name="requestId">The provisioning request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ProvisionDivisionResult> ExecuteAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        Guid countryId;
        int targetTier;
        Guid divisionSeasonId;
        int clubs;
        int players;
        int accounts;

        // Phase one: generate the tier under the country lock, or find the one a previous attempt generated.
        await using (var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken))
        {
            var request = await _requests.FindByIdAsync(requestId, cancellationToken)
                ?? throw new PermanentJobFailureException($"Provisioning request {requestId} does not exist (PYR-2).");

            countryId = request.CountryId;
            targetTier = request.TargetTier;

            await _locks.AcquireAsync(AdvisoryLockKey.Country(countryId), cancellationToken);

            if (request.Status == ProvisioningRequestStatus.Completed)
            {
                await transaction.RollbackAsync(cancellationToken);

                return new ProvisionDivisionResult(
                    ProvisionDivisionOutcome.AlreadyCompleted,
                    countryId,
                    targetTier,
                    0,
                    0,
                    0,
                    0);
            }

            if (request.Status == ProvisioningRequestStatus.Requested)
            {
                request.Start(now);
            }

            var country = await _world.FindCountryAsync(countryId, cancellationToken)
                ?? throw new PermanentJobFailureException($"Country {countryId} does not exist (PYR-4).");
            var season = await _world.FindSeasonByIdAsync(request.TargetSeasonId, cancellationToken)
                ?? throw new PermanentJobFailureException(
                    $"The target season {request.TargetSeasonId} does not exist (PYR-5).");

            var existing = await _world.FindDivisionByTierAsync(countryId, targetTier, cancellationToken);

            if (existing is null)
            {
                var generated = _generator.BuildTier(
                    new TierGenerationRequest(
                        Seed: request.GenerationSeed,
                        Country: country,
                        Tier: targetTier,
                        Season: season,
                        // A provisioned tier is not claimable until its backfill and validation complete
                        // (PYR-8); activation happens in phase four.
                        Activate: false,
                        BootstrapCutoff: now),
                    now);

                var run = GenerationRun.Start(
                    Guid.CreateVersion7(),
                    GenerationRunKind.DivisionProvisioning,
                    request.GenerationSeed,
                    DivisionProvisioningGenerator.Version,
                    DivisionProvisioningGenerator.InputHashFor(
                        request.GenerationSeed,
                        country.Code,
                        targetTier,
                        WorldRuleSet.ClubsPerDivision),
                    now);

                run.Succeed(new GenerationRunCounts(0, generated.Clubs, generated.Players, generated.Accounts), now);
                _generationRuns.Add(run);

                divisionSeasonId = generated.DivisionSeasonId;
                clubs = generated.Clubs;
                players = generated.Players;
                accounts = generated.Accounts;
            }
            else
            {
                var divisionSeason = await _world.FindDivisionSeasonAsync(existing.Id, season.Id, cancellationToken)
                    ?? throw new PermanentJobFailureException(
                        $"Division {existing.Id} has no season {season.Id} instance to resume (PYR-8).");

                divisionSeasonId = divisionSeason.Id;
                clubs = 0;
                players = 0;
                accounts = 0;
            }

            _audit.Record(new AuditEntry(
                WorldAuditActions.ProvisioningStarted,
                AuditActorTypes.Service,
                _requestContext.ActorUserId,
                AuditTargetTypes.DivisionProvisioningRequest,
                request.Id,
                _requestContext.CorrelationId,
                IpHash: null,
                Reason: $"{country.Code} tier {targetTier}"));

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // Phase two: give the eighteen new AI clubs a default side and training plan (INS-12).
        await _aiClubs.ExecuteAsync(cancellationToken);

        // Phase three: backfill the matchdays already passed this season, in round order (PYR-6).
        var backfilled = 0;

        foreach (var matchday in await _matchdays.LoadDivisionMatchdaysAsync(divisionSeasonId, cancellationToken))
        {
            if (matchday.KickoffAt > now)
            {
                break;
            }

            await _lock.ExecuteAsync(matchday.Id, cancellationToken);
            await _resolve.ExecuteAsync(matchday.Id, jobId: null, cancellationToken);
            await _publish.ExecuteAsync(matchday.Id, cancellationToken);

            backfilled++;
        }

        // Phase four: validate, then activate, under the country lock so a concurrent takeover's capacity
        // evaluation and this activation cannot disagree (PYR-3, PYR-8).
        await using (var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken))
        {
            await _locks.AcquireAsync(AdvisoryLockKey.Country(countryId), cancellationToken);

            var problems = await ValidateAsync(divisionSeasonId, cancellationToken);
            var request = await _requests.FindByIdAsync(requestId, cancellationToken)
                ?? throw new PermanentJobFailureException($"Provisioning request {requestId} vanished (PYR-2).");

            if (problems.Count > 0)
            {
                var diagnostics = string.Join("; ", problems);

                request.Fail(diagnostics, now);

                _audit.Record(new AuditEntry(
                    WorldAuditActions.ProvisioningFailed,
                    AuditActorTypes.Service,
                    _requestContext.ActorUserId,
                    AuditTargetTypes.DivisionProvisioningRequest,
                    request.Id,
                    _requestContext.CorrelationId,
                    IpHash: null,
                    Reason: diagnostics));

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                LogFailed(countryId, targetTier, diagnostics);

                // Deterministic generation should reproduce, so a validation failure is a defect rather than
                // a transient fault: dead-letter it and alert operations instead of burning the retry budget.
                throw new PermanentJobFailureException(
                    $"Provisioned tier {targetTier} of country {countryId} failed validation: {diagnostics} (PYR-8).");
            }

            var division = await _world.FindDivisionByTierAsync(countryId, targetTier, cancellationToken)
                ?? throw new PermanentJobFailureException(
                    $"The provisioned tier {targetTier} of country {countryId} vanished before activation (PYR-8).");

            division.Activate(now);
            request.Complete(now);

            // The activation is news for the whole world, not one club: a new tier exists (COM-1).
            var newsCountry = await _world.FindCountryAsync(countryId, cancellationToken);
            var newsWorld = await _world.FindWorldAsync(cancellationToken);

            if (newsCountry is not null && newsWorld is not null)
            {
                _news.DivisionActivated(
                    newsWorld.Id,
                    countryId,
                    division.Id,
                    newsCountry.DisplayName,
                    targetTier,
                    division.DisplayName);
            }

            _audit.Record(new AuditEntry(
                WorldAuditActions.ProvisioningCompleted,
                AuditActorTypes.Service,
                _requestContext.ActorUserId,
                AuditTargetTypes.DivisionProvisioningRequest,
                request.Id,
                _requestContext.CorrelationId,
                IpHash: null,
                Reason: $"{targetTier}"));

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // PYR-11: the tier that just filled might already be full, so growth continues by the same path.
            await _capacity.EnsureNextTierRequestedAsync(countryId, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        LogCompleted(countryId, targetTier, clubs, backfilled);

        return new ProvisionDivisionResult(
            ProvisionDivisionOutcome.Completed,
            countryId,
            targetTier,
            clubs,
            players,
            accounts,
            backfilled);
    }

    /// <summary>Checks the tier against the invariants activation requires (`PYR-8`).</summary>
    private async Task<List<string>> ValidateAsync(Guid divisionSeasonId, CancellationToken cancellationToken)
    {
        var problems = new List<string>();

        var clubs = await _queries.GetAvailableClubsAsync(divisionSeasonId, cancellationToken);

        if (clubs.Count != WorldRuleSet.ClubsPerDivision)
        {
            problems.Add($"expected {WorldRuleSet.ClubsPerDivision} clubs, found {clubs.Count}");
        }

        foreach (var club in clubs)
        {
            var composition = await _roster.GetCompositionAsync(club.Id, cancellationToken);

            if (composition is null)
            {
                problems.Add($"club {club.Id} has no registered squad");

                continue;
            }

            if (composition.RegisteredCount < WorldRuleSet.SquadMinimumRegistered)
            {
                problems.Add($"club {club.Id} registers {composition.RegisteredCount} players (SQ-2)");
            }

            if (composition.GoalkeeperCount < WorldRuleSet.MinimumGoalkeepers)
            {
                problems.Add($"club {club.Id} registers {composition.GoalkeeperCount} goalkeepers (SQ-2)");
            }
        }

        // The table and season statistics must be exactly what the published bootstrap results compute
        // (TBL-13). A fresh tier with no passed matchdays reconciles to its opening table.
        var rebuild = await _rebuild.ExecuteAsync(divisionSeasonId, apply: false, cancellationToken);

        if (rebuild.Outcome != ProjectionRebuildOutcome.Reconciled)
        {
            problems.Add($"standings did not reconcile with published results ({rebuild.Outcome})");
        }

        return problems;
    }

    [LoggerMessage(
        EventId = 5101,
        Level = LogLevel.Information,
        Message = "Provisioned tier {TargetTier} of country {CountryId}: {Clubs} clubs, {Rounds} rounds backfilled (PYR-4).")]
    private partial void LogCompleted(Guid countryId, int targetTier, int clubs, int rounds);

    [LoggerMessage(
        EventId = 5102,
        Level = LogLevel.Error,
        Message = "Provisioning tier {TargetTier} of country {CountryId} failed validation: {Diagnostics} (PYR-8).")]
    private partial void LogFailed(Guid countryId, int targetTier, string diagnostics);
}
