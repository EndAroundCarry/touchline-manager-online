namespace Tactical11Manager.Contracts.Competition;

/// <summary>One club as a fixture list names it, so a list of 306 fixtures does not repeat 306 names.</summary>
/// <param name="Id">The club identity.</param>
/// <param name="Name">The generated club name.</param>
/// <param name="ShortName">The abbreviation.</param>
public sealed record FixtureClubResponse(Guid Id, string Name, string ShortName);

/// <summary>One kick of a play-off shoot-out, in the order it was taken (`PR-6`).</summary>
/// <param name="Number">The kick's place in the shoot-out, from 1.</param>
/// <param name="Round">The round, from 1: each side kicks once a round.</param>
/// <param name="Side">The side that took it, <c>home</c> or <c>away</c>.</param>
/// <param name="TakerName">The player who took it.</param>
/// <param name="GoalkeeperName">The goalkeeper who faced it, or null when the other side had none on the pitch.</param>
/// <param name="Outcome">How it ended: <c>scored</c>, <c>saved</c> or <c>wide</c>.</param>
public sealed record PlayoffKickResponse(
    int Number,
    int Round,
    string Side,
    string TakerName,
    string? GoalkeeperName,
    string Outcome);

/// <summary>
/// How a promotion play-off match was settled, present only once its fixture is published (`PR-6`).
/// </summary>
/// <remarks>
/// It gives the draw away when the 90 minutes finished level, so a client holds it back behind the same gate as the score.
/// </remarks>
/// <param name="Stage">The stage code: <c>semi_final</c> or <c>final</c>.</param>
/// <param name="StageLabel">The words for the stage, e.g. <c>Play-off final</c>.</param>
/// <param name="DecidedBy">How the tie was settled: <c>score</c> or <c>penalties</c>. Null while it is undecided.</param>
/// <param name="WinnerClubId">The club that goes through. Null while the tie is undecided.</param>
/// <param name="ShootoutHomeScore">The home side's shoot-out goals, when the tie went to penalties.</param>
/// <param name="ShootoutAwayScore">The away side's shoot-out goals, when the tie went to penalties.</param>
/// <param name="FirstSide">The side that kicked first, <c>home</c> or <c>away</c>, when the tie went to penalties.</param>
/// <param name="Kicks">The shoot-out's kicks in order; empty unless the tie went to penalties.</param>
public sealed record PlayoffResultResponse(
    string Stage,
    string StageLabel,
    string? DecidedBy,
    Guid? WinnerClubId,
    int? ShootoutHomeScore,
    int? ShootoutAwayScore,
    string? FirstSide,
    IReadOnlyList<PlayoffKickResponse> Kicks);

/// <summary>One fixture, as a division's calendar and a club's own list show it.</summary>
/// <param name="Id">The fixture identity.</param>
/// <param name="MatchdayId">The round the fixture belongs to.</param>
/// <param name="RoundNumber">The round number, 1–30 (`CAL-1`); 31–33 for a promotion play-off match (`PR-4`).</param>
/// <param name="HomeClubId">The host.</param>
/// <param name="AwayClubId">The visitor.</param>
/// <param name="KickoffAt">The kickoff instant, in UTC (`CAL-2`).</param>
/// <param name="Status">The lifecycle state: scheduled, locked, simulating, staged, published, or void.</param>
/// <param name="HomeScore">The home score, present only once the result is staged or published (`MAT-7`).</param>
/// <param name="AwayScore">The away score, present only once the result is staged or published.</param>
/// <param name="MatchId">The simulated match, present once a result is staged.</param>
/// <param name="IsBootstrap">Whether the result is generated history a provisioned tier backfilled (`PYR-7`).</param>
/// <param name="Kind">What the match is for: <c>league</c> or <c>playoff</c> (`PR-4`).</param>
/// <param name="StageLabel">For a play-off match, the words for its stage; null for a league match.</param>
public sealed record FixtureSummaryResponse(
    Guid Id,
    Guid MatchdayId,
    int RoundNumber,
    Guid HomeClubId,
    Guid AwayClubId,
    DateTimeOffset KickoffAt,
    string Status,
    int? HomeScore,
    int? AwayScore,
    Guid? MatchId,
    bool IsBootstrap,
    string Kind,
    string? StageLabel);

/// <summary>One round of a division's season, with the eight fixtures it comprises (`CAL-10`).</summary>
/// <param name="Id">The matchday identity.</param>
/// <param name="RoundNumber">The round number, 1–30.</param>
/// <param name="LockAt">When team sheets lock, thirty minutes before kickoff (`CAL-3`).</param>
/// <param name="KickoffAt">The kickoff, shared by every fixture in the round (`CAL-2`).</param>
/// <param name="PublicationStatus">Whether the round is pending, staged, or published (`MAT-7`).</param>
/// <param name="Fixtures">The round's fixtures, in a stable order.</param>
/// <param name="Kind">What the round is for: <c>league</c> or <c>playoff</c> (`PR-4`).</param>
/// <param name="StageLabel">For a play-off round, the words for its stage; null for a league round.</param>
public sealed record FixtureMatchdayResponse(
    Guid Id,
    int RoundNumber,
    DateTimeOffset LockAt,
    DateTimeOffset KickoffAt,
    string PublicationStatus,
    IReadOnlyList<FixtureSummaryResponse> Fixtures,
    string Kind,
    string? StageLabel);

/// <summary>
/// A division-season's whole fixture calendar (master plan §10.5).
/// </summary>
/// <remarks>
/// The set is bounded by construction — sixteen clubs play a 30-round double round-robin, so exactly 240
/// fixtures exist — which is what makes one unpaged response the right shape. The clubs travel once and
/// the fixtures reference them by identity, so a 240-row payload does not carry sixteen names 30 times.
/// </remarks>
/// <param name="DivisionId">The division identity.</param>
/// <param name="DivisionName">The generated tier name.</param>
/// <param name="TierNumber">The tier number.</param>
/// <param name="CountryId">The owning country.</param>
/// <param name="CountryCode">The country's stable code.</param>
/// <param name="CountryName">The country's display name.</param>
/// <param name="SeasonNumber">The season sequence number.</param>
/// <param name="SeasonLabel">The season label managers see, e.g. <c>2026/27</c>.</param>
/// <param name="Clubs">Every club in the division, so the fixtures' identities resolve to names.</param>
/// <param name="Matchdays">The season's matchdays, in round order.</param>
/// <param name="ServerTime">The instant the response was produced.</param>
public sealed record DivisionFixturesResponse(
    Guid DivisionId,
    string DivisionName,
    int TierNumber,
    Guid CountryId,
    string CountryCode,
    string CountryName,
    int SeasonNumber,
    string SeasonLabel,
    IReadOnlyList<FixtureClubResponse> Clubs,
    IReadOnlyList<FixtureMatchdayResponse> Matchdays,
    DateTimeOffset ServerTime);

/// <summary>
/// One of a managed club's fixtures, from that club's point of view.
/// </summary>
/// <remarks>
/// The venue and opponent are resolved server-side rather than left as a home/away pair, because the
/// club's own screen always reads the fixture from its own side and answering "which end am I at" once is
/// better than every caller recomputing it.
/// </remarks>
/// <param name="Id">The fixture identity.</param>
/// <param name="RoundNumber">The round number, 1–30.</param>
/// <param name="Venue">The managed club's side: <c>home</c> or <c>away</c>.</param>
/// <param name="OpponentClubId">The other club.</param>
/// <param name="OpponentName">The other club's generated name.</param>
/// <param name="OpponentShortName">The other club's abbreviation.</param>
/// <param name="OpponentPrimaryColour">The other club's main kit colour as <c>#rrggbb</c>: the one chosen, or the generated one.</param>
/// <param name="OpponentSecondaryColour">The other club's second kit colour as <c>#rrggbb</c>.</param>
/// <param name="OpponentCrestShape">The code of the crest shape the other club wears.</param>
/// <param name="OpponentCrestPattern">The code of the stripes on the other club's crest.</param>
/// <param name="KickoffAt">The kickoff instant, in UTC.</param>
/// <param name="LockAt">When team sheets lock (`CAL-3`).</param>
/// <param name="Status">The lifecycle state.</param>
/// <param name="HomeScore">The home score, once a result exists.</param>
/// <param name="AwayScore">The away score, once a result exists.</param>
/// <param name="MatchId">The simulated match, once a result is staged.</param>
/// <param name="Outcome">For a published fixture, the managed club's result: <c>win</c>, <c>draw</c>, or <c>loss</c>. Null otherwise.</param>
/// <param name="IsBootstrap">Whether the result is generated history a provisioned tier backfilled (`PYR-7`).</param>
/// <param name="Kind">What the match is for: <c>league</c> or <c>playoff</c> (`PR-4`).</param>
/// <param name="StageLabel">For a play-off match, the words for its stage; null for a league match.</param>
public sealed record ClubFixtureResponse(
    Guid Id,
    int RoundNumber,
    string Venue,
    Guid OpponentClubId,
    string OpponentName,
    string OpponentShortName,
    string OpponentPrimaryColour,
    string OpponentSecondaryColour,
    string OpponentCrestShape,
    string OpponentCrestPattern,
    DateTimeOffset KickoffAt,
    DateTimeOffset LockAt,
    string Status,
    int? HomeScore,
    int? AwayScore,
    Guid? MatchId,
    string? Outcome,
    bool IsBootstrap,
    string Kind,
    string? StageLabel);

/// <summary>
/// A managed club's season fixture list, with its next fixture called out (master plan §11.1).
/// </summary>
/// <remarks>
/// The dashboard reads this one response: it answers "what am I playing next, and when does it lock"
/// without a second call, which is why the next fixture is named rather than left to the client to infer.
/// A season has 30 fixtures per club, so the set is bounded and needs no pagination.
/// </remarks>
/// <param name="ClubId">The managed club.</param>
/// <param name="ClubName">The managed club's generated name.</param>
/// <param name="ClubShortName">The managed club's abbreviation.</param>
/// <param name="DivisionId">The division the club plays in.</param>
/// <param name="DivisionName">The division's generated name.</param>
/// <param name="TierNumber">The tier number.</param>
/// <param name="SeasonNumber">The season sequence number.</param>
/// <param name="SeasonLabel">The season label managers see.</param>
/// <param name="NextFixtureId">The next fixture yet to publish, or null once the season is done.</param>
/// <param name="Fixtures">The club's fixtures, in round order.</param>
/// <param name="ServerTime">The instant the response was produced.</param>
public sealed record MyFixturesResponse(
    Guid ClubId,
    string ClubName,
    string ClubShortName,
    Guid DivisionId,
    string DivisionName,
    int TierNumber,
    int SeasonNumber,
    string SeasonLabel,
    Guid? NextFixtureId,
    IReadOnlyList<ClubFixtureResponse> Fixtures,
    DateTimeOffset ServerTime);

/// <summary>One side of a fixture, as the prepare-match screen names it.</summary>
/// <param name="ClubId">The club identity.</param>
/// <param name="Name">The generated club name.</param>
/// <param name="ShortName">The abbreviation.</param>
/// <param name="City">The generated home city.</param>
/// <param name="Region">The generated region.</param>
public sealed record FixtureSideResponse(
    Guid ClubId,
    string Name,
    string ShortName,
    string City,
    string Region);

/// <summary>
/// A fixture in full, as the prepare-match screen reads it (master plan §11.1).
/// </summary>
/// <remarks>
/// <see cref="Manageable"/> is the server's answer to "may this account prepare a side for this fixture",
/// so the screen shows the control rather than the screen deciding and being refused. A fixture is public
/// game data, so reading one is not gated on holding a club; preparing for one is.
/// </remarks>
/// <param name="Id">The fixture identity.</param>
/// <param name="MatchdayId">The round the fixture belongs to.</param>
/// <param name="DivisionId">The division.</param>
/// <param name="DivisionName">The division's generated name.</param>
/// <param name="TierNumber">The tier number.</param>
/// <param name="CountryCode">The country's stable code.</param>
/// <param name="RoundNumber">The round number, 1–30.</param>
/// <param name="LockAt">When team sheets lock (`CAL-3`).</param>
/// <param name="KickoffAt">The kickoff instant, in UTC.</param>
/// <param name="Status">The lifecycle state.</param>
/// <param name="IsLocked">Whether the sheet deadline has passed or the fixture has moved past scheduled.</param>
/// <param name="ManagedClubId">The club the caller holds among the two, or null when they hold neither.</param>
/// <param name="ManagedSide">The caller's side, <c>home</c> or <c>away</c>, or null when they hold neither.</param>
/// <param name="Home">The host.</param>
/// <param name="Away">The visitor.</param>
/// <param name="HomeScore">The home score, once a result exists.</param>
/// <param name="AwayScore">The away score, once a result exists.</param>
/// <param name="MatchId">The simulated match, once a result is staged.</param>
/// <param name="IsBootstrap">Whether the result is generated history a provisioned tier backfilled (`PYR-7`).</param>
/// <param name="Kind">What the match is for: <c>league</c> or <c>playoff</c> (`PR-4`).</param>
/// <param name="StageLabel">For a play-off match, the words for its stage; null for a league match.</param>
/// <param name="Playoff">How a play-off match was settled, present only once the fixture is published (`PR-6`).</param>
/// <param name="ServerTime">The instant the response was produced.</param>
public sealed record FixtureDetailResponse(
    Guid Id,
    Guid MatchdayId,
    Guid DivisionId,
    string DivisionName,
    int TierNumber,
    string CountryCode,
    int RoundNumber,
    DateTimeOffset LockAt,
    DateTimeOffset KickoffAt,
    string Status,
    bool IsLocked,
    Guid? ManagedClubId,
    string? ManagedSide,
    FixtureSideResponse Home,
    FixtureSideResponse Away,
    int? HomeScore,
    int? AwayScore,
    Guid? MatchId,
    bool IsBootstrap,
    string Kind,
    string? StageLabel,
    PlayoffResultResponse? Playoff,
    DateTimeOffset ServerTime);
