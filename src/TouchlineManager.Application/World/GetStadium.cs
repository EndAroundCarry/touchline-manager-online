using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Squad;
using TouchlineManager.Contracts.World;

namespace TouchlineManager.Application.World;

/// <summary>The result of reading a club's stadium.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Stadium">The stadium, when the read succeeded.</param>
public sealed record GetStadiumResult(SquadReadOutcome Outcome, StadiumResponse? Stadium);

/// <summary>
/// Reads the stadium of the club the caller manages (`STAD-1`…`STAD-6`).
/// </summary>
/// <remarks>
/// The club is resolved from the tenure rather than named by the request, so a manager can only ever read
/// their own ground and their own purse: the build costs quoted beside it are priced for their tier.
/// </remarks>
public sealed class GetStadium
{
    private readonly ResolveOwnedClub _access;
    private readonly IStadiumRepository _stadiums;
    private readonly IStadiumQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetStadium(
        ResolveOwnedClub access,
        IStadiumRepository stadiums,
        IStadiumQueries queries,
        IClock clock)
    {
        _access = access;
        _stadiums = stadiums;
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Reads the stadium, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetStadiumResult> ExecuteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new GetStadiumResult(access.Outcome.ToReadOutcome(), null);
        }

        var stadium = await _stadiums.FindByClubAsync(access.ClubId, cancellationToken);
        var context = await _queries.GetContextAsync(access.ClubId, cancellationToken);

        // A club without a ground or an account is a defect in generation, not an ordinary state; the read
        // reports it as a missing club rather than inventing a stadium.
        return stadium is null || context is null
            ? new GetStadiumResult(SquadReadOutcome.ClubNotFound, null)
            : new GetStadiumResult(SquadReadOutcome.Found, stadium.ToResponse(context, _clock.UtcNow));
    }
}
