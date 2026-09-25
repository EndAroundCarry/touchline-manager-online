using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Contracts.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>The result of reading a club's squad.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Squad">The squad, when the read succeeded.</param>
public sealed record GetSquadResult(SquadReadOutcome Outcome, SquadResponse? Squad);

/// <summary>
/// Reads the squad of the club the caller manages (master plan §10.3; `SQ-1`, `SQ-2`).
/// </summary>
/// <remarks>
/// Read-only, so the only rule it enforces is the one master plan §10.9 asks for: the squad is the
/// caller's own. Which club that is comes from the tenure, never from the request — the request only says
/// which club the client believes it is looking at, and a mismatch is a refusal rather than a redirect, so
/// a stale client learns the truth instead of silently reading a different squad.
/// </remarks>
public sealed class GetSquad
{
    private readonly ResolveOwnedClub _access;
    private readonly ISquadQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public GetSquad(ResolveOwnedClub access, ISquadQueries queries, IClock clock)
    {
        _access = access;
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Reads the squad, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="clubId">The club whose squad is being read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetSquadResult> ExecuteAsync(
        Guid userId,
        Guid clubId,
        CancellationToken cancellationToken)
    {
        var access = await _access.ExecuteAsync(userId, clubId, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new GetSquadResult(access.Outcome.ToReadOutcome(), null);
        }

        var squad = await _queries.GetSquadAsync(clubId, cancellationToken);

        // The club existed a moment ago, so this is a race with a deletion rather than a bad request.
        return squad is null
            ? new GetSquadResult(SquadReadOutcome.ClubNotFound, null)
            : new GetSquadResult(SquadReadOutcome.Found, squad.ToResponse(_clock.UtcNow));
    }
}
