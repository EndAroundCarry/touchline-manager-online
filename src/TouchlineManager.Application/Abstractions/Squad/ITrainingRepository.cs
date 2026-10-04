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
/// <param name="Programme">
/// The programme the manager chose for the player, or null to train the position default (`TRN-1`).
/// </param>
public sealed record ProgressablePlayer(
    Player Player,
    PlayerAttributes Attributes,
    PlayerState State,
    int Potential,
    TrainingProgramme? Programme);

/// <summary>A player whose training a manager may set, with the club that decides it.</summary>
/// <param name="ClubId">The club the player holds an active contract with.</param>
/// <param name="PrimaryPosition">The player's position, which fixes their default programme.</param>
public sealed record TrainablePlayer(Guid ClubId, PlayerPosition PrimaryPosition);

/// <summary>One club's training plan and the squad it applies to.</summary>
/// <param name="ClubId">The club.</param>
/// <param name="GameYear">The season's game year, which fixes each player's age (`TIME-3`).</param>
/// <param name="Intensity">The club's intensity, or the implicit default when it has no plan.</param>
/// <param name="Players">The club's players with an active contract (`SQ-6`).</param>
public sealed record ClubTrainingRoster(
    Guid ClubId,
    int GameYear,
    TrainingIntensity Intensity,
    IReadOnlyList<ProgressablePlayer> Players);

/// <summary>
/// Persistence for the squad module's training plans, programme overrides, and the daily progression run
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

    /// <summary>Loads one player's programme override, or null when the player has none.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PlayerTrainingFocus?> FindFocusAsync(Guid playerId, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a player and the club they are contracted to, or null when they hold no active contract.
    /// </summary>
    /// <remarks>
    /// The programme command authorizes against the player's own club, exactly as the player read
    /// does, so this resolves the club the request must be checked against (`SQ-6`). The position comes
    /// with it because it fixes the default programme the response reports.
    /// </remarks>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TrainablePlayer?> FindTrainablePlayerAsync(Guid playerId, CancellationToken cancellationToken);

    /// <summary>Stages a new programme override.</summary>
    /// <param name="focus">The focus.</param>
    void AddPlayerFocus(PlayerTrainingFocus focus);

    /// <summary>Removes a programme override, returning the player to their position default (`TRN-1`).</summary>
    /// <param name="focus">The focus to remove.</param>
    void RemovePlayerFocus(PlayerTrainingFocus focus);

    /// <summary>Stages one player's training day in the history (`TRN-17`).</summary>
    /// <param name="day">The day to record.</param>
    void AddTrainingDay(PlayerTrainingDay day);

    /// <summary>
    /// Loads every club's training plan and its contracted players, for the daily progression run.
    /// </summary>
    /// <remarks>
    /// The whole world is loaded because training is a world-scoped daily fact, not a club-scoped command:
    /// AI clubs and human clubs progress by the same rules, with no privileged path (`INS-12`, `TRN-9`).
    /// Clubs with no plan still appear, carrying the implicit default intensity.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ClubTrainingRoster>> LoadRostersAsync(CancellationToken cancellationToken);
}
