using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Abstractions.Squad;

/// <summary>One player as the daily progression job loads them.</summary>
/// <remarks>
/// The aggregates are tracked rather than projected, because the job mutates them; the hidden potential is
/// carried as a value because the development ceiling needs it and the job is not a manager-facing
/// surface (`data-classification.md` §2.1).
/// </remarks>
/// <param name="Player">The player, for their age and identity.</param>
/// <param name="Attributes">The player's attributes, which development writes back.</param>
/// <param name="State">The player's state, which recovery and development write back.</param>
/// <param name="Potential">The hidden development ceiling (`TRN-9`).</param>
/// <param name="IndividualFocus">The player's individual focus, when one is set (`TRN-2`).</param>
public sealed record ProgressablePlayer(
    Player Player,
    PlayerAttributes Attributes,
    PlayerState State,
    int Potential,
    AttributeFamily? IndividualFocus);

/// <summary>One club's training plan and the squad it applies to.</summary>
/// <param name="ClubId">The club.</param>
/// <param name="GameYear">The season's game year, which fixes each player's age (`TIME-3`).</param>
/// <param name="TeamFocus">The club's team focus, or the implicit default when it has no plan (`TRN-1`).</param>
/// <param name="Intensity">The club's intensity, or the implicit default when it has no plan.</param>
/// <param name="Players">The club's players with an active contract (`SQ-6`).</param>
public sealed record ClubTrainingRoster(
    Guid ClubId,
    int GameYear,
    TrainingFocus TeamFocus,
    TrainingIntensity Intensity,
    IReadOnlyList<ProgressablePlayer> Players);

/// <summary>
/// Persistence for the squad module's training plans, individual focuses, and the daily progression run
/// (`TRN-1`, `TRN-2`, `TRN-9`).
/// </summary>
/// <remarks>
/// A staging port like the rest of the module's repositories: it adds and mutates rows in the current unit
/// of work and never saves, so the progression run commits the whole world with one <c>SaveChanges</c>. The
/// roster load is here rather than on the read port because it returns tracked aggregates for a command to
/// write, which is exactly what the read port's projections must never be (`MOD-3`).
/// </remarks>
public interface ITrainingRepository
{
    /// <summary>Loads the club's training plan, or null when the club has none.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TrainingPlan?> FindPlanAsync(Guid clubId, CancellationToken cancellationToken);

    /// <summary>Stages a new training plan.</summary>
    /// <param name="plan">The plan.</param>
    void AddTrainingPlan(TrainingPlan plan);

    /// <summary>Loads one player's individual focus, or null when the player has none.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PlayerTrainingFocus?> FindFocusAsync(Guid playerId, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the club a player is contracted to, or null when they hold no active contract.
    /// </summary>
    /// <remarks>
    /// The individual-focus command authorizes against the player's own club, exactly as the player read
    /// does, so this resolves the club the request must be checked against (`SQ-6`).
    /// </remarks>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Guid?> FindPlayerClubAsync(Guid playerId, CancellationToken cancellationToken);

    /// <summary>Stages a new individual focus.</summary>
    /// <param name="focus">The focus.</param>
    void AddPlayerFocus(PlayerTrainingFocus focus);

    /// <summary>Removes an individual focus, returning the player to the team plan alone (`TRN-2`).</summary>
    /// <param name="focus">The focus to remove.</param>
    void RemovePlayerFocus(PlayerTrainingFocus focus);

    /// <summary>
    /// Loads every club's training plan and its contracted players, for the daily progression run.
    /// </summary>
    /// <remarks>
    /// The whole world is loaded because training is a world-scoped daily fact, not a club-scoped command:
    /// AI clubs and human clubs progress by the same rules, with no privileged path (`INS-12`, `TRN-9`).
    /// Clubs with no plan still appear, carrying the implicit default focus.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ClubTrainingRoster>> LoadRostersAsync(CancellationToken cancellationToken);
}
