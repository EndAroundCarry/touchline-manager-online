using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Contracts.Competition;

namespace TouchlineManager.Application.Competition;

/// <summary>The result of reading a division's discipline.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Discipline">The discipline, when the read succeeded.</param>
public sealed record GetDivisionDisciplineResult(
    CompetitionReadOutcome Outcome,
    DivisionDisciplineResponse? Discipline);

/// <summary>
/// Reads a division's discipline for the season in progress (master plan §10.5, `DIS-2`…`DIS-5`).
/// </summary>
/// <remarks>
/// Public game data, like the table and the statistics it sits beside: a card is shown in front of
/// everybody. The rows arrive in the order the server ranked them — most sendings-off, then most bookings,
/// then name — and the suspension figure comes from the open absences the publication serves, so the page
/// cannot disagree with the side a manager may actually name.
/// </remarks>
public sealed class GetDivisionDiscipline
{
    private readonly ICompetitionQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetDivisionDiscipline(ICompetitionQueries queries, IClock clock)
    {
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Reads a division's discipline.</summary>
    /// <param name="divisionId">The division.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetDivisionDisciplineResult> ExecuteAsync(
        Guid divisionId,
        CancellationToken cancellationToken)
    {
        var snapshot = await _queries.GetDivisionDisciplineAsync(divisionId, cancellationToken);

        return snapshot is null
            ? new GetDivisionDisciplineResult(CompetitionReadOutcome.DivisionNotFound, null)
            : new GetDivisionDisciplineResult(
                CompetitionReadOutcome.Found,
                snapshot.ToResponse(_clock.UtcNow));
    }
}
