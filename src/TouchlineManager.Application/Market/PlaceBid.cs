using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Squad;

namespace TouchlineManager.Application.Market;

/// <summary>
/// Places or raises a bid, reserving the amount and releasing the leader it displaces (`TRF-4`…`TRF-7`).
/// </summary>
/// <remarks>
/// <para>
/// The manager's command: it resolves the caller's club, then hands the write to the shared
/// <see cref="BidWriter"/> the AI's evaluation also uses (`INS-12`), and reads the listing back for the
/// response.
/// </para>
/// <para>
/// Nothing about the request is authoritative but the amount and the listing: the seller, the floor, and the
/// deadline all come from the stored listing (`INT-1`).
/// </para>
/// </remarks>
public sealed class PlaceBid
{
    private readonly ResolveOwnedClub _access;
    private readonly IBidWriter _writer;
    private readonly IMarketQueries _queries;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRequestContext _requestContext;
    private readonly ISecureTokenService _secureTokens;

    /// <summary>Initializes the use case.</summary>
    public PlaceBid(
        ResolveOwnedClub access,
        IBidWriter writer,
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

    /// <summary>Places or raises a bid, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="listingId">The listing bid on.</param>
    /// <param name="amountMinor">The amount, in minor units.</param>
    /// <param name="idempotencyKey">The request's idempotency key, which a retry is matched against (`INT-2`).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ListingResult> ExecuteAsync(
        Guid userId,
        Guid listingId,
        long amountMinor,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new ListingResult(access.Outcome.FromAccess(), null);
        }

        var actor = MarketActor.ForUser(userId, _requestContext, _secureTokens);

        // The bid writer serialises bids on a listing with a transaction-scoped lock, so the whole bid — the
        // leader it reads, the reservation it posts, and the bid it stages — is one transaction (ADR-0026).
        // A refusal leaves it uncommitted and the lock is released when the scope ends.
        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);

        var write = await _writer.BidAsync(
            access.ClubId,
            actor,
            listingId,
            amountMinor,
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

        await transaction.CommitAsync(cancellationToken);

        return new ListingResult(
            MarketOutcome.Found,
            await _queries.GetListingAsync(write.ListingId, access.ClubId, cancellationToken));
    }
}
