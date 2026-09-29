namespace TouchlineManager.Contracts.Competition;

/// <summary>One season a club has finished, as its history shows it (master plan §11.1, `PR-4`).</summary>
/// <remarks>
/// The figures are the ones the rollover wrote once and never rewrote (`PR-6`): the final rank, the movement
/// flags, the closing reputation, and the closing cash. The closing cash equals the season finance summary's
/// closing cash by construction (ADR-0032), so the history reads the entry and leaves the summary's category
/// breakdown to a dedicated finances screen.
/// </remarks>
/// <param name="SeasonNumber">The season's ordinal in the world.</param>
/// <param name="SeasonLabel">The season's display label.</param>
/// <param name="TierNumber">The tier the club played in that season.</param>
/// <param name="DivisionName">The tier's generated name.</param>
/// <param name="FinalRank">The club's final position, 1–18.</param>
/// <param name="Promoted">Whether the club was promoted at that season's rollover.</param>
/// <param name="Relegated">Whether the club was relegated at that season's rollover.</param>
/// <param name="ClosingCashMinor">The club's cash at the end of the season, in minor units.</param>
/// <param name="ClosingReputation">The club's reputation at the end of the season, on the 1–100 scale.</param>
public sealed record ClubSeasonHistoryEntryResponse(
    int SeasonNumber,
    string SeasonLabel,
    int TierNumber,
    string DivisionName,
    int FinalRank,
    bool Promoted,
    bool Relegated,
    long ClosingCashMinor,
    int ClosingReputation);

/// <summary>The next season a club is already placed in, when one exists (`PR-5`, `PR-6`).</summary>
/// <remarks>
/// Null outside the rollover window: the next season is created by the move phase and the world pointer
/// advances at complete, so between them a manager can see where their club is going before the season
/// begins.
/// </remarks>
/// <param name="SeasonNumber">The next season's ordinal in the world.</param>
/// <param name="SeasonLabel">The next season's display label.</param>
/// <param name="StartsAt">When the next season's first matchday is.</param>
/// <param name="DivisionName">The tier the club is placed in.</param>
/// <param name="TierNumber">The tier number.</param>
/// <param name="Movement">
/// How the club arrived: <c>promoted</c>, <c>relegated</c>, or <c>none</c>. A word, so the screen never has
/// to infer it from a tier comparison.
/// </param>
public sealed record NextSeasonSummaryResponse(
    int SeasonNumber,
    string SeasonLabel,
    DateTimeOffset StartsAt,
    string DivisionName,
    int TierNumber,
    string Movement);

/// <summary>A club's finished seasons, newest first, with the next season when one is already known.</summary>
/// <param name="ClubId">The club.</param>
/// <param name="ClubName">The club's generated name.</param>
/// <param name="ClubShortName">The club's abbreviation.</param>
/// <param name="Seasons">Every season the club has finished, most recent first.</param>
/// <param name="NextSeason">Where the club is going next, or null when the next season does not exist yet.</param>
/// <param name="ServerTime">
/// The server's current instant, so a client with a wrong clock shows the right "as of" (`TIME-5`).
/// </param>
public sealed record ClubSeasonHistoryResponse(
    Guid ClubId,
    string ClubName,
    string ClubShortName,
    IReadOnlyList<ClubSeasonHistoryEntryResponse> Seasons,
    NextSeasonSummaryResponse? NextSeason,
    DateTimeOffset ServerTime);
