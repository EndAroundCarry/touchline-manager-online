using System.Globalization;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Domain.Market;

namespace TouchlineManager.Application.Market;

/// <summary>What one AI market evaluation did.</summary>
/// <param name="Clubs">How many AI-controlled clubs were considered.</param>
/// <param name="Listed">How many surplus players the AI listed.</param>
/// <param name="Bids">How many bids the AI placed.</param>
/// <param name="Skipped">How many decisions a writer refused. Expected to be zero.</param>
public sealed record EvaluateAiMarketResult(int Clubs, int Listed, int Bids, int Skipped);

/// <summary>
/// Runs the AI transfer market: every club no human holds lists its surplus players and bids within its
/// needs, valuation, and budget (`TRF-12`, master plan §7.2).
/// </summary>
/// <remarks>
/// <para>
/// The decisions are made by the pure <see cref="AiMarketPolicy"/> and carried out through the same
/// <see cref="ListingWriter"/> and <see cref="BidWriter"/> a manager's command uses, so an AI listing faces
/// the identical eligibility, squad-legality, and affordability rules a human's does — `INS-12`'s "the AI
/// receives no bypass" is enforced by the code path rather than by intention.
/// </para>
/// <para>
/// One job is one pass: it lists a player only when the club is not already listing them, and bids only on
/// listings that existed when the pass began and that the club does not already lead, so an evaluation
/// cannot chase its own decisions. The whole pass commits in one transaction, and the deterministic daily
/// idempotency keys make a retried pass a replay rather than a second listing or bid.
/// </para>
/// </remarks>
public sealed class EvaluateAiMarket
{
    private readonly IAiMarketRepository _repository;
    private readonly IListingWriter _listingWriter;
    private readonly IBidWriter _bidWriter;
    private readonly IAiMarketDecisionRepository _decisions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IRequestContext _requestContext;

    /// <summary>Initializes the use case.</summary>
    public EvaluateAiMarket(
        IAiMarketRepository repository,
        IListingWriter listingWriter,
        IBidWriter bidWriter,
        IAiMarketDecisionRepository decisions,
        IUnitOfWork unitOfWork,
        IClock clock,
        IRequestContext requestContext)
    {
        _repository = repository;
        _listingWriter = listingWriter;
        _bidWriter = bidWriter;
        _decisions = decisions;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _requestContext = requestContext;
    }

    /// <summary>Evaluates every AI-controlled club against the open market.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<EvaluateAiMarketResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var clubs = await _repository.LoadAiClubsAsync(cancellationToken);

        var now = _clock.UtcNow;
        var day = DateOnly.FromDateTime(now.UtcDateTime);

        // The market the pass bids on is everything open before the day began, so what it lists today cannot
        // become something it bids on today: a pass is a function of the day, not of the instant it runs, and
        // a retry bids on the same listings and nothing else.
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var listings = await _repository.LoadOpenListingsAsync(dayStart, cancellationToken);

        var actor = MarketActor.Service(_requestContext.CorrelationId);

        var playerByListing = listings.ToDictionary(listing => listing.ListingId, listing => listing.PlayerId);
        var policyListings = listings.Select(ToPolicyListing).ToList();

        var listed = 0;
        var bids = 0;
        var skipped = 0;

        // One pass is one transaction. The bids it places take a listing-scoped lock that must live until the
        // pass commits (ADR-0026), and the whole pass already committed as one unit of work.
        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);

        // Club identity order makes the pass reproducible when two clubs decide in one evaluation.
        foreach (var club in clubs.OrderBy(club => club.ClubId))
        {
            var plan = AiMarketPolicy.Decide(ToPolicyClub(club), policyListings);

            foreach (var decision in plan.Listings)
            {
                var write = await _listingWriter.ListAsync(
                    club.ClubId,
                    actor,
                    decision.PlayerId,
                    decision.MinimumFeeMinor,
                    decision.Seasons,
                    ListingKey(club.ClubId, decision.PlayerId, day),
                    cancellationToken);

                if (write.Outcome != MarketOutcome.Found)
                {
                    skipped++;

                    continue;
                }

                if (write.Created)
                {
                    _decisions.Add(AiMarketDecision.Record(
                        Guid.CreateVersion7(),
                        club.ClubId,
                        now,
                        AiMarketAction.Listed,
                        decision.PlayerId,
                        write.ListingId,
                        bidId: null,
                        plan.InputsHash,
                        AiMarketPolicyVersions.Version,
                        now));

                    listed++;
                }
            }

            foreach (var decision in plan.Bids)
            {
                var write = await _bidWriter.BidAsync(
                    club.ClubId,
                    actor,
                    decision.ListingId,
                    decision.AmountMinor,
                    BidKey(club.ClubId, decision.ListingId, day),
                    cancellationToken);

                if (write.Outcome != MarketOutcome.Found)
                {
                    skipped++;

                    continue;
                }

                if (write.Created)
                {
                    _decisions.Add(AiMarketDecision.Record(
                        Guid.CreateVersion7(),
                        club.ClubId,
                        now,
                        AiMarketAction.Bid,
                        playerByListing[decision.ListingId],
                        listingId: null,
                        write.BidId,
                        plan.InputsHash,
                        AiMarketPolicyVersions.Version,
                        now));

                    bids++;
                }
            }
        }

        if (listed > 0 || bids > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new EvaluateAiMarketResult(clubs.Count, listed, bids, skipped);
    }

    private static AiMarketClub ToPolicyClub(AiMarketClubRow row) => new(
        row.ClubId,
        row.Tier,
        row.SpendableMinor,
        [.. row.Players.Select(player => new AiMarketPlayer(
            player.PlayerId,
            player.Family,
            player.Ability,
            player.Potential,
            player.Age,
            player.SquadStatus,
            player.ContractEndSeasonNumber,
            row.CurrentSeasonNumber,
            player.IsListed))]);

    private static AiMarketListing ToPolicyListing(AiMarketListingRow row) => new(
        row.ListingId,
        row.SellerClubId,
        row.LeadingClubId,
        row.PlayerId,
        row.Family,
        row.Ability,
        row.Potential,
        row.Age,
        row.MinimumFeeMinor,
        row.LeadingAmountMinor);

    /// <summary>The deterministic daily key of a listing decision, which a retried pass replays (`T-4`).</summary>
    private static string ListingKey(Guid clubId, Guid playerId, DateOnly day) =>
        string.Create(CultureInfo.InvariantCulture, $"ai-l:{clubId:N}:{playerId:N}:{day:yyyyMMdd}");

    /// <summary>The deterministic daily key of a bid decision, which a retried pass replays (`T-4`).</summary>
    private static string BidKey(Guid clubId, Guid listingId, DateOnly day) =>
        string.Create(CultureInfo.InvariantCulture, $"ai-b:{clubId:N}:{listingId:N}:{day:yyyyMMdd}");
}
