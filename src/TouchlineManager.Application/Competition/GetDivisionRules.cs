using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Contracts.Competition;

namespace TouchlineManager.Application.Competition;

/// <summary>The result of reading a division's competition rules.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Rules">The rules, when the read succeeded.</param>
public sealed record GetDivisionRulesResult(CompetitionReadOutcome Outcome, DivisionRulesResponse? Rules);

/// <summary>
/// Reads a division's competition rules for the season in progress (master plan §10.5, `TBL-1`…`TBL-11`).
/// </summary>
/// <remarks>
/// Public game data, like the table and the calendar, so nothing gates it on the caller holding a club: the
/// rules are what every manager is playing by. The points and the ordering come from the domain — the same
/// constants the calculator ranks with — and the draw is the season's stored one, so a manager reading the
/// page reads the rules the season was actually played under rather than a copy of the document.
/// </remarks>
public sealed class GetDivisionRules
{
    private readonly ICompetitionQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetDivisionRules(ICompetitionQueries queries, IClock clock)
    {
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Reads a division's competition rules.</summary>
    /// <param name="divisionId">The division.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetDivisionRulesResult> ExecuteAsync(
        Guid divisionId,
        CancellationToken cancellationToken)
    {
        var snapshot = await _queries.GetDivisionRulesAsync(divisionId, cancellationToken);

        return snapshot is null
            ? new GetDivisionRulesResult(CompetitionReadOutcome.DivisionNotFound, null)
            : new GetDivisionRulesResult(
                CompetitionReadOutcome.Found,
                snapshot.ToResponse(_clock.UtcNow));
    }
}
