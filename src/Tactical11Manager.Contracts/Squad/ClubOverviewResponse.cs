namespace Tactical11Manager.Contracts.Squad;

/// <summary>
/// Any club's public page: who it is, where it plays, and the players it holds (master plan §11.1).
/// </summary>
/// <remarks>
/// The public half of what <see cref="SquadResponse"/> gives the club's own manager. The attributes are exact
/// and public (`SCT-1`), so a manager can study a rival; condition, morale, contracts and wages are the
/// owning club's and are not here. Bounded to <c>SQ-3</c>'s 25 players, so it is one unpaged response.
/// </remarks>
/// <param name="ClubId">The club.</param>
/// <param name="Name">The generated club name.</param>
/// <param name="ShortName">The abbreviation.</param>
/// <param name="City">The generated home city.</param>
/// <param name="Region">The generated region.</param>
/// <param name="CountryCode">The club's country code, which is also the players' nationality.</param>
/// <param name="FoundingGameYear">The game year the club was founded.</param>
/// <param name="PrimaryColour">The main kit colour as <c>#rrggbb</c>: the one chosen, or the generated one.</param>
/// <param name="SecondaryColour">The second kit colour as <c>#rrggbb</c>.</param>
/// <param name="CrestShape">The code of the crest shape.</param>
/// <param name="CrestPattern">The code of the stripes on the crest.</param>
/// <param name="DivisionId">The division the club plays in this season, or null when it has none.</param>
/// <param name="DivisionName">The division's generated name.</param>
/// <param name="TierNumber">The division's tier number.</param>
/// <param name="IsOwnClub">Whether the caller manages this club, so the screen can point to the private squad.</param>
/// <param name="ManagerName">
/// The display name of the manager who runs the club, which other signed-in managers may see (`COM-11`), or null
/// when the game's own manager runs it. It carries no manager identity: a conversation is started from the club.
/// </param>
/// <param name="SeasonNumber">The season the squad is read against.</param>
/// <param name="Players">The players, goalkeepers first.</param>
/// <param name="ServerTime">The instant the response was produced.</param>
public sealed record ClubOverviewResponse(
    Guid ClubId,
    string Name,
    string ShortName,
    string City,
    string Region,
    string CountryCode,
    int FoundingGameYear,
    string PrimaryColour,
    string SecondaryColour,
    string CrestShape,
    string CrestPattern,
    Guid? DivisionId,
    string? DivisionName,
    int? TierNumber,
    bool IsOwnClub,
    string? ManagerName,
    int SeasonNumber,
    IReadOnlyList<ClubPlayerResponse> Players,
    DateTimeOffset ServerTime);

/// <summary>One player as another club's squad lists them: who they are and how good they are, nothing private.</summary>
/// <param name="Id">The player identity.</param>
/// <param name="FullName">The generated full name.</param>
/// <param name="ShortName">The abbreviation shown in lineups.</param>
/// <param name="NationalityCode">The nationality country code.</param>
/// <param name="Age">The player's age in the current game year.</param>
/// <param name="PreferredFoot">The stable code of the foot the player favours.</param>
/// <param name="PrimaryPosition">The stable code of the player's position.</param>
/// <param name="SecondaryPositions">The stable codes of the other positions the player covers.</param>
/// <param name="Attributes">The displayed attributes, so the client can rate them for a position.</param>
/// <param name="Experience">Experience points, level, and the skill bonus the level gives in matches.</param>
public sealed record ClubPlayerResponse(
    Guid Id,
    string FullName,
    string ShortName,
    string NationalityCode,
    int Age,
    string PreferredFoot,
    string PrimaryPosition,
    IReadOnlyList<string> SecondaryPositions,
    PlayerAttributesResponse Attributes,
    PlayerExperienceResponse Experience);
