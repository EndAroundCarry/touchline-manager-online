using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Jobs;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.World;

/// <summary>What a diagnostics provisioning trigger did (`PYR-4`).</summary>
public enum TriggerProvisioningOutcome
{
    /// <summary>A request exists and its provisioning job was enqueued.</summary>
    Enqueued = 0,

    /// <summary>The country does not exist.</summary>
    CountryNotFound = 1,

    /// <summary>The target tier is not one that can be provisioned (tier 1 is seeded; tiers start at 2).</summary>
    InvalidTier = 2,

    /// <summary>No season exists to attach the new tier to.</summary>
    NoSeason = 3,
}

/// <summary>The result of a diagnostics provisioning trigger.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="CountryId">The country whose tier was requested.</param>
/// <param name="TargetTier">The tier requested.</param>
/// <param name="RequestId">The provisioning request, when one was found or created.</param>
/// <param name="BusinessKey">The business key of the enqueued job.</param>
/// <param name="Enqueued">Whether the job row was newly inserted.</param>
public sealed record TriggerProvisioningResult(
    TriggerProvisioningOutcome Outcome,
    Guid CountryId,
    int TargetTier,
    Guid? RequestId,
    string BusinessKey,
    bool Enqueued);

/// <summary>
/// Creates a tier's provisioning request and enqueues its job, due now, so a non-production environment can
/// grow the pyramid without waiting for a takeover to fill a tier (`PYR-2`, `PYR-4`).
/// </summary>
/// <remarks>
/// It does what a takeover's capacity evaluation does and then what the worker-only scheduler does, and nothing
/// more: the worker still generates, backfills, validates, and activates the tier exactly as it would on the
/// calendar. The seed is derived the same way <see cref="CapacityEvaluator"/> derives it, so a triggered tier
/// is reproducible identically to a natural one (`PYR-14`). It has no production surface — the endpoint that
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

    /// <summary>Requests a tier and enqueues its provisioning job.</summary>
    /// <param name="countryId">The country whose pyramid grows.</param>
    /// <param name="targetTier">The tier to create, at least 2.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TriggerProvisioningResult> ExecuteAsync(
        Guid countryId,
        int targetTier,
        CancellationToken cancellationToken)
    {
        if (targetTier < 2)
        {
            return new TriggerProvisioningResult(
                TriggerProvisioningOutcome.InvalidTier, countryId, targetTier, null, string.Empty, false);
        }

        var country = await _world.FindCountryAsync(countryId, cancellationToken);
        var world = await _world.FindWorldAsync(cancellationToken);

        if (country is null || world is null)
        {
            return new TriggerProvisioningResult(
                TriggerProvisioningOutcome.CountryNotFound, countryId, targetTier, null, string.Empty, false);
        }

        var request = await _requests.FindAsync(countryId, targetTier, cancellationToken);

        if (request is null)
        {
            var season = await _world.FindSeasonAsync(world.Id, world.CurrentSeasonNumber, cancellationToken);

            if (season is null)
            {
                return new TriggerProvisioningResult(
                    TriggerProvisioningOutcome.NoSeason, countryId, targetTier, null, string.Empty, false);
            }

            request = DivisionProvisioningRequest.Request(
                Guid.CreateVersion7(),
                countryId,
                targetTier,
                season.Id,
                CapacityEvaluator.GenerationSeedFor(world.Id, country.Code, targetTier),
                _clock.UtcNow);

            _requests.Add(request);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var key = ProvisioningJobTypes.ProvisionKey(countryId, targetTier);

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
            request.Id,
            key,
            enqueued);
    }
}
