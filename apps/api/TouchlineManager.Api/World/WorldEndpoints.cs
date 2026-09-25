using FluentValidation;
using TouchlineManager.Api.Auth;
using TouchlineManager.Api.Http;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Http;
using TouchlineManager.Contracts.World;

namespace TouchlineManager.Api.World;

/// <summary>
/// The world and onboarding HTTP surface (master plan §10.2, §10.3).
/// </summary>
/// <remarks>
/// <para>
/// These endpoints sit at the version root rather than under <c>/api/v1/world</c>, because master plan
/// §10.2 addresses them as resources — <c>/countries</c>, <c>/club-claims</c>, <c>/club-tenure</c> — and
/// the tuple of them is the onboarding journey rather than one module's namespace. It is the same
/// reasoning that puts <c>/me</c> at the root: a committed API path is a contract, and moving one to suit
/// an internal grouping would break every client that already reads it.
/// </para>
/// <para>
/// Handlers do three things: validate the request, call one use case, and translate the outcome into a
/// status and a stable error code. Every rule about clubs, capacity, and cooldowns lives in the use case,
/// which is what lets the world-seeder and the tests invoke the same behaviour without HTTP.
/// </para>
/// </remarks>
internal static class WorldEndpoints
{
    /// <summary>
    /// The longest idempotency key accepted.
    /// </summary>
    /// <remarks>
    /// Matches <c>world.club_tenures.takeover_idempotency_key</c>. A longer key would be truncated by the
    /// database or rejected by it, and either way the retry would stop matching the original attempt.
    /// </remarks>
    private const int IdempotencyKeyMaxLength = 80;

    /// <summary>Maps the world and onboarding routes.</summary>
    public static IEndpointRouteBuilder MapWorldEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup(Endpoints.ModuleEndpointGroups.VersionPrefix)
            .WithTags("world")
            .RequireAuthorization();

        group.MapGet("/world", GetWorldAsync)
            .WithName("GetWorld")
            .WithSummary("Reads the world a manager is onboarding into.")
            .Produces<WorldResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/countries", ListCountriesAsync)
            .WithName("ListCountries")
            .WithSummary("Lists the countries a manager may join.")
            .Produces<IReadOnlyList<CountrySummaryResponse>>(StatusCodes.Status200OK);

        group.MapGet("/countries/{countryId:guid}/capacity", GetCountryCapacityAsync)
            .WithName("GetCountryCapacity")
            .WithSummary("Measures the country's lowest active tier and its next-tier generation.")
            .Produces<CountryCapacityResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/countries/{countryId:guid}/available-clubs", GetAvailableClubsAsync)
            .WithName("GetAvailableClubs")
            .WithSummary("Lists the clubs a manager may take over, with availability.")
            .Produces<AvailableClubsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/manager-profile", CreateManagerProfileAsync)
            .WithName("CreateManagerProfile")
            .WithSummary("Creates the account's manager profile.")
            .Produces<ManagerProfileResponse>(StatusCodes.Status201Created)
            .Produces<ManagerProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/club-claims", ClaimClubAsync)
            .RequireAuthorization(AuthorizationPolicies.VerifiedManager)
            .WithName("ClaimClub")
            .WithSummary("Takes over an AI-controlled club in the country's lowest active tier.")
            .WithDescription(
                "Requires an Idempotency-Key. The same key always produces the same outcome, so a retry "
                + "after a timeout cannot create a second tenure. Answers 409 CAPACITY_PROVISIONING when "
                + "every club in the tier is held, with the next tier's generation state and a polling hint.")
            .Produces<ClubDashboardResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/club-tenure/resign", ResignClubAsync)
            .WithName("ResignClub")
            .WithSummary("Resigns from the manager's club and starts the takeover cooldown.")
            .Produces<OnboardingStateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/club-tenure", GetTenureAsync)
            .WithName("GetClubTenure")
            .WithSummary("Reads the account's manager profile and current club.")
            .Produces<OnboardingStateResponse>(StatusCodes.Status200OK);

        group.MapGet("/clubs/{clubId:guid}/dashboard", GetClubDashboardAsync)
            .WithName("GetClubDashboard")
            .WithSummary("Reads the inherited-club dashboard.")
            .Produces<ClubDashboardResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetWorldAsync(GetWorld query, CancellationToken cancellationToken)
    {
        var result = await query.ExecuteAsync(cancellationToken);

        return result.Outcome == GetWorldOutcome.Found
            ? Results.Ok(result.World)
            : ProblemResults.Code(
                StatusCodes.Status404NotFound,
                WorldErrorCodes.WorldNotSeeded,
                "No world yet.",
                "The world has not been created. Run the world seeder before onboarding.");
    }

    private static async Task<IResult> ListCountriesAsync(
        ListCountries query,
        CancellationToken cancellationToken) =>
        Results.Ok(await query.ExecuteAsync(cancellationToken));

    private static async Task<IResult> GetCountryCapacityAsync(
        Guid countryId,
        GetCountryCapacity query,
        CancellationToken cancellationToken)
    {
        var result = await query.ExecuteAsync(countryId, cancellationToken);

        return result.Outcome switch
        {
            CountryCapacityOutcome.Found => Results.Ok(result.Capacity),
            CountryCapacityOutcome.CountryNotFound => CountryNotFound(),
            _ => NoActiveDivision(),
        };
    }

    private static async Task<IResult> GetAvailableClubsAsync(
        Guid countryId,
        GetAvailableClubs query,
        CancellationToken cancellationToken)
    {
        var result = await query.ExecuteAsync(countryId, cancellationToken);

        return result.Outcome switch
        {
            AvailableClubsOutcome.Found => Results.Ok(result.Clubs),
            AvailableClubsOutcome.CountryNotFound => CountryNotFound(),
            _ => NoActiveDivision(),
        };
    }

    private static async Task<IResult> CreateManagerProfileAsync(
        HttpContext httpContext,
        CreateManagerProfileRequest request,
        IValidator<CreateManagerProfileRequest> validator,
        CreateManagerProfile useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var invalid = await RequestValidation.ValidateAsync(validator, request, cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var result = await useCase.ExecuteAsync(userId, request, cancellationToken);

        return result.Outcome switch
        {
            // 200, not 201, when the profile already existed: nothing was created, and a client that
            // retried a lost response should not be told it just made a second profile.
            CreateManagerProfileOutcome.Created => Results.Created(
                $"{Endpoints.ModuleEndpointGroups.VersionPrefix}/club-tenure",
                result.Profile),

            CreateManagerProfileOutcome.ProfileExists => Results.Ok(result.Profile),

            _ => ProblemResults.Forbidden("This account cannot create a manager profile."),
        };
    }

    private static async Task<IResult> ClaimClubAsync(
        HttpContext httpContext,
        ClaimClubRequest request,
        IValidator<ClaimClubRequest> validator,
        ClaimClub useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var idempotencyKey = httpContext.Request.Headers[ApiHeaders.IdempotencyKey].ToString();

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > IdempotencyKeyMaxLength)
        {
            return ProblemResults.Code(
                StatusCodes.Status400BadRequest,
                WorldErrorCodes.IdempotencyKeyRequired,
                "An idempotency key is required.",
                $"Send a stable Idempotency-Key of at most {IdempotencyKeyMaxLength} characters. Reuse it "
                + "when retrying so the claim cannot become two tenures.");
        }

        var invalid = await RequestValidation.ValidateAsync(validator, request, cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var result = await useCase.ExecuteAsync(userId, request, idempotencyKey, cancellationToken);

        return result.Outcome switch
        {
            ClaimClubOutcome.Claimed => Results.Created(
                $"{Endpoints.ModuleEndpointGroups.VersionPrefix}/clubs/{request.ClubId}/dashboard",
                result.Dashboard),

            ClaimClubOutcome.ClubNotFound => ProblemResults.Code(
                StatusCodes.Status404NotFound,
                WorldErrorCodes.ClubNotFound,
                "No such club.",
                "That club does not exist in this world."),

            ClaimClubOutcome.ClubNotClaimable => ProblemResults.Code(
                StatusCodes.Status409Conflict,
                WorldErrorCodes.ClubNotClaimable,
                "That club cannot be taken over.",
                "New managers may only take over an AI-controlled club in the country's lowest active tier."),

            ClaimClubOutcome.ClubAlreadyClaimed => ProblemResults.Code(
                StatusCodes.Status409Conflict,
                WorldErrorCodes.ClubAlreadyClaimed,
                "That club has a manager.",
                "Another manager took this club over. Choose another club in the same division."),

            ClaimClubOutcome.ManagerHasActiveClub => ProblemResults.Code(
                StatusCodes.Status409Conflict,
                WorldErrorCodes.ManagerHasActiveClub,
                "You already manage a club.",
                "Resign from your current club before taking over another."),

            ClaimClubOutcome.ManagerProfileRequired => ProblemResults.Code(
                StatusCodes.Status409Conflict,
                WorldErrorCodes.ManagerProfileRequired,
                "No manager profile.",
                "Create your manager profile before claiming a club."),

            ClaimClubOutcome.ManagerInCooldown => ProblemResults.Code(
                StatusCodes.Status409Conflict,
                WorldErrorCodes.ManagerInCooldown,
                "You are still on cooldown.",
                "A resignation is followed by a waiting period before you can take over another club.",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["cooldownUntil"] = result.CooldownUntil,
                }),

            ClaimClubOutcome.CapacityProvisioning => ProblemResults.Code(
                StatusCodes.Status409Conflict,
                WorldErrorCodes.CapacityProvisioning,
                "This country is full.",
                "Every club in the country's lowest tier has a manager. A new tier is being generated; "
                + "poll the capacity endpoint, or choose another country.",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["provisioning"] = result.Provisioning,
                }),

            ClaimClubOutcome.IdempotencyKeyReused => ProblemResults.Code(
                StatusCodes.Status409Conflict,
                WorldErrorCodes.IdempotencyKeyReused,
                "That idempotency key was already used.",
                "Send a new Idempotency-Key for a different claim, or repeat the original request exactly."),

            ClaimClubOutcome.WorldNotSeeded => ProblemResults.Code(
                StatusCodes.Status404NotFound,
                WorldErrorCodes.WorldNotSeeded,
                "No world yet.",
                "The world has not been created. Run the world seeder before onboarding."),

            ClaimClubOutcome.WorldNotAcceptingClaims => ProblemResults.Code(
                StatusCodes.Status409Conflict,
                WorldErrorCodes.WorldNotAcceptingClaims,
                "Onboarding is closed.",
                "The world is frozen and is not accepting new managers right now."),

            _ => ProblemResults.Forbidden("This account cannot claim a club."),
        };
    }

    private static async Task<IResult> ResignClubAsync(
        HttpContext httpContext,
        ResignClub useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await useCase.ExecuteAsync(userId, cancellationToken);

        return result.Outcome switch
        {
            ResignClubOutcome.Resigned => Results.Ok(result.State),

            ResignClubOutcome.NoActiveTenure => ProblemResults.Code(
                StatusCodes.Status409Conflict,
                WorldErrorCodes.NoActiveTenure,
                "You do not manage a club.",
                "There is nothing to resign from."),

            ResignClubOutcome.ManagerProfileRequired => ProblemResults.Code(
                StatusCodes.Status409Conflict,
                WorldErrorCodes.ManagerProfileRequired,
                "No manager profile.",
                "Create your manager profile before managing a club."),

            ResignClubOutcome.WorldNotSeeded => ProblemResults.Code(
                StatusCodes.Status404NotFound,
                WorldErrorCodes.WorldNotSeeded,
                "No world yet.",
                "The world has not been created."),

            _ => ProblemResults.Forbidden("This account cannot resign a club."),
        };
    }

    private static async Task<IResult> GetTenureAsync(
        HttpContext httpContext,
        GetOnboardingState query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        return Results.Ok(await query.ExecuteAsync(userId, cancellationToken));
    }

    private static async Task<IResult> GetClubDashboardAsync(
        Guid clubId,
        GetClubDashboard query,
        CancellationToken cancellationToken)
    {
        var result = await query.ExecuteAsync(clubId, cancellationToken);

        return result.Outcome switch
        {
            ClubDashboardOutcome.Found => Results.Ok(result.Dashboard),

            ClubDashboardOutcome.WorldNotSeeded => ProblemResults.Code(
                StatusCodes.Status404NotFound,
                WorldErrorCodes.WorldNotSeeded,
                "No world yet.",
                "The world has not been created."),

            _ => ProblemResults.Code(
                StatusCodes.Status404NotFound,
                WorldErrorCodes.ClubNotFound,
                "No such club.",
                "That club does not exist in this world."),
        };
    }

    private static IResult CountryNotFound() => ProblemResults.Code(
        StatusCodes.Status404NotFound,
        WorldErrorCodes.CountryNotFound,
        "No such country.",
        "That country does not exist in this world.");

    private static IResult NoActiveDivision() => ProblemResults.Code(
        StatusCodes.Status409Conflict,
        WorldErrorCodes.ClubNotClaimable,
        "No division is open.",
        "This country has no active division, so it cannot accept managers yet.");

    private static bool TryGetUserId(HttpContext httpContext, out Guid userId) =>
        Guid.TryParse(httpContext.User.FindFirst(AuthClaimNames.Subject)?.Value, out userId);
}
