namespace TouchlineManager.Contracts.Squad;

/// <summary>The squad a manager has inherited, shaped for the squad screen (master plan §10.3, §11.1).</summary>
/// <remarks>
/// Bounded to <c>SQ-3</c>'s 25 players, which is what makes a single unpaged response the right shape.
/// Rows deliberately do not carry the twenty-eight-attribute block: the squad table is about selection
/// readiness — availability, condition, contract — and the attribute grid belongs to the player profile.
/// They carry only the four family means, so the stronger player can be spotted at a glance.
/// </remarks>
/// <param name="ClubId">The club the squad belongs to.</param>
/// <param name="ClubName">The generated club name.</param>
/// <param name="ClubShortName">The abbreviation.</param>
/// <param name="CountryCode">The club's country code, which is also the players' nationality.</param>
/// <param name="SeasonNumber">The season the registrations are measured against.</param>
/// <param name="Summary">The squad's size and legality, so the screen can warn without recomputing (`SQ-2`, `SQ-9`).</param>
/// <param name="Players">The players, goalkeepers first.</param>
/// <param name="ServerTime">The instant the response was produced.</param>
public sealed record SquadResponse(
    Guid ClubId,
    string ClubName,
    string ClubShortName,
    string CountryCode,
    int SeasonNumber,
    SquadSummaryResponse Summary,
    IReadOnlyList<SquadPlayerResponse> Players,
    DateTimeOffset ServerTime);

/// <summary>The counts and legality flags the squad screen shows (`SQ-2`, `SQ-3`, `SQ-9`).</summary>
/// <param name="PlayerCount">How many players the club holds.</param>
/// <param name="Goalkeepers">How many of them are goalkeepers.</param>
/// <param name="MeetsMinimum">Whether the club reaches the minimum registered squad (`SQ-2`).</param>
/// <param name="HasMinimumGoalkeepers">Whether the club holds enough goalkeepers (`SQ-2`).</param>
/// <param name="WeeklyWageTotalMinor">The club's committed weekly wages, in minor units (`CON-2`).</param>
public sealed record SquadSummaryResponse(
    int PlayerCount,
    int Goalkeepers,
    bool MeetsMinimum,
    bool HasMinimumGoalkeepers,
    long WeeklyWageTotalMinor);

/// <summary>One player as the squad table shows them.</summary>
/// <param name="Id">The player identity.</param>
/// <param name="FullName">The generated full name.</param>
/// <param name="ShortName">The abbreviation shown in lineups.</param>
/// <param name="NationalityCode">The nationality country code.</param>
/// <param name="Age">The player's age in the current game year.</param>
/// <param name="PreferredFoot">The stable code of the foot the player favours.</param>
/// <param name="PrimaryPosition">The stable code of the player's position.</param>
/// <param name="SecondaryPositions">The stable codes of the other positions the player covers.</param>
/// <param name="State">Condition, fatigue, morale, and sharpness.</param>
/// <param name="Contract">The active contract, or null if the player has none (`SQ-6`).</param>
/// <param name="Availability">Every open injury and suspension, empty when the player is available.</param>
/// <param name="AttributeAverages">The mean of each attribute family, for scanning the squad at a glance.</param>
public sealed record SquadPlayerResponse(
    Guid Id,
    string FullName,
    string ShortName,
    string NationalityCode,
    int Age,
    string PreferredFoot,
    string PrimaryPosition,
    IReadOnlyList<string> SecondaryPositions,
    PlayerStateResponse State,
    PlayerContractSummaryResponse? Contract,
    IReadOnlyList<PlayerAvailabilityResponse> Availability,
    AttributeAveragesResponse AttributeAverages);

/// <summary>The mean of each attribute family, on the 1–20 attribute scale, to one decimal.</summary>
/// <remarks>
/// Four family means rather than an overall: no single number is authoritative for a player.
/// </remarks>
/// <param name="Goalkeeping">Mean of the four goalkeeping attributes.</param>
/// <param name="Technical">Mean of the ten technical attributes.</param>
/// <param name="Mental">Mean of the eight mental attributes.</param>
/// <param name="Physical">Mean of the six physical attributes.</param>
public sealed record AttributeAveragesResponse(
    decimal Goalkeeping,
    decimal Technical,
    decimal Mental,
    decimal Physical);

/// <summary>
/// A player's condition, fatigue, morale, and match sharpness, as user-facing values (`TRN-8`).
/// </summary>
/// <remarks>
/// Zero to one hundred, not basis points. The database is authoritative in basis points and the API
/// converts, so the storage unit never reaches a client and a future change to the scale is one
/// conversion rather than every consumer.
/// </remarks>
/// <param name="Condition">Short-term freshness, 0–100.</param>
/// <param name="Fatigue">Accumulated load, 0–100. Higher is worse.</param>
/// <param name="Morale">Bounded sentiment, 0–100.</param>
/// <param name="MatchSharpness">Match sharpness, 0–100.</param>
public sealed record PlayerStateResponse(
    int Condition,
    int Fatigue,
    int Morale,
    int MatchSharpness);

/// <summary>A contract as a squad list or contract list shows it (`CON-1`, `CON-8`).</summary>
/// <param name="Id">The contract identity.</param>
/// <param name="StartSeasonNumber">The first season the contract covers.</param>
/// <param name="EndSeasonNumber">The last season the contract covers.</param>
/// <param name="SeasonsRemaining">How many seasons remain, counted from the current one. Zero means it expires at its end season.</param>
/// <param name="WeeklyWageMinor">The weekly wage in minor units (`CON-2`).</param>
/// <param name="SquadStatus">The stable code of the player's standing in the squad.</param>
/// <param name="Status">The stable code of the contract's lifecycle state.</param>
public sealed record PlayerContractSummaryResponse(
    Guid Id,
    int StartSeasonNumber,
    int EndSeasonNumber,
    int SeasonsRemaining,
    long WeeklyWageMinor,
    string SquadStatus,
    string Status);

/// <summary>One contract in the club's contract list, with the player it binds.</summary>
/// <param name="Id">The contract identity.</param>
/// <param name="PlayerId">The contracted player.</param>
/// <param name="PlayerName">The player's full name.</param>
/// <param name="PlayerShortName">The player's abbreviation.</param>
/// <param name="PrimaryPosition">The stable code of the player's position.</param>
/// <param name="Age">The player's age in the current game year.</param>
/// <param name="StartSeasonNumber">The first season the contract covers.</param>
/// <param name="EndSeasonNumber">The last season the contract covers.</param>
/// <param name="SeasonsRemaining">How many seasons remain, counted from the current one.</param>
/// <param name="WeeklyWageMinor">The weekly wage in minor units.</param>
/// <param name="SquadStatus">The stable code of the player's standing in the squad.</param>
/// <param name="Status">The stable code of the contract's lifecycle state.</param>
public sealed record PlayerContractResponse(
    Guid Id,
    Guid PlayerId,
    string PlayerName,
    string PlayerShortName,
    string PrimaryPosition,
    int Age,
    int StartSeasonNumber,
    int EndSeasonNumber,
    int SeasonsRemaining,
    long WeeklyWageMinor,
    string SquadStatus,
    string Status);

/// <summary>An open injury or suspension, measured in the fixtures it costs (`TRN-12`, `DIS-5`).</summary>
/// <param name="Id">The record identity.</param>
/// <param name="Type">The stable code of the unavailability type.</param>
/// <param name="Severity">The stable code of the injury severity.</param>
/// <param name="RemainingFixtures">How many eligible fixtures the player still misses.</param>
/// <param name="StartedAt">When the record started.</param>
public sealed record PlayerAvailabilityResponse(
    Guid Id,
    string Type,
    string Severity,
    int RemainingFixtures,
    DateTimeOffset StartedAt);

/// <summary>A player's registration, which is what makes them selectable (`SQ-6`, `SQ-7`).</summary>
/// <param name="Id">The registration identity.</param>
/// <param name="ClubId">The club the player is registered to.</param>
/// <param name="Status">The stable code of the registration's lifecycle state.</param>
/// <param name="EffectiveFixtureBoundaryRound">The round the registration takes effect from; zero means before the first fixture.</param>
public sealed record PlayerRegistrationResponse(
    Guid Id,
    Guid ClubId,
    string Status,
    int EffectiveFixtureBoundaryRound);

/// <summary>
/// A player's profile: the full attribute grid plus the state, contract, and registration around it
/// (master plan §11.1).
/// </summary>
/// <param name="Id">The player identity.</param>
/// <param name="ClubId">The club the player is registered to.</param>
/// <param name="FullName">The generated full name.</param>
/// <param name="ShortName">The abbreviation shown in lineups.</param>
/// <param name="NationalityCode">The nationality country code.</param>
/// <param name="Age">The player's age in the current game year.</param>
/// <param name="BirthGameYear">The game year the player was born in (`TIME-3`).</param>
/// <param name="PreferredFoot">The stable code of the foot the player favours.</param>
/// <param name="HeightCm">The player's height in centimetres.</param>
/// <param name="WeightKg">The player's weight in kilograms.</param>
/// <param name="PrimaryPosition">The stable code of the player's position.</param>
/// <param name="SecondaryPositions">The stable codes of the other positions the player covers.</param>
/// <param name="Status">The stable code of the player's lifecycle state.</param>
/// <param name="Attributes">The displayed attribute grid, grouped by family.</param>
/// <param name="State">Condition, fatigue, morale, and sharpness.</param>
/// <param name="Contract">The active contract, or null if the player has none.</param>
/// <param name="Registration">The active registration, or null if the player has none.</param>
/// <param name="Availability">Every open injury and suspension, if any.</param>
/// <param name="SeasonStats">
/// The player's own season statistics for the club they play for, or null before they have appeared
/// (`STA-2`). Read from the publication's projection, so it agrees with the division leaderboard.
/// </param>
/// <param name="CareerStats">
/// The player's whole career across seasons, or null before they have ever appeared (`STA-2`). Derived by
/// aggregating the season statistics, which survive rollover.
/// </param>
/// <param name="ServerTime">The instant the response was produced.</param>
public sealed record PlayerResponse(
    Guid Id,
    Guid ClubId,
    string FullName,
    string ShortName,
    string NationalityCode,
    int Age,
    int BirthGameYear,
    string PreferredFoot,
    int HeightCm,
    int WeightKg,
    string PrimaryPosition,
    IReadOnlyList<string> SecondaryPositions,
    string Status,
    PlayerAttributesResponse Attributes,
    PlayerStateResponse State,
    PlayerContractSummaryResponse? Contract,
    PlayerRegistrationResponse? Registration,
    IReadOnlyList<PlayerAvailabilityResponse> Availability,
    PlayerSeasonStatsResponse? SeasonStats,
    PlayerCareerStatsResponse? CareerStats,
    DateTimeOffset ServerTime);

/// <summary>
/// A player's own season statistics for the season in progress (`STA-2`, master plan §11.1).
/// </summary>
/// <remarks>
/// The player's line of the same projection the division leaderboard reads, narrowed to one player: goals,
/// assists, minutes, cards, and the average match rating. A player who has not taken the pitch has no line
/// at all, so the profile shows a message rather than a row of zeros.
/// </remarks>
/// <param name="Appearances">Matches the player took the pitch in.</param>
/// <param name="Starts">Matches the player started.</param>
/// <param name="MinutesPlayed">Total minutes played.</param>
/// <param name="Goals">Goals scored.</param>
/// <param name="Assists">Goals set up.</param>
/// <param name="Shots">Shots taken.</param>
/// <param name="ShotsOnTarget">Shots on target.</param>
/// <param name="Saves">Saves made.</param>
/// <param name="PassesAttempted">Passes attempted in open play.</param>
/// <param name="PassesCompleted">Passes that found a teammate.</param>
/// <param name="DribblesAttempted">Take-ons attempted.</param>
/// <param name="DribblesCompleted">Take-ons won.</param>
/// <param name="YellowCards">Bookings accumulated (`DIS-2`).</param>
/// <param name="RedCards">Sendings-off accumulated (`DIS-4`).</param>
/// <param name="AverageRating">
/// The average match rating on a 0.0–10.0 scale, or null before the player has been rated. Converted from
/// the stored basis points in one place (`TRN-8`), so no storage unit reaches a client.
/// </param>
public sealed record PlayerSeasonStatsResponse(
    int Appearances,
    int Starts,
    int MinutesPlayed,
    int Goals,
    int Assists,
    int Shots,
    int ShotsOnTarget,
    int Saves,
    int PassesAttempted,
    int PassesCompleted,
    int DribblesAttempted,
    int DribblesCompleted,
    int YellowCards,
    int RedCards,
    decimal? AverageRating);

/// <summary>One season of a player's career (`STA-2`, master plan §11.1).</summary>
/// <param name="SeasonNumber">The season's ordinal in the world.</param>
/// <param name="SeasonLabel">The season's display label.</param>
/// <param name="ClubId">The club the player appeared for that season.</param>
/// <param name="ClubName">The club's generated name.</param>
/// <param name="Stats">The player's line for that season.</param>
public sealed record PlayerCareerSeasonResponse(
    int SeasonNumber,
    string SeasonLabel,
    Guid ClubId,
    string ClubName,
    PlayerSeasonStatsResponse Stats);

/// <summary>
/// A player's whole career: the totals across every season and each season's own line (`STA-2`).
/// </summary>
/// <remarks>
/// A read-only aggregation over the season statistics, whose rows survive rollover, so it is derived rather
/// than stored. Null until the player has appeared at least once.
/// </remarks>
/// <param name="Totals">The summed totals across every season the player has appeared in.</param>
/// <param name="SeasonsPlayed">How many distinct seasons the player has appeared in.</param>
/// <param name="Seasons">Each season's line, most recent first.</param>
public sealed record PlayerCareerStatsResponse(
    PlayerSeasonStatsResponse Totals,
    int SeasonsPlayed,
    IReadOnlyList<PlayerCareerSeasonResponse> Seasons);

/// <summary>
/// The player's twenty-eight displayed attributes, grouped into their four families (`TRN-4`).
/// </summary>
/// <remarks>
/// Grouped rather than flat because that is how the profile renders them and how individual training
/// focus selects them (`TRN-2`). There is no "overall": the glossary is explicit that no single number
/// is authoritative, and publishing one would invite balancing against it.
/// </remarks>
/// <param name="Technical">The technical attributes.</param>
/// <param name="Mental">The mental attributes.</param>
/// <param name="Physical">The physical attributes.</param>
/// <param name="Goalkeeping">The goalkeeping attributes.</param>
public sealed record PlayerAttributesResponse(
    TechnicalAttributesResponse Technical,
    MentalAttributesResponse Mental,
    PhysicalAttributesResponse Physical,
    GoalkeepingAttributesResponse Goalkeeping);

/// <summary>The technical attributes, each 1–20 (`TRN-4`).</summary>
/// <param name="Finishing">Finishing.</param>
/// <param name="Passing">Passing.</param>
/// <param name="Crossing">Crossing.</param>
/// <param name="Dribbling">Dribbling.</param>
/// <param name="FirstTouch">First touch.</param>
/// <param name="Tackling">Tackling.</param>
/// <param name="Marking">Marking.</param>
/// <param name="Heading">Heading.</param>
/// <param name="Technique">Technique.</param>
/// <param name="SetPieces">Set pieces.</param>
public sealed record TechnicalAttributesResponse(
    int Finishing,
    int Passing,
    int Crossing,
    int Dribbling,
    int FirstTouch,
    int Tackling,
    int Marking,
    int Heading,
    int Technique,
    int SetPieces);

/// <summary>The mental attributes, each 1–20 (`TRN-4`).</summary>
/// <param name="Decisions">Decisions.</param>
/// <param name="Vision">Vision.</param>
/// <param name="Positioning">Positioning.</param>
/// <param name="Composure">Composure.</param>
/// <param name="Anticipation">Anticipation.</param>
/// <param name="WorkRate">Work rate.</param>
/// <param name="Aggression">Aggression.</param>
/// <param name="Leadership">Leadership.</param>
public sealed record MentalAttributesResponse(
    int Decisions,
    int Vision,
    int Positioning,
    int Composure,
    int Anticipation,
    int WorkRate,
    int Aggression,
    int Leadership);

/// <summary>The physical attributes, each 1–20 (`TRN-4`).</summary>
/// <param name="Pace">Pace.</param>
/// <param name="Acceleration">Acceleration.</param>
/// <param name="Stamina">Stamina.</param>
/// <param name="Strength">Strength.</param>
/// <param name="Agility">Agility.</param>
/// <param name="JumpingReach">Jumping reach.</param>
public sealed record PhysicalAttributesResponse(
    int Pace,
    int Acceleration,
    int Stamina,
    int Strength,
    int Agility,
    int JumpingReach);

/// <summary>The goalkeeping attributes, each 1–20 (`TRN-4`).</summary>
/// <param name="Handling">Handling.</param>
/// <param name="Reflexes">Reflexes.</param>
/// <param name="OneOnOnes">One-on-ones.</param>
/// <param name="AerialAbility">Aerial ability.</param>
public sealed record GoalkeepingAttributesResponse(
    int Handling,
    int Reflexes,
    int OneOnOnes,
    int AerialAbility);

/// <summary>The club's contract list (master plan §10.3; `CON-1`, `CON-8`).</summary>
/// <param name="ClubId">The club the contracts bind players to.</param>
/// <param name="ClubName">The generated club name.</param>
/// <param name="SeasonNumber">The season the remaining terms are counted from.</param>
/// <param name="WeeklyWageTotalMinor">The club's committed weekly wages, in minor units.</param>
/// <param name="Contracts">The contracts, closest to expiring first.</param>
/// <param name="ServerTime">The instant the response was produced.</param>
public sealed record ContractsResponse(
    Guid ClubId,
    string ClubName,
    int SeasonNumber,
    long WeeklyWageTotalMinor,
    IReadOnlyList<PlayerContractResponse> Contracts,
    DateTimeOffset ServerTime);

/// <summary>One match of a player's own history, with the line they had in it (`STA-2`, master plan §11.1).</summary>
/// <remarks>
/// Read from the stored match: the player's line (minutes, goals, assists, cards, rating) comes from the
/// result document and the shots and saves from the match's events, which is what the season totals are summed
/// from, so the rows add up to the season line. The passes and take-ons are the engine's own counts, carried on
/// the same line (`engine-v7`).
/// </remarks>
/// <param name="FixtureId">The fixture that was played.</param>
/// <param name="SeasonNumber">The season's ordinal in the world.</param>
/// <param name="SeasonLabel">The season's display label.</param>
/// <param name="Round">The matchday round within the season.</param>
/// <param name="PlayedAt">The kick-off instant.</param>
/// <param name="OpponentClubId">The opposing club.</param>
/// <param name="OpponentName">The opposing club's generated name.</param>
/// <param name="Home">Whether the player's club was the host.</param>
/// <param name="GoalsFor">The player's side's goals.</param>
/// <param name="GoalsAgainst">The opposing side's goals.</param>
/// <param name="Started">Whether the player was in the eleven.</param>
/// <param name="MinutesPlayed">Minutes on the pitch.</param>
/// <param name="Goals">Goals scored.</param>
/// <param name="Assists">Goals set up.</param>
/// <param name="Shots">Shots taken.</param>
/// <param name="ShotsOnTarget">Shots on target.</param>
/// <param name="Saves">Saves made.</param>
/// <param name="PassesAttempted">Passes attempted in open play.</param>
/// <param name="PassesCompleted">Passes that found a teammate.</param>
/// <param name="DribblesAttempted">Take-ons attempted.</param>
/// <param name="DribblesCompleted">Take-ons won.</param>
/// <param name="YellowCards">Bookings received.</param>
/// <param name="RedCards">Sendings-off.</param>
/// <param name="Rating">The match rating on a 0.0–10.0 scale, or null when the player was not rated (`TRN-8`).</param>
public sealed record PlayerMatchStatResponse(
    Guid FixtureId,
    int SeasonNumber,
    string SeasonLabel,
    int Round,
    DateTimeOffset PlayedAt,
    Guid OpponentClubId,
    string OpponentName,
    bool Home,
    int GoalsFor,
    int GoalsAgainst,
    bool Started,
    int MinutesPlayed,
    int Goals,
    int Assists,
    int Shots,
    int ShotsOnTarget,
    int Saves,
    int PassesAttempted,
    int PassesCompleted,
    int DribblesAttempted,
    int DribblesCompleted,
    int YellowCards,
    int RedCards,
    decimal? Rating);

/// <summary>A player's matches, most recent first, across every season they have appeared in.</summary>
/// <param name="PlayerId">The player.</param>
/// <param name="Matches">One row per match the player took the pitch in.</param>
/// <param name="ServerTime">The instant the response was produced.</param>
public sealed record PlayerMatchesResponse(
    Guid PlayerId,
    IReadOnlyList<PlayerMatchStatResponse> Matches,
    DateTimeOffset ServerTime);
