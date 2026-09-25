using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>
/// Maps training read snapshots to their transport projections.
/// </summary>
/// <remarks>
/// The same rules the squad mapper follows: state crosses as a user-facing value (`TRN-8`), enumerations
/// cross as their stable codes, and the option lists the client renders are produced here from the enums
/// rather than duplicated in each client.
/// </remarks>
public static class TrainingMapping
{
    /// <summary>The focus a club trains at when it has not set a plan, which is what the job assumes.</summary>
    public const TrainingFocus DefaultTeamFocus = TrainingFocus.Balanced;

    /// <summary>The intensity a club trains at when it has not set a plan.</summary>
    public const TrainingIntensity DefaultIntensity = TrainingIntensity.Normal;

    /// <summary>Projects a club's training plan and squad for the training screen.</summary>
    /// <param name="snapshot">The stored training state.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static TrainingResponse ToResponse(this TrainingSnapshot snapshot, DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var plan = snapshot.Plan;

        return Build(
            snapshot,
            plan?.TeamFocus ?? DefaultTeamFocus,
            plan?.Intensity ?? DefaultIntensity,
            plan?.EffectiveDate ?? DateOnly.FromDateTime(serverTime.UtcDateTime),
            plan?.Version ?? 0,
            plan is not null,
            serverTime);
    }

    /// <summary>Projects the response around a plan that was just written.</summary>
    /// <param name="snapshot">The stored squad and club identity.</param>
    /// <param name="plan">The plan in force after the write.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static TrainingResponse ToResponse(
        this TrainingSnapshot snapshot,
        TrainingPlan plan,
        DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(plan);

        return Build(
            snapshot,
            plan.TeamFocus,
            plan.Intensity,
            plan.EffectiveDate,
            plan.Version,
            isConfigured: true,
            serverTime);
    }

    /// <summary>Projects the outcome of setting or clearing one player's focus (`TRN-2`).</summary>
    /// <param name="focus">The focus now in force, or null when it was cleared.</param>
    /// <param name="playerId">The player the focus belongs to.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static PlayerTrainingFocusResponse ToResponse(
        this PlayerTrainingFocus? focus,
        Guid playerId,
        DateTimeOffset serverTime) =>
        new(
            playerId,
            focus?.FocusFamily.ToCode(),
            focus?.Version ?? 0,
            serverTime);

    private static TrainingResponse Build(
        TrainingSnapshot snapshot,
        TrainingFocus teamFocus,
        TrainingIntensity intensity,
        DateOnly effectiveDate,
        long version,
        bool isConfigured,
        DateTimeOffset serverTime) =>
        new(
            snapshot.ClubId,
            snapshot.ClubName,
            snapshot.ClubShortName,
            snapshot.CountryCode,
            snapshot.SeasonNumber,
            teamFocus.ToCode(),
            intensity.ToCode(),
            effectiveDate,
            version,
            isConfigured,
            [.. Enum.GetValues<TrainingFocus>().Select(focus => focus.ToCode())],
            [.. Enum.GetValues<TrainingIntensity>().Select(value => value.ToCode())],
            [.. Enum.GetValues<AttributeFamily>().Select(family => family.ToCode())],
            [.. snapshot.Players.Select(player => player.ToResponse(snapshot.GameYear))],
            serverTime);

    private static TrainingPlayerResponse ToResponse(this TrainingPlayerRow player, int gameYear) =>
        new(
            player.Id,
            player.FullName,
            player.ShortName,
            player.PrimaryPosition.ToCode(),
            PlayerPositions.FamilyOf(player.PrimaryPosition).ToCode(),
            SquadMapping.AgeIn(player.BirthGameYear, gameYear),
            player.State.ToResponse(),
            player.FocusFamily?.ToCode(),
            player.FocusVersion);
}
