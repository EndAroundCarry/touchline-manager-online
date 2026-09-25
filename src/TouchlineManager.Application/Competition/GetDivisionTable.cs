using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Contracts.Competition;

namespace TouchlineManager.Application.Competition;

/// <summary>The result of reading a division's table.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Table">The table, when the read succeeded.</param>
public sealed record GetDivisionTableResult(CompetitionReadOutcome Outcome, DivisionTableResponse? Table);

/// <summary>
/// Reads a division's league table for the season in progress (master plan §10.5, §11.1).
/// </summary>
/// <remarks>
/// Public game data, like the fixture calendar, so nothing gates it on the caller holding a club: a table is
/// the thing a manager looks at to see where they stand, and one they can share with anybody. The rows come
/// back in the order the division is ranked, which is the order the projection stored, so the screen does
/// not have to know a single tie-break rule.
/// </remarks>
public sealed class GetDivisionTable
{
    private readonly ICompetitionQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetDivisionTable(ICompetitionQueries queries, IClock clock)
    {
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Reads a division's table.</summary>
    /// <param name="divisionId">The division.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetDivisionTableResult> ExecuteAsync(
        Guid divisionId,
        CancellationToken cancellationToken)
    {
        var snapshot = await _queries.GetDivisionTableAsync(divisionId, cancellationToken);

        return snapshot is null
            ? new GetDivisionTableResult(CompetitionReadOutcome.DivisionNotFound, null)
            : new GetDivisionTableResult(
                CompetitionReadOutcome.Found,
                snapshot.ToResponse(_clock.UtcNow));
    }
}
