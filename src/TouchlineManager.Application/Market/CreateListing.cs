using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Squad;

namespace TouchlineManager.Application.Market;

/// <summary>
/// Lists an eligible player for sale with a minimum fee and the buyer's precomputed terms (`TRF-1`, `TRF-2`,
/// `TRF-14`, `CON-5`).
/// </summary>
/// <remarks>
/// <para>
/// The manager's command: it resolves the caller's club, then hands the write to the shared
/// <see cref="ListingWriter"/> the AI's evaluation also uses (`INS-12`), and reads the listing back for the
/// response.
/// </para>
/// <para>
/// The seller names a player and a minimum fee; the server derives everything else. The listing is refused
/// when the sale would take the seller below the minimum squad or its two goalkeepers (`SQ-2`), because a
/// manager cannot sell a squad into illegality.
/// </para>
/// </remarks>
public sealed class CreateListing
{
    private readonly ResolveOwnedClub _access;
    private readonly IListingWriter _writer;
    private readonly IMarketQueries _queries;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRequestContext _requestContext;
    private readonly ISecureTokenService _secureTokens;

    /// <summary>Initializes the use case.</summary>
    public CreateListing(
        ResolveOwnedClub access,
        IListingWriter writer,
        IMarketQueries queries,
        IUnitOfWork unitOfWork,
        IRequestContext requestContext,
        ISecureTokenService secureTokens)
    {
        _access = access;
        _writer = writer;
        _queries = queries;
        _unitOfWork = unitOfWork;
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

        var actor = MarketActor.ForUser(userId, _requestContext, _secureTokens);

        var write = await _writer.ListAsync(
            access.ClubId,
            actor,
            playerId,
            minimumFeeMinor,
            seasons,
            idempotencyKey,
            cancellationToken);

        if (write.Outcome != MarketOutcome.Found)
        {
            return new ListingResult(write.Outcome, null);
        }

        if (write.Created)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new ListingResult(
            MarketOutcome.Found,
            await _queries.GetListingAsync(write.ListingId, access.ClubId, cancellationToken));
    }
}
