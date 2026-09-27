using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Contracts.Competition;

namespace TouchlineManager.Application.Competition;

/// <summary>The result of reading a division's player statistics.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Statistics">The statistics, when the read succeeded.</param>
public sealed record GetDivisionStatisticsResult(
    CompetitionReadOutcome Outcome,
    DivisionStatisticsResponse? Statistics);

/// <summary>
/// Reads a division's player season statistics for the season in progress (master plan §10.5, §11.1).
/// </summary>
/// <remarks>
/// Public game data, like the table, so nothing gates it on the caller holding a club. The rows arrive in
/// the order the projection stored them — goals, then assists, then name — so the screen reimplements no
/// ordering rule, and the average rating is converted from its stored basis points in one place (`TRN-8`).
/// </remarks>
public sealed class GetDivisionStatistics
{
    private readonly ICompetitionQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetDivisionStatistics(ICompetitionQueries queries, IClock clock)
    {
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Reads a division's player statistics.</summary>
    /// <param name="divisionId">The division.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetDivisionStatisticsResult> ExecuteAsync(
        Guid divisionId,
        CancellationToken cancellationToken)
    {
        var snapshot = await _queries.GetDivisionStatisticsAsync(divisionId, cancellationToken);

        return snapshot is null
            ? new GetDivisionStatisticsResult(CompetitionReadOutcome.DivisionNotFound, null)
            : new GetDivisionStatisticsResult(
                CompetitionReadOutcome.Found,
                snapshot.ToResponse(_clock.UtcNow));
    }
}
