using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Contracts.Competition;

namespace TouchlineManager.Application.Competition;

/// <summary>The result of reading a division's fixture calendar.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Fixtures">The calendar, when the read succeeded.</param>
public sealed record ListDivisionFixturesResult(
    CompetitionReadOutcome Outcome,
    DivisionFixturesResponse? Fixtures);

/// <summary>
/// Reads a division-season's whole fixture calendar (master plan §10.5; `CAL-1`, `CAL-8`).
/// </summary>
/// <remarks>
/// Public game data, so it is not gated on holding a club: a calendar is what a manager plans against, and
/// hiding another division's fixtures would hide the season's shape rather than anybody's private state.
/// Reading it is still authenticated, because every manager-facing route is.
/// </remarks>
public sealed class ListDivisionFixtures
{
    private readonly IClock _clock;
    private readonly ICompetitionQueries _queries;

    /// <summary>Initializes the query.</summary>
    public ListDivisionFixtures(IClock clock, ICompetitionQueries queries)
    {
        _clock = clock;
        _queries = queries;
    }

    /// <summary>Reads the calendar for the season in progress.</summary>
    /// <param name="divisionId">The division to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ListDivisionFixturesResult> ExecuteAsync(
        Guid divisionId,
        CancellationToken cancellationToken)
    {
        var snapshot = await _queries.GetDivisionFixturesAsync(divisionId, cancellationToken);

        return snapshot is null
            ? new ListDivisionFixturesResult(CompetitionReadOutcome.DivisionNotFound, null)
            : new ListDivisionFixturesResult(
                CompetitionReadOutcome.Found,
                snapshot.ToResponse(_clock.UtcNow));
    }
}
