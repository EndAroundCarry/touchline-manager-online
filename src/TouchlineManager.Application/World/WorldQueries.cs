using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.World;

/// <summary>What happened when the world was read.</summary>
public enum GetWorldOutcome
{
    /// <summary>The world exists.</summary>
    Found = 0,

    /// <summary>No world has been seeded yet.</summary>
    NotSeeded = 1,
}

/// <summary>The result of a world read.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="World">The world, when one exists.</param>
public sealed record GetWorldResult(GetWorldOutcome Outcome, WorldResponse? World);

/// <summary>
/// Reads the world a manager is onboarding into (master plan §10.2).
/// </summary>
/// <remarks>
/// An unseeded world is reported as a distinct outcome rather than as an empty world, because the two
/// mean different things to whoever is looking: an empty world is a product state, and no world at all is
/// an operator who has not run the seeder.
/// </remarks>
public sealed class GetWorld
{
    private readonly Abstractions.IClock _clock;
    private readonly IWorldRepository _world;

    /// <summary>Initializes the query.</summary>
    public GetWorld(Abstractions.IClock clock, IWorldRepository world)
    {
        _clock = clock;
        _world = world;
    }

    /// <summary>Reads the world and its running season.</summary>
    public async Task<GetWorldResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var world = await _world.FindWorldAsync(cancellationToken);

        if (world is null)
        {
            return new GetWorldResult(GetWorldOutcome.NotSeeded, null);
        }

        var season = await _world.FindSeasonAsync(world.Id, world.CurrentSeasonNumber, cancellationToken);

        return new GetWorldResult(
            GetWorldOutcome.Found,
            new WorldResponse(
                world.Id,
                world.Name,
                world.Status.ToCode(),
                world.RuleSetVersion,
                world.CurrentSeasonNumber,
                world.AcceptsClaims,
                season?.ToResponse(),
                _clock.UtcNow));
    }
}

/// <summary>
/// Lists the countries a manager may join (master plan §10.2).
/// </summary>
/// <remarks>
/// Returns an empty list rather than failing before the world is seeded. Onboarding shows "nothing to
/// join yet" from the list it was given, which is the same code path it uses when every country is
/// temporarily out of capacity — a second, unreachable branch would be a branch nobody would ever test.
/// </remarks>
public sealed class ListCountries
{
    private readonly IWorldRepository _world;

    /// <summary>Initializes the query.</summary>
    public ListCountries(IWorldRepository world) => _world = world;

    /// <summary>Lists the world's countries in presentation order.</summary>
    public async Task<IReadOnlyList<CountrySummaryResponse>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var world = await _world.FindWorldAsync(cancellationToken);

        if (world is null)
        {
            return [];
        }

        var countries = await _world.ListCountriesAsync(world.Id, cancellationToken);

        return [.. countries.Select(country => country.ToResponse())];
    }
}

/// <summary>What happened when a country's capacity was measured.</summary>
public enum CountryCapacityOutcome
{
    /// <summary>Measured.</summary>
    Found = 0,

    /// <summary>No such country.</summary>
    CountryNotFound = 1,

    /// <summary>The country has no active division, so there is nothing to join.</summary>
    NoActiveDivision = 2,
}

/// <summary>The result of a capacity read.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Capacity">The measurement, when one was possible.</param>
public sealed record CountryCapacityResult(
    CountryCapacityOutcome Outcome,
    CountryCapacityResponse? Capacity);

/// <summary>
/// Measures a country's lowest active tier and the state of its next-tier generation (`PYR-1`, `PYR-10`).
/// </summary>
public sealed class GetCountryCapacity
{
    private readonly Abstractions.IClock _clock;
    private readonly IWorldRepository _world;
    private readonly IOnboardingQueries _queries;
    private readonly IDivisionProvisioningRequestRepository _requests;
    private readonly WorldOptions _options;

    /// <summary>Initializes the query.</summary>
    public GetCountryCapacity(
        Abstractions.IClock clock,
        IWorldRepository world,
        IOnboardingQueries queries,
        IDivisionProvisioningRequestRepository requests,
        IOptions<WorldOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _clock = clock;
        _world = world;
        _queries = queries;
        _requests = requests;
        _options = options.Value;
    }

    /// <summary>Measures the country.</summary>
    /// <param name="countryId">The country to measure.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CountryCapacityResult> ExecuteAsync(
        Guid countryId,
        CancellationToken cancellationToken)
    {
        var country = await _world.FindCountryAsync(countryId, cancellationToken);

        if (country is null)
        {
            return new CountryCapacityResult(CountryCapacityOutcome.CountryNotFound, null);
        }

        var capacity = await _queries.GetCountryCapacityAsync(countryId, cancellationToken);

        if (capacity is null)
        {
            return new CountryCapacityResult(CountryCapacityOutcome.NoActiveDivision, null);
        }

        return new CountryCapacityResult(
            CountryCapacityOutcome.Found,
            await BuildResponseAsync(capacity, cancellationToken));
    }

    private async Task<CountryCapacityResponse> BuildResponseAsync(
        CountryCapacitySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var rules = CountryCapacity.Measure(
            snapshot.CountryId,
            snapshot.LowestActiveTier,
            snapshot.DivisionId,
            snapshot.ClubsInLowestTier,
            snapshot.HumanOccupiedClubs);

        // The provisioning record is only interesting while the tier is full: that is the only time
        // onboarding is actually waiting on it.
        var provisioning = rules.NeedsExpansion
            ? await _requests.FindAsync(snapshot.CountryId, rules.TargetTierForExpansion, cancellationToken)
            : null;

        return new CountryCapacityResponse(
            snapshot.CountryId,
            snapshot.LowestActiveTier,
            snapshot.DivisionId,
            snapshot.DivisionName,
            snapshot.ClubsInLowestTier,
            snapshot.HumanOccupiedClubs,
            rules.AvailableClubs,
            rules.LowestTierIsFull,
            rules.TargetTierForExpansion,
            provisioning?.ToProvisioningResponse(_options.ProvisioningPollSeconds),
            _clock.UtcNow);
    }
}

/// <summary>What happened when a country's available clubs were listed.</summary>
public enum AvailableClubsOutcome
{
    /// <summary>Listed.</summary>
    Found = 0,

    /// <summary>No such country.</summary>
    CountryNotFound = 1,

    /// <summary>The country has no active division.</summary>
    NoActiveDivision = 2,
}

/// <summary>The result of an available-clubs read.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Clubs">The listing, when one was possible.</param>
public sealed record AvailableClubsResult(AvailableClubsOutcome Outcome, AvailableClubsResponse? Clubs);

/// <summary>
/// Lists the clubs a manager may take over in a country's lowest active tier (`WORLD-8`, master plan §10.2).
/// </summary>
/// <remarks>
/// The list is the whole tier, with taken clubs marked unavailable rather than hidden. A manager choosing
/// between countries wants to see that a league is nearly full — hiding the taken clubs would make a
/// crowded country look identical to an empty one.
/// </remarks>
public sealed class GetAvailableClubs
{
    private readonly Abstractions.IClock _clock;
    private readonly IWorldRepository _world;
    private readonly IOnboardingQueries _queries;

    /// <summary>Initializes the query.</summary>
    public GetAvailableClubs(
        Abstractions.IClock clock,
        IWorldRepository world,
        IOnboardingQueries queries)
    {
        _clock = clock;
        _world = world;
        _queries = queries;
    }

    /// <summary>Lists the claimable tier.</summary>
    /// <param name="countryId">The country to list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<AvailableClubsResult> ExecuteAsync(
        Guid countryId,
        CancellationToken cancellationToken)
    {
        var country = await _world.FindCountryAsync(countryId, cancellationToken);

        if (country is null)
        {
            return new AvailableClubsResult(AvailableClubsOutcome.CountryNotFound, null);
        }

        var capacity = await _queries.GetCountryCapacityAsync(countryId, cancellationToken);

        if (capacity is null)
        {
            return new AvailableClubsResult(AvailableClubsOutcome.NoActiveDivision, null);
        }

        var clubs = await _queries.GetAvailableClubsAsync(capacity.DivisionSeasonId, cancellationToken);

        return new AvailableClubsResult(
            AvailableClubsOutcome.Found,
            new AvailableClubsResponse(
                countryId,
                capacity.DivisionId,
                capacity.DivisionName,
                capacity.LowestActiveTier,
                [.. clubs.Select(club => new AvailableClubResponse(
                    club.Id,
                    club.Name,
                    club.ShortName,
                    club.City,
                    club.Region,
                    club.BadgeSeed,
                    club.Reputation,
                    club.StadiumBaseline,
                    club.IsAvailable))],
                _clock.UtcNow));
    }
}

/// <summary>
/// Projects a provisioning request for the client (`PYR-10`).
/// </summary>
internal static class ProvisioningMapping
{
    /// <summary>Projects the request, with the polling hint the client should honour.</summary>
    public static ProvisioningStatusResponse ToProvisioningResponse(
        this Domain.World.DivisionProvisioningRequest request,
        int pollAfterSeconds)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ProvisioningStatusResponse(
            request.Id,
            request.TargetTier,
            request.Status.ToCode(),
            request.RequestedAt,
            request.StartedAt,
            request.CompletedAt,
            pollAfterSeconds);
    }
}
