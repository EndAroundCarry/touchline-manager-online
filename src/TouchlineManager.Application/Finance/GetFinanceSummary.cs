using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Squad;
using TouchlineManager.Contracts.Finance;

namespace TouchlineManager.Application.Finance;

/// <summary>The result of reading a club's finance summary.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Summary">The summary, when the read succeeded.</param>
public sealed record GetFinanceSummaryResult(SquadReadOutcome Outcome, FinanceSummaryResponse? Summary);

/// <summary>
/// Reads the club the caller manages: its money and its season's totals by category (master plan §10.7;
/// `FIN-3`…`FIN-9`).
/// </summary>
/// <remarks>
/// The club is resolved from the tenure rather than named by the request, so a manager can only ever read
/// their own money — balances and wages are club state, not public data (`data-classification.md` §2).
/// </remarks>
public sealed class GetFinanceSummary
{
    private readonly ResolveOwnedClub _access;
    private readonly IFinanceQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetFinanceSummary(ResolveOwnedClub access, IFinanceQueries queries, IClock clock)
    {
        _access = access;
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Reads the summary, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetFinanceSummaryResult> ExecuteAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new GetFinanceSummaryResult(access.Outcome.ToReadOutcome(), null);
        }

        var summary = await _queries.GetFinanceSummaryAsync(access.ClubId, cancellationToken);

        // A club without an account is a defect in generation, not an ordinary state; the read reports it as
        // a missing club rather than inventing a zero balance.
        return summary is null
            ? new GetFinanceSummaryResult(SquadReadOutcome.ClubNotFound, null)
            : new GetFinanceSummaryResult(SquadReadOutcome.Found, summary.ToResponse(_clock.UtcNow));
    }
}
