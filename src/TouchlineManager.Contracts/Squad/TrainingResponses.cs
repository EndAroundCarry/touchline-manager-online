namespace TouchlineManager.Contracts.Squad;

/// <summary>
/// The club's training plan and the squad it applies to (master plan §10.4, §11.1; `TRN-1`, `TRN-2`).
/// </summary>
/// <remarks>
/// One response for the whole training screen: the plan in force, the options a client may pick from so
/// it does not reproduce the enumerations, and every player with the individual focus that steers their
/// development. When a club has not set a plan yet, <see cref="IsConfigured"/> is false and the values are
/// the implicit defaults the progression job would use.
/// </remarks>
/// <param name="ClubId">The club the plan belongs to.</param>
/// <param name="ClubName">The generated club name.</param>
/// <param name="ClubShortName">The club abbreviation.</param>
/// <param name="CountryCode">The club's country code.</param>
/// <param name="SeasonNumber">The season the plan is read against.</param>
/// <param name="TeamFocus">The team focus code in force (`TRN-1`).</param>
/// <param name="Intensity">The intensity code in force.</param>
/// <param name="EffectiveDate">The date the plan took effect from.</param>
/// <param name="Version">The plan version, returned as the strong entity tag; zero when no plan exists.</param>
/// <param name="IsConfigured">Whether the club has set a plan, rather than falling back to the default.</param>
/// <param name="TeamFocusOptions">Every team focus code a manager may choose (`TRN-1`).</param>
/// <param name="IntensityOptions">Every intensity code a manager may choose.</param>
/// <param name="FocusFamilyOptions">Every attribute family code a player may focus on (`TRN-2`).</param>
/// <param name="Players">The squad, with each player's individual focus when one is set.</param>
/// <param name="ServerTime">The instant the response was produced.</param>
public sealed record TrainingResponse(
    Guid ClubId,
    string ClubName,
    string ClubShortName,
    string CountryCode,
    int SeasonNumber,
    string TeamFocus,
    string Intensity,
    DateOnly EffectiveDate,
    long Version,
    bool IsConfigured,
    IReadOnlyList<string> TeamFocusOptions,
    IReadOnlyList<string> IntensityOptions,
    IReadOnlyList<string> FocusFamilyOptions,
    IReadOnlyList<TrainingPlayerResponse> Players,
    DateTimeOffset ServerTime);

/// <summary>One player as the training screen shows them.</summary>
/// <param name="Id">The player identity.</param>
/// <param name="FullName">The generated full name.</param>
/// <param name="ShortName">The abbreviation.</param>
/// <param name="PrimaryPosition">The stable code of the player's position.</param>
/// <param name="PositionFamily">The stable code of the family that position belongs to.</param>
/// <param name="Age">The player's age in the current game year.</param>
/// <param name="State">Condition, fatigue, morale, and sharpness (`TRN-5`…`TRN-8`).</param>
/// <param name="FocusFamily">The player's individual focus code, or null when they train with the team (`TRN-2`).</param>
/// <param name="FocusVersion">The focus's version, or null when there is none.</param>
public sealed record TrainingPlayerResponse(
    Guid Id,
    string FullName,
    string ShortName,
    string PrimaryPosition,
    string PositionFamily,
    int Age,
    PlayerStateResponse State,
    string? FocusFamily,
    long? FocusVersion);

/// <summary>The result of setting or clearing one player's individual training focus (`TRN-2`).</summary>
/// <param name="PlayerId">The player the focus belongs to.</param>
/// <param name="FocusFamily">The focus code now set, or null when the focus was cleared.</param>
/// <param name="Version">The focus's version; zero when the focus was cleared.</param>
/// <param name="ServerTime">The instant the response was produced.</param>
public sealed record PlayerTrainingFocusResponse(
    Guid PlayerId,
    string? FocusFamily,
    long Version,
    DateTimeOffset ServerTime);
