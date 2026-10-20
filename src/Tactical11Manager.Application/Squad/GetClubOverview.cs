using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Squad;
using Tactical11Manager.Contracts.Squad;

namespace Tactical11Manager.Application.Squad;

/// <summary>The result of reading a club's public page.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Club">The page, when the read succeeded.</param>
public sealed record GetClubOverviewResult(SquadReadOutcome Outcome, ClubOverviewResponse? Club);

/// <summary>
/// Reads any club's public page: its details and its players' public attributes (master plan §11.1).
/// </summary>
/// <remarks>
/// Public game data like the fixture calendar and the scouting search (`SCT-1`), so it is not gated on holding
/// the club: a manager studies a rival before they play it. The caller's own tenure is read only to tell the
/// screen whether this is their club, and a caller without a club simply is not the owner of this one.
/// What a club keeps to itself (condition, morale, contracts, wages) is not in the response at all.
/// </remarks>
public sealed class GetClubOverview
{
    private readonly ResolveOwnedClub _access;
    private readonly ISquadQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public GetClubOverview(ResolveOwnedClub access, ISquadQueries queries, IClock clock)
    {
        _access = access;
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Reads the club, or reports that it does not exist.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="clubId">The club to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetClubOverviewResult> ExecuteAsync(
        Guid userId,
        Guid clubId,
        CancellationToken cancellationToken)
    {
        var overview = await _queries.GetClubOverviewAsync(clubId, cancellationToken);

        if (overview is null)
        {
            return new GetClubOverviewResult(SquadReadOutcome.ClubNotFound, null);
        }

        var own = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);
        var isOwnClub = own.Outcome == ClubAccessOutcome.Granted && own.ClubId == clubId;

        return new GetClubOverviewResult(
            SquadReadOutcome.Found,
            overview.ToResponse(isOwnClub, _clock.UtcNow));
    }
}
