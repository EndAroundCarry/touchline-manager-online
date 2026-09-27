namespace TouchlineManager.Contracts.Competition;

/// <summary>How many points a result is worth (`TBL-1`).</summary>
/// <param name="Win">Points for a win.</param>
/// <param name="Draw">Points for a draw.</param>
/// <param name="Loss">Points for a defeat.</param>
public sealed record LeaguePointsResponse(int Win, int Draw, int Loss);

/// <summary>One tie-breaker, as the stable code of the criterion (`TBL-2`…`TBL-10`).</summary>
/// <param name="Code">The criterion's stable code; the client renders the wording.</param>
public sealed record TieBreakerResponse(string Code);

/// <summary>One club's place in the season's tie-break draw (`TBL-10`, `TBL-11`).</summary>
/// <param name="ClubId">The club.</param>
/// <param name="ClubName">The club's name.</param>
/// <param name="ClubShortName">The club's abbreviation.</param>
/// <param name="DrawKey">The key the final tie-breaker compares, derived from the stored seed.</param>
public sealed record DivisionRulesClubResponse(
    Guid ClubId,
    string ClubName,
    string ClubShortName,
    string DrawKey);

/// <summary>
/// A division's competition rules for the season in progress: the points, the ordering, and the stored draw
/// (master plan §10.5, `TBL-1`…`TBL-11`).
/// </summary>
/// <param name="DivisionId">The division.</param>
/// <param name="DivisionName">The division's name.</param>
/// <param name="TierNumber">The tier.</param>
/// <param name="CountryId">The country.</param>
/// <param name="CountryCode">The country's code.</param>
/// <param name="CountryName">The country's name.</param>
/// <param name="SeasonNumber">The season's ordinal in the world.</param>
/// <param name="SeasonLabel">The season's display label.</param>
/// <param name="Points">What a win, a draw, and a defeat are worth.</param>
/// <param name="TieBreakers">
/// Every tie-breaker in the order the table applies them, first difference deciding (`TBL-2`…`TBL-10`).
/// </param>
/// <param name="TieDrawSeed">
/// The seed the season's final tie-break draw was derived from. Stored before the season so the ordering can
/// always be reproduced and explained (`TBL-11`).
/// </param>
/// <param name="TieDrawHash">
/// The published digest of the seed, so a season's draw cannot be changed unnoticed (`TBL-11`).
/// </param>
/// <param name="Clubs">Every club in the division, with the draw key its ordering would use.</param>
/// <param name="ServerTime">
/// The server's current instant, so a client with a wrong clock shows the right "as of" (`TIME-5`).
/// </param>
public sealed record DivisionRulesResponse(
    Guid DivisionId,
    string DivisionName,
    int TierNumber,
    Guid CountryId,
    string CountryCode,
    string CountryName,
    int SeasonNumber,
    string SeasonLabel,
    LeaguePointsResponse Points,
    IReadOnlyList<TieBreakerResponse> TieBreakers,
    string TieDrawSeed,
    string TieDrawHash,
    IReadOnlyList<DivisionRulesClubResponse> Clubs,
    DateTimeOffset ServerTime);
