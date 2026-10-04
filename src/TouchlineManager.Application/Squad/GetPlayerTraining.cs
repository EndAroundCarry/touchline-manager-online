using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Contracts.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>The result of reading a player's training regime and history.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Training">The regime and history, when the read succeeded.</param>
public sealed record GetPlayerTrainingResult(SquadReadOutcome Outcome, PlayerTrainingResponse? Training);

/// <summary>
/// Reads a player's training regime and their recent progression days (master plan §11.1; `TRN-17`).
/// </summary>
/// <remarks>
/// Authorized exactly like the profile: the player's own club is the one the caller must manage, so a manager
/// sees only their own players' training. A player who holds no active contract is a 404 for everybody.
/// </remarks>
public sealed class GetPlayerTraining
{
    /// <summary>The number of days returned when the caller does not say.</summary>
    public const int DefaultDays = 120;

    /// <summary>The most days one read may return.</summary>
    public const int MaxDays = 400;

    private readonly ResolveOwnedClub _access;
    private readonly ITrainingQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public GetPlayerTraining(ResolveOwnedClub access, ITrainingQueries queries, IClock clock)
    {
        _access = access;
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Reads the player's training, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="playerId">The player to read.</param>
    /// <param name="days">How many of the most recent progression days to return, clamped to a sane range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetPlayerTrainingResult> ExecuteAsync(
        Guid userId,
        Guid playerId,
        int? days,
        CancellationToken cancellationToken)
    {
        var window = Math.Clamp(days ?? DefaultDays, 1, MaxDays);

        var snapshot = await _queries.GetPlayerTrainingAsync(playerId, window, cancellationToken);

        if (snapshot is null)
        {
            return new GetPlayerTrainingResult(SquadReadOutcome.PlayerNotFound, null);
        }

        var access = await _access.ExecuteAsync(userId, snapshot.ClubId, cancellationToken);

        return access.Outcome == ClubAccessOutcome.Granted
            ? new GetPlayerTrainingResult(SquadReadOutcome.Found, snapshot.ToResponse(_clock.UtcNow))
            : new GetPlayerTrainingResult(access.Outcome.ToReadOutcome(), null);
    }
}
