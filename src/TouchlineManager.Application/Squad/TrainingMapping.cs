using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>
/// Maps training read snapshots to their transport projections.
/// </summary>
/// <remarks>
/// The same rules the squad mapper follows: state crosses as a user-facing value (`TRN-8`), enumerations
/// cross as their stable codes, and the programme catalogue the client renders is produced here from the
/// domain catalogue rather than duplicated in each client. Aptitude and potential never cross (`TRN-9`).
/// </remarks>
public static class TrainingMapping
{
    /// <summary>The intensity a club trains at when it has not set a plan, which is what the job assumes.</summary>
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
            plan.Intensity,
            plan.EffectiveDate,
            plan.Version,
            isConfigured: true,
            serverTime);
    }

    /// <summary>Projects the outcome of setting or clearing one player's programme (`TRN-1`).</summary>
    /// <param name="focus">The override now in force, or null when it was cleared.</param>
    /// <param name="playerId">The player the programme belongs to.</param>
    /// <param name="primaryPosition">The player's position, which fixes the default programme.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static PlayerTrainingProgrammeResponse ToResponse(
        this PlayerTrainingFocus? focus,
        Guid playerId,
        PlayerPosition primaryPosition,
        DateTimeOffset serverTime)
    {
        var defaultProgramme = TrainingProgrammes.DefaultFor(primaryPosition);

        return new PlayerTrainingProgrammeResponse(
            playerId,
            (focus?.Programme ?? defaultProgramme).ToCode(),
            focus is null,
            defaultProgramme.ToCode(),
            focus?.Version ?? 0,
            serverTime);
    }

    /// <summary>Projects a player's training regime and recent history for the player page (`TRN-17`).</summary>
    /// <param name="snapshot">The stored regime and days.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static PlayerTrainingResponse ToResponse(this PlayerTrainingSnapshot snapshot, DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var programme = snapshot.Programme ?? TrainingProgrammes.DefaultFor(snapshot.PrimaryPosition);
        var definition = TrainingProgrammes.Of(programme);

        var days = snapshot.Days
            .Select(day => new PlayerTrainingDayResponse(
                day.Day,
                day.Programme.ToCode(),
                day.Intensity.ToCode(),
                Math.Round((day.DevelopmentMilli - day.DeclineMilli) / 1000m, 3),
                day.PointsGained,
                day.PointsLost,
                [.. day.AttributeChanges.Select(change => new PlayerTrainingAttributeChangeResponse(
                    AttributeNames.CodeOf(change.Attribute),
                    change.Delta))],
                [.. day.ProgressChanges.Select(change => new PlayerTrainingProgressResponse(
                    AttributeNames.CodeOf(change.Attribute),
                    SquadMapping.MicroToPoints(change.DeltaMicro)))]))
            .ToList();

        // One line per programme, in the order the player first trained it, so the table reads as a history.
        var summary = snapshot.Days
            .GroupBy(day => day.Programme)
            .Select(group => new PlayerTrainingSummaryResponse(
                group.Key.ToCode(),
                TrainingProgrammes.Of(group.Key).Label,
                group.Count(),
                group.Sum(day => day.PointsGained),
                group.Sum(day => day.PointsLost),
                group.Sum(day => day.PointsGained) - group.Sum(day => day.PointsLost)))
            .ToList();

        return new PlayerTrainingResponse(
            snapshot.PlayerId,
            new PlayerTrainingRegimeResponse(
                definition.Code,
                definition.Label,
                definition.Description,
                snapshot.Programme is null,
                snapshot.Intensity.ToCode(),
                definition.ToAttributeResponses()),
            days,
            summary,
            serverTime);
    }

    private static TrainingResponse Build(
        TrainingSnapshot snapshot,
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
            intensity.ToCode(),
            effectiveDate,
            version,
            isConfigured,
            [.. Enum.GetValues<TrainingIntensity>().Select(value => value.ToCode())],
            [.. TrainingProgrammes.All.Select(definition => new TrainingProgrammeResponse(
                definition.Code,
                definition.Label,
                definition.Description,
                definition.ToAttributeResponses()))],
            [.. snapshot.Players.Select(player => player.ToResponse(snapshot.GameYear))],
            serverTime);

    private static IReadOnlyList<TrainingProgrammeAttributeResponse> ToAttributeResponses(
        this TrainingProgrammeDefinition definition) =>
        [.. definition.Attributes.Select(entry => new TrainingProgrammeAttributeResponse(
            AttributeNames.CodeOf(entry.Attribute),
            AttributeNames.FamilyOf(entry.Attribute).ToCode(),
            entry.Weight))];

    private static TrainingPlayerResponse ToResponse(this TrainingPlayerRow player, int gameYear)
    {
        var defaultProgramme = TrainingProgrammes.DefaultFor(player.PrimaryPosition);

        return new TrainingPlayerResponse(
            player.Id,
            player.FullName,
            player.ShortName,
            player.PrimaryPosition.ToCode(),
            PlayerPositions.FamilyOf(player.PrimaryPosition).ToCode(),
            SquadMapping.AgeIn(player.BirthGameYear, gameYear),
            player.State.ToResponse(),
            player.Attributes.ToResponse(),
            player.State.AttributeProgress.ToProgressResponse(),
            (player.Programme ?? defaultProgramme).ToCode(),
            player.Programme is null,
            defaultProgramme.ToCode(),
            player.FocusVersion);
    }
}
