using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Squad;
using TouchlineManager.Domain.Market;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Market;

/// <summary>
/// Lists an eligible player for sale with a minimum fee and the buyer's precomputed terms (`TRF-1`, `TRF-2`,
/// `TRF-14`, `CON-5`).
/// </summary>
/// <remarks>
/// <para>
/// The seller names a player and a minimum fee; the server derives everything else. The buyer's wage and
/// contract length are priced from the same quote a renewal is (`CON-3`), so the terms a bidder will sign are
/// displayed before they bid (`CON-5`), and the resolution only has to revalidate, not reprice.
/// </para>
/// <para>
/// The listing is refused when the sale would take the seller below the minimum squad or its two goalkeepers
/// (`SQ-2`), because a manager cannot sell a squad into illegality. The resolution revalidates the same rule
/// (`TRF-9`) in case a bid or an injury changes the picture while the auction runs.
/// </para>
/// </remarks>
public sealed class CreateListing
{
    private readonly ResolveOwnedClub _access;
    private readonly IRosterQueries _roster;
    private readonly IListingRepository _listings;
    private readonly IMarketQueries _queries;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;
    private readonly IRequestContext _requestContext;
    private readonly ISecureTokenService _secureTokens;

    /// <summary>Initializes the use case.</summary>
    public CreateListing(
        ResolveOwnedClub access,
        IRosterQueries roster,
        IListingRepository listings,
        IMarketQueries queries,
        IUnitOfWork unitOfWork,
        IAuditWriter audit,
        IClock clock,
        IRequestContext requestContext,
        ISecureTokenService secureTokens)
    {
        _access = access;
        _roster = roster;
        _listings = listings;
        _queries = queries;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _clock = clock;
        _requestContext = requestContext;
        _secureTokens = secureTokens;
    }

    /// <summary>Lists a player, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="playerId">The player to sell.</param>
    /// <param name="minimumFeeMinor">The minimum fee, in minor units.</param>
    /// <param name="seasons">The buyer contract length, 1–3 game seasons.</param>
    /// <param name="idempotencyKey">The request's idempotency key, which a retry is matched against (`INT-2`).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ListingResult> ExecuteAsync(
        Guid userId,
        Guid playerId,
        long minimumFeeMinor,
        int seasons,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new ListingResult(access.Outcome.FromAccess(), null);
        }

        // A retried create returns the listing it already opened rather than opening a second (T-4).
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var replayed = await _listings.FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);

            if (replayed is not null)
            {
                if (replayed.PlayerId != playerId
                    || replayed.MinimumFeeMinor != minimumFeeMinor
                    || replayed.GeneratedContractSeasons != seasons)
                {
                    return new ListingResult(MarketOutcome.IdempotencyKeyReused, null);
                }

                return new ListingResult(
                    MarketOutcome.Found,
                    await _queries.GetListingAsync(replayed.Id, access.ClubId, cancellationToken));
            }
        }

        var eligibility = await _roster.GetListingEligibilityAsync(playerId, cancellationToken);

        if (eligibility is null)
        {
            return new ListingResult(MarketOutcome.NotFound, null);
        }

        if (eligibility.ClubId != access.ClubId || !eligibility.HasActiveRegistration)
        {
            return new ListingResult(MarketOutcome.NotEligible, null);
        }

        if (eligibility.IsListed)
        {
            return new ListingResult(MarketOutcome.AlreadyListed, null);
        }

        // The sale must leave the seller legal: at least the minimum squad, and still two goalkeepers (SQ-2).
        var remainingSquad = eligibility.RegisteredCount - 1;
        var remainingGoalkeepers = eligibility.GoalkeeperCount
            - (eligibility.PrimaryPosition == PlayerPosition.Goalkeeper ? 1 : 0);

        if (!SquadLegality.MeetsMinimum(remainingSquad)
            || remainingGoalkeepers < WorldRuleSet.MinimumGoalkeepers)
        {
            return new ListingResult(MarketOutcome.NotEligible, null);
        }

        var remainingSeasons = Math.Max(
            0,
            eligibility.ContractEndSeasonNumber - eligibility.CurrentSeasonNumber);

        var terms = ContractRenewalQuote.Calculate(
            new ContractRenewalInput(
                eligibility.Ability,
                eligibility.Potential,
                eligibility.Age,
                eligibility.Appearances,
                eligibility.MoraleBp,
                eligibility.TierNumber,
                remainingSeasons),
            seasons);

        var now = _clock.UtcNow;
        var listing = TransferListing.Open(
            Guid.CreateVersion7(),
            playerId,
            access.ClubId,
            minimumFeeMinor,
            terms.WeeklyWageMinor,
            terms.Seasons,
            now,
            AuctionWindows.EndsAtFor(now),
            idempotencyKey,
            now);

        _listings.Add(listing);

        _audit.Record(new AuditEntry(
            MarketAuditActions.ListingOpened,
            AuditActorTypes.User,
            userId,
            AuditTargetTypes.TransferListing,
            listing.Id,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ListingResult(
            MarketOutcome.Found,
            await _queries.GetListingAsync(listing.Id, access.ClubId, cancellationToken));
    }
}
