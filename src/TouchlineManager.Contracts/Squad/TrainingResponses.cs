namespace TouchlineManager.Contracts.Squad;

/// <summary>
/// The club's training plan, the programme catalogue, and the squad it applies to (master plan §10.4, §11.1;
/// `TRN-1`, `TRN-2`).
/// </summary>
/// <remarks>
/// One response for the whole training screen: the intensity in force, the programme catalogue so a client
/// does not reproduce which attributes each programme trains, and every player with their effective programme
/// and full attribute set. When a club has not set a plan yet, <see cref="IsConfigured"/> is false and the
/// values are the implicit defaults the progression job would use. The hidden potential and each player's
/// training aptitude are never part of it (`data-classification.md` §2.1).
/// </remarks>
/// <param name="ClubId">The club the plan belongs to.</param>
/// <param name="ClubName">The generated club name.</param>
/// <param name="ClubShortName">The club abbreviation.</param>
/// <param name="CountryCode">The club's country code.</param>
/// <param name="SeasonNumber">The season the plan is read against.</param>
/// <param name="Intensity">The intensity code in force.</param>
/// <param name="EffectiveDate">The date the plan took effect from.</param>
/// <param name="Version">The plan version, returned as the strong entity tag; zero when no plan exists.</param>
/// <param name="IsConfigured">Whether the club has set a plan, rather than falling back to the default.</param>
/// <param name="IntensityOptions">Every intensity code a manager may choose.</param>
/// <param name="Programmes">The programme catalogue: what each programme trains and how strongly (`TRN-1`).</param>
/// <param name="Players">The squad, with each player's effective programme and attributes.</param>
/// <param name="ServerTime">The instant the response was produced.</param>
public sealed record TrainingResponse(
    Guid ClubId,
    string ClubName,
    string ClubShortName,
    string CountryCode,
    int SeasonNumber,
    string Intensity,
    DateOnly EffectiveDate,
    long Version,
    bool IsConfigured,
    IReadOnlyList<string> IntensityOptions,
    IReadOnlyList<TrainingProgrammeResponse> Programmes,
    IReadOnlyList<TrainingPlayerResponse> Players,
    DateTimeOffset ServerTime);

/// <summary>One training programme: a weighted set of attributes (`TRN-1`).</summary>
/// <param name="Code">The stable programme code, e.g. <c>forward</c>.</param>
/// <param name="Label">The display name.</param>
/// <param name="Description">One sentence saying who the programme is for.</param>
/// <param name="Attributes">The attributes the programme trains, heaviest first; empty for recovery.</param>
public sealed record TrainingProgrammeResponse(
    string Code,
    string Label,
    string Description,
    IReadOnlyList<TrainingProgrammeAttributeResponse> Attributes);

/// <summary>One attribute a programme trains, and how much it matters to it.</summary>
/// <param name="Name">The attribute code, which is its property name in the attribute grid, e.g. <c>firstTouch</c>.</param>
/// <param name="Family">The stable code of the family the attribute belongs to.</param>
/// <param name="Weight">3 for a core attribute, 2 for an important one, 1 for a supporting one.</param>
public sealed record TrainingProgrammeAttributeResponse(string Name, string Family, int Weight);

/// <summary>One player as the training screen shows them.</summary>
/// <param name="Id">The player identity.</param>
/// <param name="FullName">The generated full name.</param>
/// <param name="ShortName">The abbreviation.</param>
/// <param name="PrimaryPosition">The stable code of the player's position.</param>
/// <param name="PositionFamily">The stable code of the family that position belongs to.</param>
/// <param name="Age">The player's age in the current game year.</param>
/// <param name="State">Condition, fatigue, morale, and sharpness (`TRN-5`…`TRN-8`).</param>
/// <param name="Attributes">The player's twenty-eight attributes, grouped by family (`TRN-4`).</param>
/// <param name="Programme">The code of the programme the player trains: the override, else the default.</param>
/// <param name="IsDefaultProgramme">Whether <paramref name="Programme"/> is the position default rather than a choice.</param>
/// <param name="DefaultProgramme">The code of the programme matching the player's position (`TRN-1`).</param>
/// <param name="FocusVersion">The override's version, or null when the player has none.</param>
public sealed record TrainingPlayerResponse(
    Guid Id,
    string FullName,
    string ShortName,
    string PrimaryPosition,
    string PositionFamily,
    int Age,
    PlayerStateResponse State,
    PlayerAttributesResponse Attributes,
    string Programme,
    bool IsDefaultProgramme,
    string DefaultProgramme,
    long? FocusVersion);

/// <summary>The result of setting or clearing one player's training programme (`TRN-1`, `TRN-2`).</summary>
/// <param name="PlayerId">The player the programme belongs to.</param>
/// <param name="Programme">The code of the programme the player now trains: the override, else the default.</param>
/// <param name="IsDefaultProgramme">Whether <paramref name="Programme"/> is the position default.</param>
/// <param name="DefaultProgramme">The code of the programme matching the player's position.</param>
/// <param name="Version">The override's version; zero when the override was cleared.</param>
/// <param name="ServerTime">The instant the response was produced.</param>
public sealed record PlayerTrainingProgrammeResponse(
    Guid PlayerId,
    string Programme,
    bool IsDefaultProgramme,
    string DefaultProgramme,
    long Version,
    DateTimeOffset ServerTime);

/// <summary>
/// A player's training regime and how it has changed them over the progression days so far (`TRN-17`).
/// </summary>
/// <remarks>
/// Read for the player page's Training tab. The days are the most recent progression days in the order they
/// happened, each recording the regime in force that day, so a chart can draw one series per regime. Nothing
/// hidden is part of it: growth is the net change the player's visible attributes actually made.
/// </remarks>
/// <param name="PlayerId">The player.</param>
/// <param name="Regime">The programme and intensity the player trains now.</param>
/// <param name="Days">The recorded progression days, oldest first.</param>
/// <param name="Summary">One line per programme that appears in <paramref name="Days"/>.</param>
/// <param name="ServerTime">The instant the response was produced.</param>
public sealed record PlayerTrainingResponse(
    Guid PlayerId,
    PlayerTrainingRegimeResponse Regime,
    IReadOnlyList<PlayerTrainingDayResponse> Days,
    IReadOnlyList<PlayerTrainingSummaryResponse> Summary,
    DateTimeOffset ServerTime);

/// <summary>The training a player is on now.</summary>
/// <param name="Programme">The programme code in force.</param>
/// <param name="Label">The programme's display name.</param>
/// <param name="Description">One sentence saying who the programme is for.</param>
/// <param name="IsDefaultProgramme">Whether the programme is the position default rather than a choice.</param>
/// <param name="Intensity">The club's intensity code.</param>
/// <param name="Attributes">The attributes the programme trains, heaviest first; empty for recovery.</param>
public sealed record PlayerTrainingRegimeResponse(
    string Programme,
    string Label,
    string Description,
    bool IsDefaultProgramme,
    string Intensity,
    IReadOnlyList<TrainingProgrammeAttributeResponse> Attributes);

/// <summary>One progression day in a player's training history.</summary>
/// <param name="Day">The progression day.</param>
/// <param name="Programme">The programme the player trained that day.</param>
/// <param name="Intensity">The club intensity that day.</param>
/// <param name="Growth">The net points accrued that day (development less decline), to three decimals.</param>
/// <param name="PointsGained">The whole attribute points gained that day.</param>
/// <param name="PointsLost">The whole attribute points lost that day.</param>
/// <param name="AttributeChanges">Which attributes moved, and by how much; empty on most days.</param>
public sealed record PlayerTrainingDayResponse(
    DateOnly Day,
    string Programme,
    string Intensity,
    decimal Growth,
    int PointsGained,
    int PointsLost,
    IReadOnlyList<PlayerTrainingAttributeChangeResponse> AttributeChanges);

/// <summary>One attribute's change on a progression day.</summary>
/// <param name="Attribute">The attribute code, e.g. <c>finishing</c>.</param>
/// <param name="Delta">The whole-point change: positive for growth, negative for decline.</param>
public sealed record PlayerTrainingAttributeChangeResponse(string Attribute, int Delta);

/// <summary>What one programme did for a player across the days returned.</summary>
/// <param name="Programme">The programme code.</param>
/// <param name="Label">The programme's display name, so a client can name a regime it has no catalogue for.</param>
/// <param name="Days">How many of the returned days the player trained it.</param>
/// <param name="PointsGained">The whole points gained across those days.</param>
/// <param name="PointsLost">The whole points lost across those days.</param>
/// <param name="Net">Points gained less points lost.</param>
public sealed record PlayerTrainingSummaryResponse(
    string Programme,
    string Label,
    int Days,
    int PointsGained,
    int PointsLost,
    int Net);
