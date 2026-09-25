using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Contracts.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>The result of reading a player's profile.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Player">The profile, when the read succeeded.</param>
public sealed record GetPlayerResult(SquadReadOutcome Outcome, PlayerResponse? Player);

/// <summary>
/// Reads one player's profile (master plan §10.3, §11.1; `TRN-4`, `TRN-8`).
/// </summary>
/// <remarks>
/// <para>
/// The player is resolved first, because the club to authorize against is the player's own and the request
/// does not say which club that is. A player who does not exist is therefore a 404 for everybody, which is
/// what it should be: identities are generated and unguessable, and a player's existence is public game
/// data (`data-classification.md` §1).
/// </para>
/// <para>
/// Attribute values are exact and public (`SCT-1`), but condition and contract terms are club state, so
/// the profile as a whole is still the owning manager's read. The public, unattached player profile is
/// Stage 10's scouting surface.
/// </para>
/// </remarks>
public sealed class GetPlayer
{
    private readonly ResolveOwnedClub _access;
    private readonly ISquadQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public GetPlayer(ResolveOwnedClub access, ISquadQueries queries, IClock clock)
    {
        _access = access;
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Reads the player, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="playerId">The player to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetPlayerResult> ExecuteAsync(
        Guid userId,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        var player = await _queries.GetPlayerAsync(playerId, cancellationToken);

        if (player is null)
        {
            return new GetPlayerResult(SquadReadOutcome.PlayerNotFound, null);
        }

        var access = await _access.ExecuteAsync(userId, player.ClubId, cancellationToken);

        return access.Outcome == ClubAccessOutcome.Granted
            ? new GetPlayerResult(SquadReadOutcome.Found, player.ToResponse(_clock.UtcNow))
            : new GetPlayerResult(access.Outcome.ToReadOutcome(), null);
    }
}
