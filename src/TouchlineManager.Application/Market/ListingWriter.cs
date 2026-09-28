using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Domain.Market;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Market;

/// <summary>What a listing write did.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="ListingId">The listing that now exists, when the write succeeded.</param>
/// <param name="Created">Whether this call opened the listing, as opposed to replaying an existing one (`T-4`).</param>
public sealed record ListingWriteResult(MarketOutcome Outcome, Guid ListingId, bool Created);

/// <summary>The listing core shared by a manager's command and the AI's evaluation (`TRF-1`, `INS-12`).</summary>
public interface IListingWriter
{
    /// <summary>Lists a player for a club, or refuses.</summary>
    /// <param name="clubId">The selling club.</param>
    /// <param name="actor">Who is acting.</param>
    /// <param name="playerId">The player to sell.</param>
    /// <param name="minimumFeeMinor">The asking price, in minor units.</param>
    /// <param name="seasons">The buyer contract length, 1–3 game seasons.</param>
    /// <param name="idempotencyKey">The request's idempotency key, which a retry is matched against (`T-4`).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ListingWriteResult> ListAsync(
        Guid clubId,
        MarketActor actor,
        Guid playerId,
        long minimumFeeMinor,
        int seasons,
        string? idempotencyKey,
        CancellationToken cancellationToken);
}

/// <summary>
/// Opens a transfer listing for an eligible player (`TRF-1`, `TRF-2`, `TRF-14`, `CON-5`).
/// </summary>
/// <remarks>
/// <para>
/// The listing core, shared by a manager's <see cref="CreateListing"/> command and the AI's market
/// evaluation (`INS-12`). It stages the listing and its audit row and never saves, so the caller commits it
/// beside whatever else the operation changes.
/// </para>
/// <para>
/// The player's buyer terms are priced from the same quote a renewal is (`CON-3`), so the wage and contract
/// length a bidder will sign are fixed when the listing opens and cannot drift while the auction runs
/// (`CON-5`). The sale is refused when it would leave the seller below the minimum squad or its two
/// goalkeepers (`SQ-2`).
/// </para>
/// </remarks>
public sealed class ListingWriter : IListingWriter
{
    private readonly IRosterQueries _roster;
    private readonly IListingRepository _listings;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;

    /// <summary>Initializes the writer.</summary>
    public ListingWriter(
        IRosterQueries roster,
        IListingRepository listings,
        IAuditWriter audit,
        IClock clock)
    {
        _roster = roster;
        _listings = listings;
        _audit = audit;
        _clock = clock;
    }

    /// <summary>Lists a player for a club, or refuses.</summary>
    /// <param name="clubId">The selling club.</param>
    /// <param name="actor">Who is acting.</param>
    /// <param name="playerId">The player to sell.</param>
    /// <param name="minimumFeeMinor">The asking price, in minor units.</param>
    /// <param name="seasons">The buyer contract length, 1–3 game seasons.</param>
    /// <param name="idempotencyKey">The request's idempotency key, which a retry is matched against (`T-4`).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ListingWriteResult> ListAsync(
        Guid clubId,
        MarketActor actor,
        Guid playerId,
        long minimumFeeMinor,
        int seasons,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var replayed = await _listings.FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);

            if (replayed is not null)
            {
                var identical = replayed.PlayerId == playerId
                    && replayed.SellerClubId == clubId
                    && replayed.MinimumFeeMinor == minimumFeeMinor
                    && replayed.GeneratedContractSeasons == seasons;

                return identical
                    ? new ListingWriteResult(MarketOutcome.Found, replayed.Id, Created: false)
                    : new ListingWriteResult(MarketOutcome.IdempotencyKeyReused, Guid.Empty, Created: false);
            }
        }

        var eligibility = await _roster.GetListingEligibilityAsync(playerId, cancellationToken);

        if (eligibility is null)
        {
            return new ListingWriteResult(MarketOutcome.NotFound, Guid.Empty, Created: false);
        }

        if (eligibility.ClubId != clubId || !eligibility.HasActiveRegistration)
        {
            return new ListingWriteResult(MarketOutcome.NotEligible, Guid.Empty, Created: false);
        }

        if (eligibility.IsListed)
        {
            return new ListingWriteResult(MarketOutcome.AlreadyListed, Guid.Empty, Created: false);
        }

        // The sale must leave the seller legal: at least the minimum squad, and still two goalkeepers (SQ-2).
        var remainingSquad = eligibility.RegisteredCount - 1;
        var remainingGoalkeepers = eligibility.GoalkeeperCount
            - (eligibility.PrimaryPosition == PlayerPosition.Goalkeeper ? 1 : 0);

        if (!SquadLegality.MeetsMinimum(remainingSquad)
            || remainingGoalkeepers < WorldRuleSet.MinimumGoalkeepers)
        {
            return new ListingWriteResult(MarketOutcome.NotEligible, Guid.Empty, Created: false);
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
            clubId,
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
            actor.ActorType,
            actor.UserId,
            AuditTargetTypes.TransferListing,
            listing.Id,
            actor.CorrelationId,
            actor.IpHash,
            Reason: null));

        return new ListingWriteResult(MarketOutcome.Found, listing.Id, Created: true);
    }
}
