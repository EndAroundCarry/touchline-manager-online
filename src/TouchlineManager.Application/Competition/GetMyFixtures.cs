using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Squad;
using TouchlineManager.Contracts.Competition;

namespace TouchlineManager.Application.Competition;

/// <summary>The result of reading the manager's own fixture list.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Fixtures">The list, when the read succeeded.</param>
public sealed record GetMyFixturesResult(
    CompetitionReadOutcome Outcome,
    MyFixturesResponse? Fixtures);

/// <summary>
/// Reads the fixtures of the club the caller manages, with the next one named (master plan §11.1).
/// </summary>
/// <remarks>
/// One response answers the dashboard and the fixtures screen: what the club plays next, when it locks,
/// and the whole season's results around it. Which club that is comes from the tenure, never from the
/// request, exactly as the squad reads do (`WORLD-9`).
/// </remarks>
public sealed class GetMyFixtures
{
    private readonly ResolveOwnedClub _access;
    private readonly ICompetitionQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetMyFixtures(ResolveOwnedClub access, ICompetitionQueries queries, IClock clock)
    {
        _access = access;
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Reads the manager's club's fixture list.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetMyFixturesResult> ExecuteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new GetMyFixturesResult(access.Outcome.ToCompetitionOutcome(), null);
        }

        var snapshot = await _queries.GetClubFixturesAsync(access.ClubId, cancellationToken);

        // The club exists — access was granted against it — so a missing snapshot means it has no season
        // in progress, which is a division lookup that failed rather than a club that vanished.
        return snapshot is null
            ? new GetMyFixturesResult(CompetitionReadOutcome.DivisionNotFound, null)
            : new GetMyFixturesResult(CompetitionReadOutcome.Found, snapshot.ToResponse(_clock.UtcNow));
    }
}
