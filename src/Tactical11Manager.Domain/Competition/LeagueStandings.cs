namespace Tactical11Manager.Domain.Competition;

/// <summary>One active league's final ordering, as promotion, relegation and the play-off bracket read it (`PR-1`).</summary>
/// <param name="DivisionId">The league's durable division.</param>
/// <param name="Tier">The tier number, ascending down the pyramid.</param>
/// <param name="League">The league's number within its tier.</param>
/// <param name="ParentDivisionId">The league of the tier above that this league feeds, or <see langword="null"/> for the top division.</param>
/// <param name="RankedClubIds">The league's clubs, best first, exactly as the finalized table ranks them (`TBL-12`).</param>
public sealed record LeagueStandings(
    Guid DivisionId,
    int Tier,
    int League,
    Guid? ParentDivisionId,
    IReadOnlyList<Guid> RankedClubIds);
