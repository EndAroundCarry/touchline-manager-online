using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Application.Competition;
using TouchlineManager.Contracts.Competition;

namespace TouchlineManager.Application.Squad;

/// <summary>The result of reading a club's prepared side for a fixture.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="TeamSheet">The prepared side, when the read succeeded.</param>
public sealed record GetFixtureTeamSheetResult(
    CompetitionReadOutcome Outcome,
    FixtureTeamSheetResponse? TeamSheet);

/// <summary>
/// Reads the caller's club's side for one fixture (master plan §10.4, §11.1; `SQ-4`).
/// </summary>
/// <remarks>
/// The club is the caller's own, resolved from their tenure rather than from the request. A fixture they
/// do not play in is refused as <see cref="CompetitionReadOutcome.ClubNotManaged"/> — which the API answers
/// as <c>FIXTURE_NOT_YOURS</c> — so a manager learns that this is not their fixture rather than being told
/// it does not exist. A club with no default plan is not a refusal: the response says so by naming no plan,
/// and the screen offers the tactics screen instead of a selection.
/// </remarks>
public sealed class GetFixtureTeamSheet
{
    private readonly ResolveOwnedClub _access;
    private readonly ITeamSheetQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetFixtureTeamSheet(ResolveOwnedClub access, ITeamSheetQueries queries, IClock clock)
    {
        _access = access;
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Reads the caller's side for a fixture.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="fixtureId">The fixture.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetFixtureTeamSheetResult> ExecuteAsync(
        Guid userId,
        Guid fixtureId,
        CancellationToken cancellationToken)
    {
        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new GetFixtureTeamSheetResult(access.Outcome.ToCompetitionOutcome(), null);
        }

        var snapshot = await _queries.GetTeamSheetAsync(fixtureId, access.ClubId, cancellationToken);

        if (snapshot is null)
        {
            return new GetFixtureTeamSheetResult(CompetitionReadOutcome.FixtureNotFound, null);
        }

        if (snapshot.Fixture.HomeClubId != access.ClubId && snapshot.Fixture.AwayClubId != access.ClubId)
        {
            return new GetFixtureTeamSheetResult(CompetitionReadOutcome.ClubNotManaged, null);
        }

        return new GetFixtureTeamSheetResult(
            CompetitionReadOutcome.Found,
            snapshot.ToResponse(access.ClubId, _clock.UtcNow));
    }
}
