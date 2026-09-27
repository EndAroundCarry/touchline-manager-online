using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Abstractions.Squad;

/// <summary>One player as the AI evaluation loads them, with everything the policy weighs (`INS-12`).</summary>
/// <param name="PlayerId">The player.</param>
/// <param name="PrimaryPosition">The position the player is most at home in.</param>
/// <param name="SecondaryPositions">The further positions the player covers.</param>
/// <param name="Attributes">The twenty-eight attributes, in canonical order.</param>
/// <param name="ConditionBp">Condition in basis points.</param>
/// <param name="IsAvailable">Whether the player has no open injury or suspension (`TRN-12`, `DIS-5`).</param>
public sealed record AiClubPlayerRow(
    Guid PlayerId,
    PlayerPosition PrimaryPosition,
    IReadOnlyList<PlayerPosition> SecondaryPositions,
    IReadOnlyList<int> Attributes,
    int ConditionBp,
    bool IsAvailable);

/// <summary>One club the AI manages, its squad, and which of its plans already exist (`INS-12`).</summary>
/// <remarks>
/// "Managed by the AI" is the absence of an open tenure, not a column on the club (`WORLD-7`): the load
/// returns the clubs no manager currently holds. The two plan flags let the use case skip a club without a
/// second read per club, and a club a human has already set up is left exactly as it is (`OCC-5`).
/// </remarks>
/// <param name="ClubId">The club.</param>
/// <param name="HasDefaultTacticalPlan">Whether the club already has a default tactical plan (`INS-11`).</param>
/// <param name="HasTrainingPlan">Whether the club already has a training plan (`TRN-1`).</param>
/// <param name="Players">Every player it may pick, available and unavailable alike.</param>
public sealed record AiClubRecord(
    Guid ClubId,
    bool HasDefaultTacticalPlan,
    bool HasTrainingPlan,
    IReadOnlyList<AiClubPlayerRow> Players);

/// <summary>
/// The AI evaluation's read port: every club no human holds, with the facts the policy decides from
/// (`INS-12`, master plan §7.2).
/// </summary>
/// <remarks>
/// A read-only projection, separate from the staging ports that write the plans it produces
/// (<see cref="ITacticsRepository"/>, <see cref="ITrainingRepository"/>), for the same reason the module's
/// other reads are: a command can change without widening what a query reaches (`MOD-3`). It reads across
/// the world and squad schemas because "who is AI-controlled" is a world fact and the squad is a squad
/// fact, and the two meet in this one question.
/// </remarks>
public interface IAiClubRepository
{
    /// <summary>
    /// Loads every AI-controlled club with its squad, ordered by club identity so a run is reproducible.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AiClubRecord>> LoadAiClubsAsync(CancellationToken cancellationToken);
}
