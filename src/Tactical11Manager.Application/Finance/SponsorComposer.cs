using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Contracts.Finance;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Application.Finance;

/// <summary>
/// Builds the sponsor page of a club: its three offers with what each objective would pay, the deal it signed, and the deadline (`INT-1`).
/// </summary>
/// <remarks>
/// Every figure is the server's: the bonus next to an objective is worked out from the fans and the standing the offer was made from, so a
/// client prints it and never computes it.
/// </remarks>
public sealed class SponsorComposer
{
    private readonly ISponsorQueries _queries;
    private readonly ISponsorRepository _sponsors;
    private readonly SponsorSigning _signing;
    private readonly IClock _clock;

    /// <summary>Initializes the composer.</summary>
    public SponsorComposer(ISponsorQueries queries, ISponsorRepository sponsors, SponsorSigning signing, IClock clock)
    {
        _queries = queries;
        _sponsors = sponsors;
        _signing = signing;
        _clock = clock;
    }

    /// <summary>Composes the page, or returns null when the club has no place or no offers in the current season.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SponsorResponse?> ComposeAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var context = await _queries.GetClubContextAsync(clubId, cancellationToken);

        if (context is null)
        {
            return null;
        }

        var offers = await _sponsors.ListOffersAsync(clubId, context.SeasonId, cancellationToken);

        if (offers.Count == 0)
        {
            return null;
        }

        var deal = await _sponsors.FindDealAsync(clubId, context.SeasonId, cancellationToken);
        var realistic = await _signing.RealisticObjectiveAsync(clubId, cancellationToken);
        var canSign = deal is null || await _signing.CanChangeAsync(context, deal, cancellationToken);

        return new SponsorResponse(
            context.SeasonNumber,
            SponsorDeadline.LockAtFor(context.FirstMatchday),
            realistic.ToCode(),
            context.PublishedMatchdays,
            [.. offers.Select(ToResponse)],
            deal is null ? null : ToResponse(deal, offers.First(offer => offer.Id == deal.OfferId)),
            canSign,
            deal?.Version ?? 0,
            _clock.UtcNow);
    }

    private static SponsorOfferResponse ToResponse(ClubSponsorOffer offer) =>
        new(
            offer.Kind.ToCode(),
            offer.SponsorName,
            offer.FixedSeasonMinor,
            EconomyRuleSet.PerMatchday(offer.FixedSeasonMinor),
            offer.BonusMultiplierBp,
            [
                .. SponsorOffers.Quotes(offer.Kind, offer.Tier, offer.Fans, offer.StandingPoints)
                    .Select(quote => new SponsorObjectiveQuoteResponse(
                        quote.Objective.ToCode(),
                        quote.TargetPlace,
                        quote.MetMinor,
                        quote.ExceededMinor)),
            ]);

    private static SponsorDealResponse ToResponse(ClubSponsorDeal deal, ClubSponsorOffer offer) =>
        new(
            deal.Kind.ToCode(),
            offer.SponsorName,
            deal.Objective.ToCode(),
            deal.TargetPlace,
            offer.FixedSeasonMinor,
            deal.FixedMatchdayMinor,
            deal.BonusMetMinor,
            deal.BonusExceededMinor,
            deal.SignedBy.ToCode(),
            deal.SignedAt,
            deal.Outcome?.ToCode(),
            deal.BonusPaidMinor,
            deal.FinalPlace);
}
