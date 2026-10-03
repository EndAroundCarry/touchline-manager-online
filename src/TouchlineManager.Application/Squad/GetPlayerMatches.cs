using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Contracts.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>The result of reading a player's match-by-match history.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Matches">The history, when the read succeeded.</param>
public sealed record GetPlayerMatchesResult(SquadReadOutcome Outcome, PlayerMatchesResponse? Matches);

/// <summary>
/// Reads a player's match-by-match statistics (master plan §11.1; `STA-2`).
/// </summary>
/// <remarks>
/// Authorized exactly like the profile: the player's own club is the one the caller must manage. The rows come
/// from the stored results the season statistics are summed from, so they add up to the season line.
/// </remarks>
public sealed class GetPlayerMatches
{
    private readonly ResolveOwnedClub _access;
    private readonly ISquadQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public GetPlayerMatches(ResolveOwnedClub access, ISquadQueries queries, IClock clock)
    {
        _access = access;
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Reads the player's matches, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="playerId">The player to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetPlayerMatchesResult> ExecuteAsync(
        Guid userId,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        var player = await _queries.GetPlayerAsync(playerId, cancellationToken);

        if (player is null)
        {
            return new GetPlayerMatchesResult(SquadReadOutcome.PlayerNotFound, null);
        }

        var access = await _access.ExecuteAsync(userId, player.ClubId, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new GetPlayerMatchesResult(access.Outcome.ToReadOutcome(), null);
        }

        var sources = await _queries.GetPlayerMatchesAsync(playerId, cancellationToken);

        var matches = sources
            .Select(source => source.ToMatchResponse(playerId))
            .OfType<PlayerMatchStatResponse>()
            .ToList();

        return new GetPlayerMatchesResult(
            SquadReadOutcome.Found,
            new PlayerMatchesResponse(playerId, matches, _clock.UtcNow));
    }
}
