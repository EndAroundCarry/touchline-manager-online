using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>
/// Produces the deterministic renewal quote for a player (`CON-3`, master plan §10.3).
/// </summary>
/// <remarks>
/// <para>
/// A read that decides nothing: it gathers the server-only facts a quote is derived from, runs the pure
/// calculator, and returns the wage and term. Asking twice gives the same answer, because the quote is a
/// function of the player and not of when it was requested.
/// </para>
/// <para>
/// The contract is resolved through the caller's own club, so a manager cannot quote — or probe for — another
/// club's contract. <c>CON-4</c>'s decline needs no command: not accepting is doing nothing.
/// </para>
/// </remarks>
public sealed class RequestRenewalQuote
{
    private readonly ResolveOwnedClub _access;
    private readonly ISquadQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public RequestRenewalQuote(ResolveOwnedClub access, ISquadQueries queries, IClock clock)
    {
        _access = access;
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Quotes a renewal, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="contractId">The contract to quote.</param>
    /// <param name="seasons">The proposed length, 1–3 seasons.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RequestRenewalQuoteResult> ExecuteAsync(
        Guid userId,
        Guid contractId,
        int seasons,
        CancellationToken cancellationToken)
    {
        if (!ContractRenewal.IsLegalTerm(seasons))
        {
            return new RequestRenewalQuoteResult(ContractRenewalOutcome.InvalidTerm, null);
        }

        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new RequestRenewalQuoteResult(ContractRenewal.FromAccess(access.Outcome), null);
        }

        var context = await _queries.GetRenewalContextAsync(contractId, cancellationToken);

        // A contract that does not exist and one that belongs to another club are the same answer, so a
        // manager cannot probe for contract identities (master plan §10.9).
        if (context is null || context.ClubId != access.ClubId)
        {
            return new RequestRenewalQuoteResult(ContractRenewalOutcome.ContractNotFound, null);
        }

        return new RequestRenewalQuoteResult(
            ContractRenewalOutcome.Quoted,
            Quote(context, seasons, _clock.UtcNow));
    }

    /// <summary>Builds the quote response from the context and the requested term.</summary>
    internal static RenewalQuoteResponse Quote(
        ContractRenewalContext context,
        int seasons,
        DateTimeOffset now)
    {
        var terms = ContractRenewalQuote.Calculate(ContractRenewal.ToInput(context), seasons);
        var start = context.CurrentSeasonNumber;

        return new RenewalQuoteResponse(
            context.ContractId,
            context.PlayerId,
            seasons,
            start,
            start + seasons - 1,
            terms.WeeklyWageMinor,
            context.ContractVersion,
            now);
    }
}
