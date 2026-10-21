using Tactical11Manager.Contracts.Competition;

namespace Tactical11Manager.Contracts.Fans;

/// <summary>
/// Stable machine-readable error codes for the fan reads.
/// </summary>
public static class FanErrorCodes
{
    /// <summary>The requested history is not one the fan base serves.</summary>
    public const string InvalidHistory = "FANS_INVALID_HISTORY";
}

/// <summary>
/// How much of a club's fan history the Fan Base page asks for (`FAN-10`).
/// </summary>
public static class FanHistoryScopes
{
    /// <summary>Only what happened since the current manager took the club over: the default.</summary>
    public const string Tenure = "tenure";

    /// <summary>The whole club history, with the takeovers in it as markers.</summary>
    public const string Club = "club";
}

/// <summary>What a club's fans expect from one match, from that club's side (`FAN-4`).</summary>
/// <param name="ExpectedPointsCentis">The points the fans expect the club to take, in hundredths.</param>
/// <param name="WinBp">The chance of a win they give, in basis points.</param>
/// <param name="DrawBp">The chance of a draw they give, in basis points.</param>
/// <param name="LossBp">The chance of a defeat they give, in basis points.</param>
/// <param name="LabelCode">The stable code of the label, such as <c>should_win</c>.</param>
/// <param name="LabelPhrase">The words the game shows, such as "Should win".</param>
/// <param name="PredictedGoalsFor">The goals the club scores in the score the fans would bet on.</param>
/// <param name="PredictedGoalsAgainst">The goals the club concedes in it.</param>
public sealed record FanExpectationResponse(
    int ExpectedPointsCentis,
    int WinBp,
    int DrawBp,
    int LossBp,
    string LabelCode,
    string LabelPhrase,
    int PredictedGoalsFor,
    int PredictedGoalsAgainst);

/// <summary>The club's next match with what its fans expect from it.</summary>
/// <param name="Fixture">The fixture, from the club's point of view.</param>
/// <param name="Expectation">What the fans expect.</param>
/// <param name="Frozen">Whether the expectation was frozen when the team sheets locked; false means it is worked out now from the squads and may still move.</param>
public sealed record FanNextFixtureResponse(
    ClubFixtureResponse Fixture,
    FanExpectationResponse Expectation,
    bool Frozen);

/// <summary>One published match with what the fans expected and how they reacted.</summary>
/// <param name="Fixture">The fixture, from the club's point of view.</param>
/// <param name="Expectation">What the fans expected at the lock, or null when the match was locked before fans existed.</param>
/// <param name="PointsWon">The points the club took.</param>
/// <param name="FansBefore">The fans before the match, or null when no change was recorded.</param>
/// <param name="FansAfter">The fans after it.</param>
/// <param name="MoodBeforeBp">The mood before the match, in basis points.</param>
/// <param name="MoodAfterBp">The mood after it, in basis points.</param>
public sealed record FanRecentMatchResponse(
    ClubFixtureResponse Fixture,
    FanExpectationResponse? Expectation,
    int? PointsWon,
    int? FansBefore,
    int? FansAfter,
    int? MoodBeforeBp,
    int? MoodAfterBp);

/// <summary>One point on the Fan Base page's charts: a change to the club's fans (`FAN-10`).</summary>
/// <param name="Id">The event identity.</param>
/// <param name="Kind">What moved the fans: <c>opening</c>, <c>match</c>, <c>weekly</c>, <c>season_reset</c>, <c>promotion</c>, <c>relegation</c> or <c>takeover</c>.</param>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="SeasonNumber">The season it belongs to, from 1.</param>
/// <param name="FansAfter">The fans after the change.</param>
/// <param name="MoodAfterBp">The mood after the change, in basis points.</param>
/// <param name="FixtureId">The match, on a match event.</param>
/// <param name="ExpectedPointsCentis">The points the fans expected, on a match event.</param>
/// <param name="PointsWon">The points the club took, on a match event.</param>
/// <param name="Gap">The club's place minus the fans' target, on a weekly review and a season reset.</param>
public sealed record FanEventResponse(
    Guid Id,
    string Kind,
    DateTimeOffset OccurredAt,
    int SeasonNumber,
    int FansAfter,
    int MoodAfterBp,
    Guid? FixtureId,
    int? ExpectedPointsCentis,
    int? PointsWon,
    int? Gap);

/// <summary>
/// The Fan Base page: the club's fans as they stand, what they expect from the next match, how the last ones went, and the history the
/// charts draw (`FAN-1`…`FAN-10`).
/// </summary>
/// <param name="ClubId">The club.</param>
/// <param name="ClubName">The club's name.</param>
/// <param name="TierNumber">The tier the club plays in, starting at 1.</param>
/// <param name="Fans">The fans the club has.</param>
/// <param name="WantToCome">The people who want to come to a home match at the start of the count: a share of the fans, before the mood and the ground.</param>
/// <param name="HomeDemand">The crowd the next home match draws if nothing moves the fans before it, before the ground limits it (`STAD-5`).</param>
/// <param name="MoodBp">How satisfied the fans are, from 0 to 10,000 basis points.</param>
/// <param name="MoodCode">The stable code of the mood word, such as <c>calm</c>.</param>
/// <param name="MoodPhrase">The mood word the game shows, such as "Calm".</param>
/// <param name="FloorFans">The fewest fans a club in this tier can have.</param>
/// <param name="SoftCapFans">The fans above which a club in this tier stops winning new ones.</param>
/// <param name="SeasonTargetRank">The place the fans expect the club to finish this season, or null before it is set.</param>
/// <param name="LastWeekGap">Where the club stood against that place at the last weekly review (projected place minus target), or null before the first.</param>
/// <param name="SeasonFansChange">How many fans the club has gained or lost since the season began, or since the manager took over if that was later.</param>
/// <param name="History">Which part of the history <paramref name="Events"/> holds: <c>tenure</c> or <c>club</c>.</param>
/// <param name="TenureStartedAt">When the current manager took the club over, or null when the club has never had a takeover.</param>
/// <param name="NextFixture">The club's next match, or null when the season has none left.</param>
/// <param name="RecentMatches">The last published matches of the season, newest first.</param>
/// <param name="Events">The history, oldest first.</param>
/// <param name="ServerTime">When the response was produced.</param>
public sealed record FanBaseResponse(
    Guid ClubId,
    string ClubName,
    int TierNumber,
    int Fans,
    int WantToCome,
    int HomeDemand,
    int MoodBp,
    string MoodCode,
    string MoodPhrase,
    int FloorFans,
    int SoftCapFans,
    int? SeasonTargetRank,
    int? LastWeekGap,
    int SeasonFansChange,
    string History,
    DateTimeOffset? TenureStartedAt,
    FanNextFixtureResponse? NextFixture,
    IReadOnlyList<FanRecentMatchResponse> RecentMatches,
    IReadOnlyList<FanEventResponse> Events,
    DateTimeOffset ServerTime);

/// <summary>What anyone can see of a club's fans: how many there are and how they feel.</summary>
/// <param name="ClubId">The club.</param>
/// <param name="ClubName">The club's name.</param>
/// <param name="Fans">The fans the club has.</param>
/// <param name="MoodCode">The stable code of the mood word, such as <c>calm</c>.</param>
/// <param name="MoodPhrase">The mood word the game shows, such as "Calm".</param>
public sealed record FanSummaryResponse(
    Guid ClubId,
    string ClubName,
    int Fans,
    string MoodCode,
    string MoodPhrase);
