using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World;
using TouchlineManager.Domain.World.Generation;

namespace TouchlineManager.Application.World;

/// <summary>
/// Decides whether a country's pyramid should grow, and records the request when it should (`PYR-2`).
/// </summary>
/// <remarks>
/// <para>
/// Extracted from the takeover command because it is the one piece of that workflow with a rule of its
/// own, and because Stage 11 runs exactly this evaluation when the provisioning worker finishes and when a
/// tenure changes state. Two callers deciding "is this country full?" separately is how a product ends up
/// with two different answers to it.
/// </para>
/// <para>
/// <b>The caller must hold the country's advisory lock and be inside a transaction.</b> The evaluation
/// reads a count and then writes a row that must not duplicate a peer's; without the lock, two takeovers
/// can both see a full tier and both insert.
/// </para>
/// </remarks>
public sealed class CapacityEvaluator
{
    private readonly IClock _clock;
    private readonly IWorldRepository _world;
    private readonly IOnboardingQueries _queries;
    private readonly IDivisionProvisioningRequestRepository _requests;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly WorldOptions _options;

    /// <summary>Initializes the evaluator.</summary>
    public CapacityEvaluator(
        IClock clock,
        IWorldRepository world,
        IOnboardingQueries queries,
        IDivisionProvisioningRequestRepository requests,
        IAuditWriter audit,
        IRequestContext requestContext,
        IOptions<WorldOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _clock = clock;
        _world = world;
        _queries = queries;
        _requests = requests;
        _audit = audit;
        _requestContext = requestContext;
        _options = options.Value;
    }

    /// <summary>
    /// Creates the next tier's provisioning request when the country's lowest active tier is full and no
    /// request exists yet.
    /// </summary>
    /// <param name="countryId">The country to evaluate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The request's state, whether it was just created or already existed, or <see langword="null"/> when
    /// the country still has room and no tier is needed.
    /// </returns>
    public async Task<ProvisioningStatusResponse?> EnsureNextTierRequestedAsync(
        Guid countryId,
        CancellationToken cancellationToken)
    {
        var capacity = await _queries.GetCountryCapacityAsync(countryId, cancellationToken);

        if (capacity is null)
        {
            return null;
        }

        var rules = CountryCapacity.Measure(
            capacity.CountryId,
            capacity.LowestActiveTier,
            capacity.DivisionId,
            capacity.ClubsInLowestTier,
            capacity.HumanOccupiedClubs);

        if (!rules.NeedsExpansion)
        {
            return null;
        }

        var targetTier = rules.TargetTierForExpansion;
        var existing = await _requests.FindAsync(countryId, targetTier, cancellationToken);

        if (existing is not null)
        {
            return existing.ToProvisioningResponse(_options.ProvisioningPollSeconds);
        }

        var world = await _world.FindWorldAsync(cancellationToken);
        var country = await _world.FindCountryAsync(countryId, cancellationToken);

        if (world is null || country is null)
        {
            return null;
        }

        // The current season is the target. PYR-9 makes this the *next* season when rollover holds the
        // country lock, which is a Stage 12 refinement of this one line.
        var season = await _world.FindSeasonAsync(world.Id, world.CurrentSeasonNumber, cancellationToken);

        if (season is null)
        {
            return null;
        }

        var now = _clock.UtcNow;

        var request = DivisionProvisioningRequest.Request(
            Guid.CreateVersion7(),
            countryId,
            targetTier,
            season.Id,
            GenerationSeedFor(world.Id, country.Code, targetTier),
            now);

        _requests.Add(request);

        _audit.Record(new AuditEntry(
            WorldAuditActions.ProvisioningRequested,
            AuditActorTypes.Service,
            _requestContext.ActorUserId,
            AuditTargetTypes.DivisionProvisioningRequest,
            request.Id,
            _requestContext.CorrelationId,
            IpHash: null,
            Reason: $"{country.Code} tier {targetTier}"));

        return request.ToProvisioningResponse(_options.ProvisioningPollSeconds);
    }

    /// <summary>
    /// Derives the seed for a provisioned tier (`PYR-14`).
    /// </summary>
    /// <remarks>
    /// Derived from the world's own identity and the country's stable code rather than from mutable state,
    /// so a retried provisioning run reuses the same seed. That is what keeps two attempts at the same
    /// tier attempts at the same tier — the property <c>DivisionProvisioningRequest.Retry</c> depends on.
    /// </remarks>
    private static string GenerationSeedFor(Guid worldId, string countryCode, int targetTier) =>
        DeterministicDigest.Of(
            worldId.ToString(),
            countryCode,
            targetTier.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "provision");
}
