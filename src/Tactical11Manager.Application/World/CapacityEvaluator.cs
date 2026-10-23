using Microsoft.Extensions.Options;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Contracts.World;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;
using Tactical11Manager.Domain.World.Generation;

namespace Tactical11Manager.Application.World;

/// <summary>
/// Decides whether a country's pyramid should grow, and records the request when it should (`PYR-2`, `PYR-15`).
/// </summary>
/// <remarks>
/// <para>
/// Extracted from the takeover command because it is the one piece of that workflow with a rule of its
/// own, and because Stage 11 runs exactly this evaluation when the provisioning worker finishes and when a
/// tenure changes state. Two callers deciding "is it time to grow?" separately is how a product ends up
/// with two different answers to it.
/// </para>
/// <para>
/// <b>The caller must hold the country's advisory lock and be inside a transaction.</b> The evaluation
/// reads a count and then writes a row that must not duplicate a peer's; without the lock, two takeovers
/// can both see the free clubs at the trigger and both insert.
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
    /// Creates the next league's provisioning request when the country needs one and none is in flight.
    /// </summary>
    /// <remarks>
    /// A league is requested once the free clubs fall to <see cref="WorldRuleSet.ExpansionFreeClubsPercent"/> per cent of
    /// the clubs in the active leagues, and it is the league <see cref="PyramidShape.NextLeague"/> names. A country grows
    /// one league at a time: a request that is still requested or running is returned as it is, so a rush of claims never
    /// makes two overlapping requests, and growth then chains through the re-check after each activation (`PYR-2`,
    /// `PYR-11`).
    /// </remarks>
    /// <param name="countryId">The country to evaluate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The request's state, whether it was just created or already existed, or <see langword="null"/> when
    /// the country still has room and no league is needed.
    /// </returns>
    public async Task<ProvisioningStatusResponse?> EnsureNextLeagueRequestedAsync(
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
            capacity.ClubsInCountry,
            capacity.HumanOccupiedClubs,
            capacity.Leagues);

        if (!rules.NeedsExpansion)
        {
            return null;
        }

        var inFlight = await _requests.FindInFlightAsync(countryId, cancellationToken);

        if (inFlight is not null)
        {
            return inFlight.ToProvisioningResponse(_options.ProvisioningPollSeconds);
        }

        var target = rules.TargetLeague;
        var existing = await _requests.FindAsync(countryId, target.Tier, target.League, cancellationToken);

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

        // The league it feeds is the one the shape names in the tier above; a league that is still provisioning has its
        // division row already, so the parent is found whatever its status.
        var parent = target.ParentLeague is { } parentLeague
            ? await _world.FindDivisionByLeagueAsync(countryId, target.Tier - 1, parentLeague, cancellationToken)
            : null;

        if (parent is null)
        {
            return null;
        }

        // The current season is the target, unless it is rolling over — then the request targets the next
        // season so the closing one is never mutated (`PYR-5`, `PYR-9`).
        var season = await ResolveTargetSeasonAsync(world, cancellationToken);

        if (season is null)
        {
            return null;
        }

        var now = _clock.UtcNow;

        var request = DivisionProvisioningRequest.Request(
            Guid.CreateVersion7(),
            countryId,
            target.Tier,
            target.League,
            parent.Id,
            season.Id,
            GenerationSeedFor(world.Id, country.Code, target.Tier, target.League),
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
            Reason: $"{country.Code} league {target.Tier}.{target.League}"));

        return request.ToProvisioningResponse(_options.ProvisioningPollSeconds);
    }

    /// <summary>
    /// Resolves the season a provisioning request targets (`PYR-5`, `PYR-9`).
    /// </summary>
    /// <remarks>
    /// The current season when it is being played, or the next one while it is rolling over, so a request made
    /// during the rollover window never mutates the closing season. If the next season does not exist yet — the
    /// rollover has not reached its move phase — the request is deferred, and the next capacity evaluation
    /// retries it.
    /// </remarks>
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

    /// <summary>
    /// Derives the seed for a provisioned league (`PYR-14`).
    /// </summary>
    /// <remarks>
    /// Derived from the world's own identity and the country's stable code rather than from mutable state,
    /// so a retried provisioning run reuses the same seed. That is what keeps two attempts at the same
    /// league attempts at the same league — the property <c>DivisionProvisioningRequest.Retry</c> depends on. The league
    /// number is left out for the first league of a tier, so division 2.1's seed is what it was before a tier could hold
    /// several, and every other league of the tier draws its own.
    /// </remarks>
    internal static string GenerationSeedFor(Guid worldId, string countryCode, int targetTier, int targetLeague = 1)
    {
        var tier = targetTier.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return DeterministicDigest.Of(
            worldId.ToString(),
            countryCode,
            targetLeague == 1
                ? tier
                : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{tier}.{targetLeague}"),
            "provision");
    }
}
