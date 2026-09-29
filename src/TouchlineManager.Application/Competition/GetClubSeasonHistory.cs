using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Contracts.Competition;

namespace TouchlineManager.Application.Competition;

/// <summary>The result of reading a club's season history.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="History">The history, when the read succeeded.</param>
public sealed record GetClubSeasonHistoryResult(
    CompetitionReadOutcome Outcome,
    ClubSeasonHistoryResponse? History);

/// <summary>
/// Reads a club's finished seasons and, when it is already known, the next season it is placed in (master
/// plan §11.1, `PR-4`, `PR-6`).
/// </summary>
/// <remarks>
/// Public game data, like the table, so nothing gates it on the caller holding the club: a season's record is
/// a record of results everyone can see. The rows are the entries the rollover closed once and never rewrote,
/// so they do not change under a read, and the next season is what the rollover has already committed to
/// rather than a projection.
/// </remarks>
public sealed class GetClubSeasonHistory
{
    private readonly ICompetitionQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetClubSeasonHistory(ICompetitionQueries queries, IClock clock)
    {
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Reads a club's season history.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetClubSeasonHistoryResult> ExecuteAsync(
        Guid clubId,
        CancellationToken cancellationToken)
    {
        var snapshot = await _queries.GetClubSeasonHistoryAsync(clubId, cancellationToken);

        return snapshot is null
            ? new GetClubSeasonHistoryResult(CompetitionReadOutcome.ClubNotFound, null)
            : new GetClubSeasonHistoryResult(
                CompetitionReadOutcome.Found,
                snapshot.ToResponse(_clock.UtcNow));
    }
}
