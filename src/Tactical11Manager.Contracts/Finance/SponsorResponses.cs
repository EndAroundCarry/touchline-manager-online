namespace Tactical11Manager.Contracts.Finance;

/// <summary>What one objective pays under one offer (`OBJ-2`).</summary>
/// <param name="Objective">The objective's stable code (e.g. <c>top_three</c>).</param>
/// <param name="TargetPlace">The final place it asks for.</param>
/// <param name="BonusMetMinor">The bonus for meeting it, in minor units.</param>
/// <param name="BonusExceededMinor">The bonus for finishing three or more places better, in minor units.</param>
public sealed record SponsorObjectiveQuoteResponse(string Objective, int TargetPlace, long BonusMetMinor, long BonusExceededMinor);

/// <summary>One of the three offers a club's sponsors make for the season (`SPN-1`).</summary>
/// <param name="Kind">The offer's stable code: <c>steady</c>, <c>balanced</c> or <c>performance</c>.</param>
/// <param name="SponsorName">The sponsor's name.</param>
/// <param name="FixedSeasonMinor">What it pays over the season whatever happens, in minor units.</param>
/// <param name="FixedMatchdayMinor">What it pays on each matchday, in minor units.</param>
/// <param name="BonusMultiplierBp">How much the offer multiplies a bonus by, in basis points.</param>
/// <param name="Objectives">What every objective would pay under the offer, hardest first.</param>
public sealed record SponsorOfferResponse(
    string Kind,
    string SponsorName,
    long FixedSeasonMinor,
    long FixedMatchdayMinor,
    int BonusMultiplierBp,
    IReadOnlyList<SponsorObjectiveQuoteResponse> Objectives);

/// <summary>The deal a club signed for the season and, once the season is over, how it turned out (`SPN-2`, `OBJ-4`).</summary>
/// <param name="Kind">The offer's stable code.</param>
/// <param name="SponsorName">The sponsor's name.</param>
/// <param name="Objective">The objective's stable code.</param>
/// <param name="TargetPlace">The final place the objective asks for.</param>
/// <param name="FixedSeasonMinor">What the deal pays over the season whatever happens.</param>
/// <param name="FixedMatchdayMinor">What it pays on each matchday.</param>
/// <param name="BonusMetMinor">The bonus for meeting the objective.</param>
/// <param name="BonusExceededMinor">The bonus for exceeding it.</param>
/// <param name="SignedBy">Who signed: <c>manager</c>, <c>board</c> or <c>ai</c>.</param>
/// <param name="SignedAt">When it was signed.</param>
/// <param name="Outcome">How the season ended against the objective (<c>missed</c>, <c>met</c> or <c>exceeded</c>), or null before it is settled.</param>
/// <param name="BonusPaidMinor">The bonus the season paid, or null before it is settled.</param>
/// <param name="FinalPlace">The place the club finished in, or null before the season is settled.</param>
public sealed record SponsorDealResponse(
    string Kind,
    string SponsorName,
    string Objective,
    int TargetPlace,
    long FixedSeasonMinor,
    long FixedMatchdayMinor,
    long BonusMetMinor,
    long BonusExceededMinor,
    string SignedBy,
    DateTimeOffset SignedAt,
    string? Outcome,
    long? BonusPaidMinor,
    int? FinalPlace);

/// <summary>The club's sponsor and objective for the season: the offers, the deal, and the deadline (master plan §10.7, `SPN-1`…`SPN-6`).</summary>
/// <param name="SeasonNumber">The season's ordinal in the world.</param>
/// <param name="Deadline">When the board signs for the club if it has not signed: the fourth matchday's team-sheet lock.</param>
/// <param name="RealisticObjective">The objective the board thinks fits the squad, as a stable code.</param>
/// <param name="MatchdaysPlayed">How many matchdays of the season are published.</param>
/// <param name="Offers">The three offers, steady first.</param>
/// <param name="Deal">The deal, or null while the club has not signed.</param>
/// <param name="CanSign">Whether the manager can sign or change the deal now.</param>
/// <param name="Version">The deal's version, which the request must send back in <c>If-Match</c>: 0 before there is a deal.</param>
/// <param name="ServerTime">The server's current instant.</param>
public sealed record SponsorResponse(
    int SeasonNumber,
    DateTimeOffset Deadline,
    string RealisticObjective,
    int MatchdaysPlayed,
    IReadOnlyList<SponsorOfferResponse> Offers,
    SponsorDealResponse? Deal,
    bool CanSign,
    long Version,
    DateTimeOffset ServerTime);

/// <summary>The choice a manager signs: an offer and an objective.</summary>
/// <param name="Offer">The offer's stable code.</param>
/// <param name="Objective">The objective's stable code.</param>
public sealed record SignSponsorRequest(string? Offer, string? Objective);
