using Tactical11Manager.Application.Squad;
using Tactical11Manager.Contracts.Finance;

namespace Tactical11Manager.Application.Finance;

/// <summary>The result of reading a club's sponsor page.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Sponsor">The page, when the read succeeded.</param>
public sealed record GetSponsorOffersResult(SquadReadOutcome Outcome, SponsorResponse? Sponsor);

/// <summary>
/// Reads the sponsor offers, the deal and the objective of the club the caller manages (`SPN-1`…`SPN-6`).
/// </summary>
/// <remarks>The club is resolved from the tenure, so a manager can only read their own club's offers.</remarks>
public sealed class GetSponsorOffers
{
    private readonly ResolveOwnedClub _access;
    private readonly SponsorComposer _composer;

    /// <summary>Initializes the query.</summary>
    public GetSponsorOffers(ResolveOwnedClub access, SponsorComposer composer)
    {
        _access = access;
        _composer = composer;
    }

    /// <summary>Reads the page, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetSponsorOffersResult> ExecuteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new GetSponsorOffersResult(access.Outcome.ToReadOutcome(), null);
        }

        var sponsor = await _composer.ComposeAsync(access.ClubId, cancellationToken);

        return sponsor is null
            ? new GetSponsorOffersResult(SquadReadOutcome.ClubNotFound, null)
            : new GetSponsorOffersResult(SquadReadOutcome.Found, sponsor);
    }
}
