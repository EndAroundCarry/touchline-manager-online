using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Squad;
using Tactical11Manager.Contracts.Squad;

namespace Tactical11Manager.Application.Squad;

/// <summary>
/// Quotes what releasing a player now would cost, and says whether the club may (`CON-12`).
/// </summary>
/// <remarks>
/// <para>
/// A read that decides nothing: the price comes from the same pure calculator the release charges, so the
/// amount a manager confirms is the amount paid. A release the rules refuse is still quoted, with the reason
/// beside it, so the screen can say why the button is off instead of letting the manager find out by trying.
/// </para>
/// <para>
/// The contract is resolved through the caller's own club, so a manager cannot quote — or probe for — another
/// club's contract.
/// </para>
/// </remarks>
public sealed class RequestReleaseQuote
{
    private readonly ResolveOwnedClub _access;
    private readonly ISquadQueries _queries;
    private readonly IClubAccountRepository _accounts;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public RequestReleaseQuote(
        ResolveOwnedClub access,
        ISquadQueries queries,
        IClubAccountRepository accounts,
        IClock clock)
    {
        _access = access;
        _queries = queries;
        _accounts = accounts;
        _clock = clock;
    }

    /// <summary>Quotes a release, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="contractId">The contract to quote.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RequestReleaseQuoteResult> ExecuteAsync(
        Guid userId,
        Guid contractId,
        CancellationToken cancellationToken)
    {
        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new RequestReleaseQuoteResult(ContractRelease.FromAccess(access.Outcome), null);
        }

        var context = await _queries.GetReleaseContextAsync(contractId, cancellationToken);

        // A contract that does not exist and one that belongs to another club are the same answer, so a
        // manager cannot probe for contract identities (master plan §10.9).
        if (context is null || context.ClubId != access.ClubId)
        {
            return new RequestReleaseQuoteResult(ContractReleaseOutcome.ContractNotFound, null);
        }

        var account = await _accounts.FindByClubAsync(access.ClubId, cancellationToken);

        if (account is null)
        {
            return new RequestReleaseQuoteResult(ContractReleaseOutcome.ClubNotFound, null);
        }

        var now = _clock.UtcNow;
        var quote = ContractRelease.Price(context, now);
        var refusal = ContractRelease.Refusal(context, quote, account.AvailableMinor);

        return new RequestReleaseQuoteResult(
            ContractReleaseOutcome.Quoted,
            new ReleaseQuoteResponse(
                context.ContractId,
                context.PlayerId,
                quote.RemainingWeeks,
                quote.PayoutMinor,
                context.SeasonWageMinor,
                account.AvailableMinor,
                ContractRelease.JoinsPoolAt(now),
                ContractRelease.CodeOf(refusal),
                context.ContractVersion,
                now));
    }
}
