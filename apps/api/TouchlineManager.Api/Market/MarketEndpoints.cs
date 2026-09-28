using FluentValidation;
using TouchlineManager.Api.Auth;
using TouchlineManager.Api.Http;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Application.Market;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Http;
using TouchlineManager.Contracts.Market;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.Market;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Api.Market;

/// <summary>
/// The market module's surface: scouting, shortlists, listings, and bids (master plan §10.6).
/// </summary>
/// <remarks>
/// <para>
/// Like the squad and finance endpoints these sit at the version root rather than under
/// <c>/api/v1/market</c>, because §10.6 addresses them as <c>/scouting/players</c>, <c>/shortlist</c>, and
/// <c>/transfers/…</c>. Which club the caller may act on comes from their tenure, so nothing here trusts a
/// club identity from the request.
/// </para>
/// <para>
/// A listing, a cancellation, and a bid are commands: they require a verified manager and an idempotency key,
/// so a retried request is a replay rather than a second listing or a double charge (`INT-2`, `T-4`).
/// </para>
/// </remarks>
internal static class MarketEndpoints
{
    /// <summary>Matches the largest idempotency key a stored listing or bid can hold (`T-4`).</summary>
    private const int IdempotencyKeyMaxLength = 80;

    /// <summary>Maps the market routes.</summary>
    public static IEndpointRouteBuilder MapMarketEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup(Endpoints.ModuleEndpointGroups.VersionPrefix)
            .WithTags("market")
            .RequireAuthorization();

        group.MapGet("/scouting/players", SearchPlayersAsync)
            .WithName("SearchPlayers")
            .WithSummary("Searches the global player database by name, position, age, and ability.")
            .Produces<PlayerSearchResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/shortlist", ListShortlistAsync)
            .WithName("ListShortlist")
            .WithSummary("Reads the manager's private shortlist.")
            .Produces<ShortlistResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/shortlist/{playerId:guid}", AddShortlistAsync)
            .WithName("AddShortlistEntry")
            .RequireAuthorization(AuthorizationPolicies.VerifiedManager)
            .WithSummary("Adds a player to the manager's shortlist, or updates the note kept.")
            .Produces<ShortlistResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/shortlist/{playerId:guid}", RemoveShortlistAsync)
            .WithName("RemoveShortlistEntry")
            .RequireAuthorization(AuthorizationPolicies.VerifiedManager)
            .WithSummary("Removes a player from the manager's shortlist.")
            .Produces<ShortlistResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/transfers/listings", ListListingsAsync)
            .WithName("ListTransferListings")
            .WithSummary("Browses transfer listings.")
            .Produces<TransferListingsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/transfers/listings", CreateListingAsync)
            .WithName("CreateTransferListing")
            .RequireAuthorization(AuthorizationPolicies.VerifiedManager)
            .WithSummary("Lists an eligible player for sale.")
            .Produces<TransferListingResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/transfers/listings/{listingId:guid}", GetListingAsync)
            .WithName("GetTransferListing")
            .WithSummary("Reads one transfer listing.")
            .Produces<TransferListingResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/transfers/listings/{listingId:guid}", CancelListingAsync)
            .WithName("CancelTransferListing")
            .RequireAuthorization(AuthorizationPolicies.VerifiedManager)
            .WithSummary("Withdraws a listing, releasing its reservations.")
            .Produces<TransferListingResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/transfers/listings/{listingId:guid}/bids", PlaceBidAsync)
            .WithName("PlaceTransferBid")
            .RequireAuthorization(AuthorizationPolicies.VerifiedManager)
            .WithSummary("Places or raises a bid, reserving its funds.")
            .Produces<TransferListingResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/transfers/mine", GetMyActivityAsync)
            .WithName("GetMyMarketActivity")
            .WithSummary("Reads the club's own listings and bids.")
            .Produces<MyMarketActivityResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/transfers/history", GetHistoryAsync)
            .WithName("GetTransferHistory")
            .WithSummary("Reads the public history of completed transfers.")
            .Produces<TransferHistoryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static async Task<IResult> SearchPlayersAsync(
        HttpContext httpContext,
        SearchPlayers useCase,
        IClock clock,
        CancellationToken cancellationToken,
        string? name = null,
        PositionFamily? family = null,
        int? ageMin = null,
        int? ageMax = null,
        int? abilityMin = null,
        Guid? clubId = null,
        Guid? divisionId = null,
        PlayerSort sort = PlayerSort.Name,
        string? cursor = null,
        int? pageSize = null)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var filter = new PlayerSearchFilter(
            name,
            family,
            ageMin,
            ageMax,
            abilityMin,
            clubId,
            divisionId,
            sort,
            Cursor: null,
            pageSize ?? SearchPlayers.DefaultPageSize);

        var result = await useCase.ExecuteAsync(userId, filter, cursor, cancellationToken);

        return result.Outcome == MarketOutcome.Found
            ? Results.Ok(result.Page!.ToResponse(clock.UtcNow))
            : Refusal(result.Outcome);
    }

    private static async Task<IResult> ListShortlistAsync(
        HttpContext httpContext,
        Shortlists useCase,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await useCase.ListAsync(userId, cancellationToken);

        return result.Outcome == MarketOutcome.Found
            ? Results.Ok(result.Shortlist!.ToResponse(clock.UtcNow))
            : Refusal(result.Outcome);
    }

    private static async Task<IResult> AddShortlistAsync(
        HttpContext httpContext,
        Guid playerId,
        ShortlistRequest request,
        Shortlists useCase,
        IValidator<ShortlistRequest> validator,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        if (await RequestValidation.ValidateAsync(validator, request, cancellationToken) is { } problem)
        {
            return problem;
        }

        var result = await useCase.AddAsync(userId, playerId, request.Notes, cancellationToken);

        return result.Outcome == MarketOutcome.Found
            ? Results.Ok(result.Shortlist!.ToResponse(clock.UtcNow))
            : Refusal(result.Outcome);
    }

    private static async Task<IResult> RemoveShortlistAsync(
        HttpContext httpContext,
        Guid playerId,
        Shortlists useCase,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await useCase.RemoveAsync(userId, playerId, cancellationToken);

        return result.Outcome == MarketOutcome.Found
            ? Results.Ok(result.Shortlist!.ToResponse(clock.UtcNow))
            : Refusal(result.Outcome);
    }

    private static async Task<IResult> ListListingsAsync(
        HttpContext httpContext,
        ListListings useCase,
        IClock clock,
        CancellationToken cancellationToken,
        string? status = null,
        Guid? sellerClubId = null,
        PositionFamily? family = null,
        string? cursor = null,
        int? pageSize = null)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        if (!TryParseStatus(status, out var listingStatus))
        {
            return ProblemResults.Code(
                StatusCodes.Status400BadRequest,
                MarketErrorCodes.InvalidCursor,
                "Unknown status.",
                "The listing status is not one this build knows.");
        }

        var filter = new ListingBrowseFilter(
            listingStatus,
            sellerClubId,
            family,
            CursorListingId: null,
            pageSize ?? ListListings.DefaultPageSize);

        var result = await useCase.ExecuteAsync(userId, filter, cursor, cancellationToken);

        return result.Outcome == MarketOutcome.Found
            ? Results.Ok(result.Page!.ToResponse(DateTimeOffset.UtcNow))
            : Refusal(result.Outcome);
    }

    private static async Task<IResult> CreateListingAsync(
        HttpContext httpContext,
        CreateListingRequest request,
        CreateListing useCase,
        IValidator<CreateListingRequest> validator,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        if (!TryGetIdempotencyKey(httpContext, out var idempotencyKey))
        {
            return IdempotencyKeyRequired();
        }

        if (await RequestValidation.ValidateAsync(validator, request, cancellationToken) is { } problem)
        {
            return problem;
        }

        var result = await useCase.ExecuteAsync(
            userId,
            request.PlayerId,
            request.MinimumFeeMinor,
            request.Seasons,
            idempotencyKey,
            cancellationToken);

        return result.Outcome == MarketOutcome.Found
            ? Results.Created(
                $"/api/v1/transfers/listings/{result.Listing!.ListingId}",
                result.Listing.ToResponse())
            : Refusal(result.Outcome);
    }

    private static async Task<IResult> GetListingAsync(
        HttpContext httpContext,
        Guid listingId,
        ListListings useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await useCase.GetListingAsync(userId, listingId, cancellationToken);

        return result.Outcome == MarketOutcome.Found
            ? Results.Ok(result.Listing!.ToResponse())
            : Refusal(result.Outcome);
    }

    private static async Task<IResult> CancelListingAsync(
        HttpContext httpContext,
        Guid listingId,
        CancelListing useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        if (!TryGetIdempotencyKey(httpContext, out _))
        {
            return IdempotencyKeyRequired();
        }

        var result = await useCase.ExecuteAsync(userId, listingId, cancellationToken);

        return result.Outcome == MarketOutcome.Found
            ? Results.Ok(result.Listing!.ToResponse())
            : Refusal(result.Outcome);
    }

    private static async Task<IResult> PlaceBidAsync(
        HttpContext httpContext,
        Guid listingId,
        PlaceBidRequest request,
        PlaceBid useCase,
        IValidator<PlaceBidRequest> validator,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        if (!TryGetIdempotencyKey(httpContext, out var idempotencyKey))
        {
            return IdempotencyKeyRequired();
        }

        if (await RequestValidation.ValidateAsync(validator, request, cancellationToken) is { } problem)
        {
            return problem;
        }

        var result = await useCase.ExecuteAsync(
            userId,
            listingId,
            request.AmountMinor,
            idempotencyKey,
            cancellationToken);

        return result.Outcome == MarketOutcome.Found
            ? Results.Ok(result.Listing!.ToResponse())
            : Refusal(result.Outcome);
    }

    private static async Task<IResult> GetMyActivityAsync(
        HttpContext httpContext,
        ListListings useCase,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await useCase.GetActivityAsync(userId, cancellationToken);

        return result.Outcome == MarketOutcome.Found
            ? Results.Ok(result.Activity!.ToResponse(clock.UtcNow))
            : Refusal(result.Outcome);
    }

    private static async Task<IResult> GetHistoryAsync(
        HttpContext httpContext,
        ListListings useCase,
        IClock clock,
        CancellationToken cancellationToken,
        string? cursor = null)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await useCase.GetHistoryAsync(userId, cursor, cancellationToken);

        return result.Outcome == MarketOutcome.Found
            ? Results.Ok(result.Page!.ToResponse(clock.UtcNow))
            : Refusal(result.Outcome);
    }

    /// <summary>Turns a market refusal into a status and a stable code.</summary>
    internal static IResult Refusal(MarketOutcome outcome) => outcome switch
    {
        MarketOutcome.NoClub => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            SquadErrorCodes.NoClub,
            "No club.",
            "You do not manage a club yet."),

        MarketOutcome.ClubNotManaged => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            SquadErrorCodes.ClubNotManaged,
            "Not your club.",
            "You do not manage the club that owns this resource."),

        MarketOutcome.NoManagerProfile => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            WorldErrorCodes.ManagerProfileRequired,
            "No manager profile.",
            "Create your manager profile before using the market."),

        MarketOutcome.WorldNotSeeded => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            WorldErrorCodes.WorldNotSeeded,
            "No world yet.",
            "The world has not been created."),

        MarketOutcome.InvalidCursor => ProblemResults.Code(
            StatusCodes.Status400BadRequest,
            MarketErrorCodes.InvalidCursor,
            "Invalid cursor.",
            "The cursor could not be read."),

        MarketOutcome.NotFound => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            MarketErrorCodes.ListingNotFound,
            "Not found.",
            "That listing, player, or entry does not exist."),

        MarketOutcome.NotEligible => ProblemResults.Code(
            StatusCodes.Status409Conflict,
            MarketErrorCodes.ListingNotEligible,
            "Not eligible to be listed.",
            "The player is not yours to sell, or the sale would leave your squad below the minimum (SQ-2)."),

        MarketOutcome.AlreadyListed => ProblemResults.Code(
            StatusCodes.Status409Conflict,
            MarketErrorCodes.PlayerAlreadyListed,
            "Already listed.",
            "That player already has an open listing (TRF-14)."),

        MarketOutcome.NotOpen => ProblemResults.Code(
            StatusCodes.Status409Conflict,
            MarketErrorCodes.ListingNotOpen,
            "Listing is closed.",
            "That listing is no longer open, so it accepts no bids and cannot be cancelled."),

        MarketOutcome.CannotBidOnOwnPlayer => ProblemResults.Code(
            StatusCodes.Status400BadRequest,
            MarketErrorCodes.CannotBidOnOwnPlayer,
            "You cannot bid on your own player.",
            "A club cannot bid on a listing it owns (TRF-1)."),

        MarketOutcome.BidTooLow => ProblemResults.Code(
            StatusCodes.Status400BadRequest,
            MarketErrorCodes.BidTooLow,
            "Bid too low.",
            "The bid does not clear the minimum fee or the minimum raise (TRF-5)."),

        MarketOutcome.InsufficientFunds => ProblemResults.Code(
            StatusCodes.Status400BadRequest,
            MarketErrorCodes.InsufficientFunds,
            "Insufficient funds.",
            "The club does not hold that much cash after its existing reservations (FIN-10)."),

        MarketOutcome.IdempotencyKeyReused => ProblemResults.Code(
            StatusCodes.Status409Conflict,
            MarketErrorCodes.IdempotencyKeyReused,
            "Idempotency key reused.",
            "That idempotency key was used for a different request (T-4)."),

        _ => ProblemResults.Code(
            StatusCodes.Status409Conflict,
            ApiErrorCodes.Forbidden,
            "Not permitted.",
            "The request could not be completed in the current state."),
    };

    private static IResult IdempotencyKeyRequired() => ProblemResults.Code(
        StatusCodes.Status400BadRequest,
        MarketErrorCodes.IdempotencyKeyRequired,
        "An idempotency key is required.",
        "Send a unique Idempotency-Key header so a retried command is not applied twice (INT-2).");

    private static bool TryGetIdempotencyKey(HttpContext httpContext, out string idempotencyKey)
    {
        idempotencyKey = httpContext.Request.Headers[Contracts.Http.ApiHeaders.IdempotencyKey].ToString();

        return !string.IsNullOrWhiteSpace(idempotencyKey) && idempotencyKey.Length <= IdempotencyKeyMaxLength;
    }

    private static bool TryParseStatus(string? status, out ListingStatus? listingStatus)
    {
        listingStatus = null;

        if (string.IsNullOrWhiteSpace(status))
        {
            return true;
        }

        if (!ListingStatuses.TryFromCode(status, out var parsed))
        {
            return false;
        }

        listingStatus = parsed;

        return true;
    }

    private static bool TryGetUserId(HttpContext httpContext, out Guid userId) =>
        Guid.TryParse(httpContext.User.FindFirst(AuthClaimNames.Subject)?.Value, out userId);
}
