using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Contracts.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>The result of reading a club's tactical plans.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Tactics">The plans and squad, when the read succeeded.</param>
public sealed record GetTacticsResult(SquadReadOutcome Outcome, TacticsResponse? Tactics);

/// <summary>
/// Reads the tactical plans of the club the caller manages (master plan §10.4, §11.1; F-19).
/// </summary>
/// <remarks>
/// The tactics screen's subject is the caller's own club, resolved from their tenure rather than named
/// in the request, for the same reason the squad reads are: a plan holds what a manager intends, which
/// is club state.
/// </remarks>
public sealed class GetTactics
{
    private readonly ResolveOwnedClub _access;
    private readonly ITacticsQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public GetTactics(ResolveOwnedClub access, ITacticsQueries queries, IClock clock)
    {
        _access = access;
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Reads the plans, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetTacticsResult> ExecuteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new GetTacticsResult(access.Outcome.ToReadOutcome(), null);
        }

        var snapshot = await _queries.GetTacticsAsync(access.ClubId, cancellationToken);

        return snapshot is null
            ? new GetTacticsResult(SquadReadOutcome.ClubNotFound, null)
            : new GetTacticsResult(SquadReadOutcome.Found, snapshot.ToResponse(_clock.UtcNow));
    }
}
