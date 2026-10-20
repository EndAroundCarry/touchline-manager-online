using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Squad;
using Tactical11Manager.Contracts.Squad;

namespace Tactical11Manager.Application.Squad;

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
/// Attribute values are exact and public (`SCT-1`), but condition and contract terms are club state. So the
/// owning manager reads the whole profile, and every other manager reads the public half: the attributes,
/// the biography and the statistics. The state, contract, registration and injuries are left out of that
/// response rather than hidden by the client.
/// </para>
/// <para>
/// A free agent has no club to authorize against (`FA-5`), so any manager holding a club reads the public
/// half, exactly as they read a rival's player.
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

        if (player.ClubId is null)
        {
            // With no club to match, a granted access is the caller's own club: the public view all the same.
            return access.Outcome == ClubAccessOutcome.Granted
                ? new GetPlayerResult(SquadReadOutcome.Found, player.ToResponse(isOwnClub: false, _clock.UtcNow))
                : new GetPlayerResult(access.Outcome.ToReadOutcome(), null);
        }

        return access.Outcome switch
        {
            ClubAccessOutcome.Granted => new GetPlayerResult(
                SquadReadOutcome.Found,
                player.ToResponse(isOwnClub: true, _clock.UtcNow)),
            ClubAccessOutcome.ClubNotManaged => new GetPlayerResult(
                SquadReadOutcome.Found,
                player.ToResponse(isOwnClub: false, _clock.UtcNow)),
            _ => new GetPlayerResult(access.Outcome.ToReadOutcome(), null),
        };
    }
}
