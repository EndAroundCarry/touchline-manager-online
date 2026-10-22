using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Auth;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Squad;
using Tactical11Manager.Contracts.Finance;
using Tactical11Manager.Domain.Finance;

namespace Tactical11Manager.Application.Finance;

/// <summary>What happened when a manager signed a sponsor deal.</summary>
public enum SignSponsorDealOutcome
{
    /// <summary>The deal was signed or changed.</summary>
    Agreed = 0,

    /// <summary>No world has been seeded.</summary>
    WorldNotSeeded = 1,

    /// <summary>The account has no manager profile.</summary>
    NoManagerProfile = 2,

    /// <summary>The manager holds no club.</summary>
    NoClub = 3,

    /// <summary>The club has no place or no offers in the current season.</summary>
    ClubNotFound = 4,

    /// <summary>The request named an offer that does not exist.</summary>
    UnknownOffer = 5,

    /// <summary>The request named an objective that does not exist.</summary>
    UnknownObjective = 6,

    /// <summary>No <c>If-Match</c> version was supplied (`CONC-1`).</summary>
    PreconditionRequired = 7,

    /// <summary>The supplied version was stale. The client must reload and decide again.</summary>
    PreconditionFailed = 8,

    /// <summary>The club has signed and may no longer choose (`SPN-2`).</summary>
    AlreadySigned = 9,
}

/// <summary>The result of signing.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Sponsor">The page as it now stands, when the signing went through.</param>
public sealed record SignSponsorDealResult(SignSponsorDealOutcome Outcome, SponsorResponse? Sponsor);

/// <summary>
/// Signs the sponsor deal of the club the caller manages: an offer and an objective (`SPN-2`, `OBJ-1`).
/// </summary>
/// <remarks>
/// <para>
/// The deal's version is the strong entity tag, 0 before there is a deal, and the request must send it back in <c>If-Match</c>. A double-click,
/// a retry or a second device therefore finds the version moved on and is refused rather than signing twice (`CONC-1`); the ledger entry for
/// the matchdays already played is keyed by the deal, which is the same guarantee at the money's own layer (`FIN-17`).
/// </para>
/// <para>
/// A new manager may sign again in the first matchdays after taking a club over, which replaces the deal the club had (`SPN-6`). Every other
/// manager signs once, until the board does it for them at the deadline.
/// </para>
/// </remarks>
public sealed class SignSponsorDeal
{
    private readonly ResolveOwnedClub _access;
    private readonly SponsorSigning _signing;
    private readonly SponsorComposer _composer;
    private readonly IClock _clock;
    private readonly IRequestContext _requestContext;
    private readonly ISecureTokenService _secureTokens;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public SignSponsorDeal(
        ResolveOwnedClub access,
        SponsorSigning signing,
        SponsorComposer composer,
        IClock clock,
        IRequestContext requestContext,
        ISecureTokenService secureTokens,
        IUnitOfWork unitOfWork)
    {
        _access = access;
        _signing = signing;
        _composer = composer;
        _clock = clock;
        _requestContext = requestContext;
        _secureTokens = secureTokens;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Signs the deal, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="request">The offer and the objective chosen.</param>
    /// <param name="expectedVersion">The version from the request's <c>If-Match</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SignSponsorDealResult> ExecuteAsync(
        Guid userId,
        SignSponsorRequest request,
        long? expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return Refused(FromAccess(access.Outcome));
        }

        if (!TryOffer(request.Offer, out var kind))
        {
            return Refused(SignSponsorDealOutcome.UnknownOffer);
        }

        if (!TryObjective(request.Objective, out var objective))
        {
            return Refused(SignSponsorDealOutcome.UnknownObjective);
        }

        if (expectedVersion is null)
        {
            return Refused(SignSponsorDealOutcome.PreconditionRequired);
        }

        var actor = new SponsorActor(
            SponsorSigner.Manager,
            userId,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress));

        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);

        var result = await _signing.SignAsync(
            access.ClubId,
            kind,
            objective,
            expectedVersion,
            actor,
            _clock.UtcNow,
            cancellationToken);

        if (result.Outcome is not (SponsorSigningOutcome.Agreed or SponsorSigningOutcome.Changed))
        {
            await transaction.RollbackAsync(cancellationToken);

            return Refused(result.Outcome switch
            {
                SponsorSigningOutcome.PreconditionFailed => SignSponsorDealOutcome.PreconditionFailed,
                SponsorSigningOutcome.AlreadySigned => SignSponsorDealOutcome.AlreadySigned,
                _ => SignSponsorDealOutcome.ClubNotFound,
            });
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return Refused(SignSponsorDealOutcome.PreconditionFailed);
        }

        await transaction.CommitAsync(cancellationToken);

        var sponsor = await _composer.ComposeAsync(access.ClubId, cancellationToken);

        return sponsor is null
            ? Refused(SignSponsorDealOutcome.ClubNotFound)
            : new SignSponsorDealResult(SignSponsorDealOutcome.Agreed, sponsor);
    }

    private static SignSponsorDealResult Refused(SignSponsorDealOutcome outcome) => new(outcome, null);

    private static bool TryOffer(string? code, out SponsorOfferKind kind)
    {
        kind = default;

        if (code is null || !SponsorCodes.OfferCodes.Contains(code))
        {
            return false;
        }

        kind = SponsorCodes.OfferFromCode(code);

        return true;
    }

    private static bool TryObjective(string? code, out SeasonObjective objective)
    {
        objective = default;

        if (code is null || !SponsorCodes.ObjectiveCodes.Contains(code))
        {
            return false;
        }

        objective = SponsorCodes.ObjectiveFromCode(code);

        return true;
    }

    private static SignSponsorDealOutcome FromAccess(ClubAccessOutcome outcome) => outcome switch
    {
        ClubAccessOutcome.WorldNotSeeded => SignSponsorDealOutcome.WorldNotSeeded,
        ClubAccessOutcome.NoManagerProfile => SignSponsorDealOutcome.NoManagerProfile,
        ClubAccessOutcome.NoClub => SignSponsorDealOutcome.NoClub,
        _ => SignSponsorDealOutcome.ClubNotFound,
    };
}
