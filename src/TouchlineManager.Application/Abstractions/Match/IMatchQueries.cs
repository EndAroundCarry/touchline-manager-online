using TouchlineManager.Domain.Match;

namespace TouchlineManager.Application.Abstractions.Match;

/// <summary>One club as a match read names it.</summary>
/// <param name="Id">The club identity.</param>
/// <param name="Name">The generated club name.</param>
/// <param name="ShortName">The abbreviation.</param>
public sealed record MatchClubRow(Guid Id, string Name, string ShortName);

/// <summary>
/// A published match, with everything its summary and its replay are built from (master plan §9.5).
/// </summary>
/// <remarks>
/// <para>
/// The read is deliberately measured against the match's own season rather than the world's current one:
/// a played match is history and must stay readable after a rollover, which is the point of storing it at
/// all (`MAT-9`).
/// </para>
/// <para>
/// It carries the frozen <see cref="Snapshot"/> as well as the stored result, because the replay is
/// re-derived rather than stored: the presentation is a pure function of the snapshot the match was
/// simulated from, and re-deriving it means the commentary and highlights can never drift from the result
/// they describe. Only a published fixture produces one, so the query filters on that rather than leaving
/// visibility to a caller to remember.
/// </para>
/// </remarks>
/// <param name="MatchId">The match identity.</param>
/// <param name="FixtureId">The fixture that was played.</param>
/// <param name="DivisionId">The division.</param>
/// <param name="DivisionName">The division's generated name.</param>
/// <param name="TierNumber">The tier number.</param>
/// <param name="CountryId">The country.</param>
/// <param name="CountryCode">The country's stable code.</param>
/// <param name="CountryName">The country's display name.</param>
/// <param name="SeasonNumber">The season's ordinal.</param>
/// <param name="SeasonLabel">The season's display label.</param>
/// <param name="RoundNumber">The round number, 1–34.</param>
/// <param name="KickoffAt">The kickoff instant, in UTC.</param>
/// <param name="EngineVersion">The engine version that produced the result.</param>
/// <param name="RuleSetVersion">The rules version the result was produced under.</param>
/// <param name="HomeGoals">The stored home score.</param>
/// <param name="AwayGoals">The stored away score.</param>
/// <param name="StatisticsJson">The stored statistics document (`MAT-5`).</param>
/// <param name="OutputHash">The stored output hash, which the re-derived result must reproduce (`MAT-9`).</param>
/// <param name="Home">The host.</param>
/// <param name="Away">The visitor.</param>
/// <param name="Snapshot">The frozen input the match was simulated from.</param>
public sealed record MatchReadSnapshot(
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
    string EngineVersion,
    string RuleSetVersion,
    int HomeGoals,
    int AwayGoals,
    string StatisticsJson,
    string OutputHash,
    MatchClubRow Home,
    MatchClubRow Away,
    InputSnapshot Snapshot);

/// <summary>
/// The read side of the match module: one played match, for the summary and the replay (master plan §9.5).
/// </summary>
/// <remarks>
/// A projection rather than a tracked aggregate, like the competition reads: one query shaped for the two
/// screens, and nothing here is used to make a decision (`MOD-3`). The write side keeps its port, so a
/// screen can change without widening what a command can reach.
/// </remarks>
public interface IMatchQueries
{
    /// <summary>Reads a published match in full, or null when no published match has that identity.</summary>
    /// <param name="matchId">The match to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<MatchReadSnapshot?> GetMatchAsync(Guid matchId, CancellationToken cancellationToken);
}
