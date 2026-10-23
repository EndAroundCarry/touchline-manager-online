using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Jobs;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Jobs;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.World;

/// <summary>What a diagnostics provisioning trigger did (`PYR-4`).</summary>
public enum TriggerProvisioningOutcome
{
    /// <summary>A request exists and its provisioning job was enqueued.</summary>
    Enqueued = 0,

    /// <summary>The country does not exist.</summary>
    CountryNotFound = 1,

    /// <summary>The country has no league to hang the next one from.</summary>
    InvalidTier = 2,

    /// <summary>No season exists to attach the new tier to.</summary>
    NoSeason = 3,
}

/// <summary>The result of a diagnostics provisioning trigger.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="CountryId">The country whose league was requested.</param>
/// <param name="TargetTier">The tier of the league requested.</param>
/// <param name="TargetLeague">The league number within its tier.</param>
/// <param name="RequestId">The provisioning request, when one was found or created.</param>
/// <param name="BusinessKey">The business key of the enqueued job.</param>
/// <param name="Enqueued">Whether the job row was newly inserted.</param>
public sealed record TriggerProvisioningResult(
    TriggerProvisioningOutcome Outcome,
    Guid CountryId,
    int TargetTier,
    int TargetLeague,
    Guid? RequestId,
    string BusinessKey,
    bool Enqueued);

/// <summary>
/// Creates the next league's provisioning request and enqueues its job, due now, so a non-production environment can
/// grow the pyramid without waiting for the free clubs to fall to the trigger (`PYR-2`, `PYR-4`).
/// </summary>
/// <remarks>
/// It does what a takeover's capacity evaluation does and then what the worker-only scheduler does, and nothing
/// more: the worker still generates, backfills, validates, and activates the league exactly as it would on the
/// calendar. The league is the one <see cref="PyramidShape.NextLeague"/> names, and the seed is derived the same way
/// <see cref="CapacityEvaluator"/> derives it, so a triggered league is reproducible identically to a natural one
/// (`PYR-14`). It has no production surface — the endpoint that
/// reaches it is mapped only when the diagnostics flag is on.
/// </remarks>
public sealed class TriggerProvisioning
{
    private readonly IWorldRepository _world;
    private readonly IDivisionProvisioningRequestRepository _requests;
    private readonly IJobQueue _jobs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public TriggerProvisioning(
        IWorldRepository world,
        IDivisionProvisioningRequestRepository requests,
        IJobQueue jobs,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _world = world;
        _requests = requests;
        _jobs = jobs;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>Requests the next league of the pyramid and enqueues its provisioning job.</summary>
    /// <param name="countryId">The country whose pyramid grows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TriggerProvisioningResult> ExecuteAsync(
        Guid countryId,
        CancellationToken cancellationToken)
    {
        var country = await _world.FindCountryAsync(countryId, cancellationToken);
        var world = await _world.FindWorldAsync(cancellationToken);

        if (country is null || world is null)
        {
            return new TriggerProvisioningResult(
                TriggerProvisioningOutcome.CountryNotFound, countryId, 0, 0, null, string.Empty, false);
        }

        var divisions = await _world.ListDivisionsAsync(countryId, cancellationToken);
        var target = PyramidShape.NextLeague([.. divisions.Select(division => (division.TierNumber, division.LeagueNumber))]);
        var targetTier = target.Tier;
        var targetLeague = target.League;

        if (target.ParentLeague is not { } parentLeague)
        {
            return new TriggerProvisioningResult(
                TriggerProvisioningOutcome.InvalidTier, countryId, targetTier, targetLeague, null, string.Empty, false);
        }

        var request = await _requests.FindAsync(countryId, targetTier, targetLeague, cancellationToken);

        if (request is null)
        {
            var season = await ResolveTargetSeasonAsync(world, cancellationToken);

            if (season is null)
            {
                return new TriggerProvisioningResult(
                    TriggerProvisioningOutcome.NoSeason, countryId, targetTier, targetLeague, null, string.Empty, false);
            }

            var parent = divisions.FirstOrDefault(
                division => division.TierNumber == targetTier - 1 && division.LeagueNumber == parentLeague);

            if (parent is null)
            {
                return new TriggerProvisioningResult(
                    TriggerProvisioningOutcome.InvalidTier, countryId, targetTier, targetLeague, null, string.Empty, false);
            }

            request = DivisionProvisioningRequest.Request(
                Guid.CreateVersion7(),
                countryId,
                targetTier,
                targetLeague,
                parent.Id,
                season.Id,
                CapacityEvaluator.GenerationSeedFor(world.Id, country.Code, targetTier, targetLeague),
                _clock.UtcNow);

            _requests.Add(request);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var key = ProvisioningJobTypes.ProvisionKey(countryId, targetTier, targetLeague);

        var enqueued = await _jobs.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = ProvisionDivisionJobHandler.TypeName,
                BusinessKey = key,
                DueAt = _clock.UtcNow,
                PayloadJson = ProvisioningJobPayload.For(request.Id),
            },
            cancellationToken);

        return new TriggerProvisioningResult(
            TriggerProvisioningOutcome.Enqueued,
            countryId,
            targetTier,
            targetLeague,
            request.Id,
            key,
            enqueued);
    }

    /// <summary>
    /// Resolves the season a triggered request targets, mirroring <see cref="CapacityEvaluator"/> (`PYR-9`).
    /// </summary>
    private async Task<Season?> ResolveTargetSeasonAsync(GameWorld world, CancellationToken cancellationToken)
    {
        var current = await _world.FindSeasonAsync(world.Id, world.CurrentSeasonNumber, cancellationToken);

        if (current is null)
        {
            return null;
        }

        return current.Status == SeasonStatus.Active
            ? current
            : await _world.FindSeasonAsync(world.Id, world.CurrentSeasonNumber + 1, cancellationToken);
    }
}
