namespace TouchlineManager.Contracts.Match;

/// <summary>
/// One side's statistics, exactly as the engine derived them from the event stream (`MAT-5`).
/// </summary>
/// <remarks>
/// Every count reconciles with the events by construction, so a stats panel and the scoreline cannot
/// disagree. Possession is a share in basis points rather than a percentage, and it is the only figure
/// here that is not a count — which is why it carries its unit in its name.
/// </remarks>
/// <param name="PossessionBasisPoints">The side's share of possession, in basis points.</param>
/// <param name="Goals">Goals scored.</param>
/// <param name="Shots">Shots taken, whatever the outcome.</param>
/// <param name="ShotsOnTarget">Shots on target: goals plus saves.</param>
/// <param name="ShotsOffTarget">Shots that missed the target.</param>
/// <param name="ShotsBlocked">Shots blocked by a defender.</param>
/// <param name="WoodworkHits">Shots that hit the woodwork.</param>
/// <param name="Saves">Saves made.</param>
/// <param name="Corners">Corners won.</param>
/// <param name="Offsides">Offsides given against the side.</param>
/// <param name="Fouls">Fouls committed.</param>
/// <param name="YellowCards">Yellow cards shown.</param>
/// <param name="RedCards">Red cards shown, counting a second yellow as a sending-off.</param>
/// <param name="PenaltiesAwarded">Penalties awarded to the side.</param>
/// <param name="PenaltiesScored">Penalties converted.</param>
/// <param name="Injuries">Injuries suffered.</param>
/// <param name="Substitutions">Substitutions made.</param>
public sealed record MatchStatisticsResponse(
    int PossessionBasisPoints,
    int Goals,
    int Shots,
    int ShotsOnTarget,
    int ShotsOffTarget,
    int ShotsBlocked,
    int WoodworkHits,
    int Saves,
    int Corners,
    int Offsides,
    int Fouls,
    int YellowCards,
    int RedCards,
    int PenaltiesAwarded,
    int PenaltiesScored,
    int Injuries,
    int Substitutions);

/// <summary>One side of a played match: who it was, the score it finished on, and what it did.</summary>
/// <param name="ClubId">The club identity.</param>
/// <param name="Name">The club's generated name.</param>
/// <param name="ShortName">The club's abbreviation.</param>
/// <param name="Goals">The goals the side scored, as the published result records them.</param>
/// <param name="Statistics">The side's statistics.</param>
public sealed record MatchTeamResponse(
    Guid ClubId,
    string Name,
    string ShortName,
    int Goals,
    MatchStatisticsResponse Statistics);

/// <summary>
/// A played match's summary (master plan §9.5): the score, the statistics, and where it happened.
/// </summary>
/// <remarks>
/// <para>
/// Only a published match is readable, so the score here is the published one and never a staged result
/// that has yet to become public with its round (`MAT-7`). A match id reaches a client only once its
/// fixture published, so an unpublished match is answered as not found rather than as a hidden one.
/// </para>
/// <para>
/// It carries no simulation diagnostics: neither the input nor the output hash, and certainly not the
/// seed, is player-facing (`MAT-11`, §10.9). What it does carry is the presentation version, so a client
/// knows the shape of the replay it can ask for next.
/// </para>
/// </remarks>
/// <param name="MatchId">The match identity.</param>
/// <param name="FixtureId">The fixture that was played.</param>
/// <param name="DivisionId">The division the match belongs to.</param>
/// <param name="DivisionName">The division's generated name.</param>
/// <param name="TierNumber">The tier number.</param>
/// <param name="CountryId">The country.</param>
/// <param name="CountryCode">The country's stable code.</param>
/// <param name="CountryName">The country's display name.</param>
/// <param name="SeasonNumber">The season's ordinal in the world.</param>
/// <param name="SeasonLabel">The season's display label.</param>
/// <param name="RoundNumber">The round number, 1–34.</param>
/// <param name="KickoffAt">The kickoff instant, in UTC.</param>
/// <param name="Status">The publication state, <c>published</c>.</param>
/// <param name="Home">The host.</param>
/// <param name="Away">The visitor.</param>
/// <param name="EngineVersion">The engine version that produced the result.</param>
/// <param name="PresentationVersion">The version of the replay presentation this match offers.</param>
/// <param name="ServerTime">The instant the response was produced.</param>
public sealed record MatchResponse(
    Guid MatchId,
    Guid FixtureId,
    Guid DivisionId,
    string DivisionName,
    int TierNumber,
    Guid CountryId,
    string CountryCode,
    string CountryName,
    int SeasonNumber,
    string SeasonLabel,
    int RoundNumber,
    DateTimeOffset KickoffAt,
    string Status,
    MatchTeamResponse Home,
    MatchTeamResponse Away,
    string EngineVersion,
    string PresentationVersion,
    DateTimeOffset ServerTime);
