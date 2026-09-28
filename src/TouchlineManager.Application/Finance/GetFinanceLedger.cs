using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Squad;
using TouchlineManager.Contracts.Finance;

namespace TouchlineManager.Application.Finance;

/// <summary>The result of reading a club's ledger.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Ledger">The page, when the read succeeded.</param>
public sealed record GetFinanceLedgerResult(SquadReadOutcome Outcome, FinanceLedgerResponse? Ledger);

/// <summary>
/// Reads one page of the club the caller manages' ledger, newest first (master plan §10.7; `FIN-11`,
/// `FIN-12`).
/// </summary>
/// <remarks>
/// The ledger grows without bound over a world's life, so it is keyset-paginated by sequence rather than
/// returned whole. The club is resolved from the tenure, so a manager can only walk their own ledger.
/// </remarks>
public sealed class GetFinanceLedger
{
    /// <summary>How many entries one page holds. Small enough to render, large enough to be useful.</summary>
    private const int PageSize = 25;

    private readonly ResolveOwnedClub _access;
    private readonly IFinanceQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetFinanceLedger(ResolveOwnedClub access, IFinanceQueries queries, IClock clock)
    {
        _access = access;
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Reads a page of the ledger, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="beforeSequence">The cursor's exclusive upper bound, or null for the first page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetFinanceLedgerResult> ExecuteAsync(
        Guid userId,
        long? beforeSequence,
        CancellationToken cancellationToken)
    {
        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new GetFinanceLedgerResult(access.Outcome.ToReadOutcome(), null);
        }

        var page = await _queries.GetFinanceLedgerAsync(
            access.ClubId,
            beforeSequence,
            PageSize,
            cancellationToken);

        return new GetFinanceLedgerResult(SquadReadOutcome.Found, page.ToResponse(_clock.UtcNow));
    }
}
