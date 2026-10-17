using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Market;
using Tactical11Manager.Application.Squad;

namespace Tactical11Manager.Application.Market;

/// <summary>
/// Reads one player's exact public attributes, so a name shown anywhere can open the same skills card (`SCT-1`).
/// </summary>
/// <remarks>
/// Scouting already shows every player's exact attributes to any manager holding a club, so this is the same
/// information addressed by identity rather than found by a search: the caller must hold a club, and a player
/// with no active contract is not found, exactly as they would not be found by a search.
/// </remarks>
public sealed class GetPlayerCard
{
    private readonly ResolveOwnedClub _access;
    private readonly IMarketQueries _queries;

    /// <summary>Initializes the query.</summary>
    public GetPlayerCard(ResolveOwnedClub access, IMarketQueries queries)
    {
        _access = access;
        _queries = queries;
    }

    /// <summary>Reads the player, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="playerId">The player to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<PlayerCardResult> ExecuteAsync(
        Guid userId,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new PlayerCardResult(access.Outcome.FromAccess(), null);
        }

        var row = await _queries.GetPlayerAsync(playerId, cancellationToken);

        return row is null
            ? new PlayerCardResult(MarketOutcome.NotFound, null)
            : new PlayerCardResult(MarketOutcome.Found, row);
    }
}
