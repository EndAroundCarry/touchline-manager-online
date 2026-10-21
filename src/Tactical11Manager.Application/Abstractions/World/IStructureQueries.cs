using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.Abstractions.World;

/// <summary>One week of a building's books, as stored (`STRC-8`).</summary>
/// <param name="WeekOf">The first day of the week the weekly finance run settled.</param>
/// <param name="TakingsMinor">What the building took that week, in minor units.</param>
/// <param name="UpkeepMinor">What it cost to keep that week, in minor units, as a positive amount.</param>
public sealed record StructureWeekLine(DateOnly WeekOf, long TakingsMinor, long UpkeepMinor);

/// <summary>One home match the snack bar served, as stored (`STRC-4`).</summary>
/// <param name="FixtureId">The match.</param>
/// <param name="Opponent">The visiting club's name.</param>
/// <param name="Spectators">The crowd.</param>
/// <param name="GoalsFor">The home side's goals.</param>
/// <param name="GoalsAgainst">The visitors' goals.</param>
/// <param name="BonusBp">The result bonus applied, in basis points.</param>
/// <param name="BonusReason">The result bonus's reason code.</param>
/// <param name="TakingsMinor">What the snack bar took, in minor units.</param>
public sealed record StructureMatchLine(
    Guid FixtureId,
    string Opponent,
    int Spectators,
    int GoalsFor,
    int GoalsAgainst,
    int BonusBp,
    string BonusReason,
    long TakingsMinor);

/// <summary>What a building's page shows of its past (`STRC-4`, `STRC-6`, `STRC-8`).</summary>
/// <param name="Weeks">The most recent weeks, newest first.</param>
/// <param name="Matches">The most recent home matches, newest first. Empty for the shop.</param>
/// <param name="SeasonTakingsMinor">What the building has taken since the season opened, in minor units.</param>
/// <param name="SeasonUpkeepMinor">What it has cost to keep since the season opened, in minor units, as a positive amount.</param>
public sealed record StructureHistory(
    IReadOnlyList<StructureWeekLine> Weeks,
    IReadOnlyList<StructureMatchLine> Matches,
    long SeasonTakingsMinor,
    long SeasonUpkeepMinor);

/// <summary>
/// The structures page's read projection (`STRC-4`, `STRC-6`, `STRC-8`).
/// </summary>
/// <remarks>
/// The takings history is a projection over <c>world.club_structure_takings</c>, not a method on the
/// repository, because it joins the fixtures and the clubs it names (`MOD-3`).
/// </remarks>
public interface IStructureQueries
{
    /// <summary>Reads one building's recent weeks and home matches and its season totals.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="kind">Which building.</param>
    /// <param name="weeks">How many weeks to return at most.</param>
    /// <param name="matches">How many home matches to return at most.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<StructureHistory> GetHistoryAsync(
        Guid clubId,
        StructureKind kind,
        int weeks,
        int matches,
        CancellationToken cancellationToken);
}
