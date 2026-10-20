using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Competition;
using Tactical11Manager.Contracts.Competition;

namespace Tactical11Manager.Application.Competition;

/// <summary>The result of reading a club's fixture list.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Fixtures">The list, when the read succeeded.</param>
public sealed record GetClubFixturesResult(
    CompetitionReadOutcome Outcome,
    MyFixturesResponse? Fixtures);

/// <summary>
/// Reads the season's fixtures of any club, with the next one named (master plan §11.1).
/// </summary>
/// <remarks>
/// The same list <see cref="GetMyFixtures"/> reads for the caller's own club, for a club the caller may only
/// study. Fixtures and results are public game data, so nothing here is gated on holding the club; the opponent,
/// the venue and the outcome are read from this club's side, exactly as its own manager sees them.
/// </remarks>
public sealed class GetClubFixtures
{
    private readonly ICompetitionQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetClubFixtures(ICompetitionQueries queries, IClock clock)
    {
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Reads a club's fixture list.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetClubFixturesResult> ExecuteAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var snapshot = await _queries.GetClubFixturesAsync(clubId, cancellationToken);

        // The query answers null both for an unknown club and for a club with no division this season; the
        // first is the one a caller can cause, so that is what it is reported as.
        return snapshot is null
            ? new GetClubFixturesResult(CompetitionReadOutcome.ClubNotFound, null)
            : new GetClubFixturesResult(CompetitionReadOutcome.Found, snapshot.ToResponse(_clock.UtcNow));
    }
}
