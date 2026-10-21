namespace Tactical11Manager.Contracts.World;

/// <summary>What the next level of a building would change (`STRC-3`).</summary>
/// <param name="Level">The level an upgrade reaches.</param>
/// <param name="SpendMinor">What a spectator (snack bar) or a fan (shop) would spend at that level, in minor units.</param>
/// <param name="UpkeepWeeklyMinor">What the building would cost to keep each week at that level, in minor units.</param>
/// <param name="CostMinor">What the upgrade costs, in minor units, paid in full when ordered.</param>
/// <param name="BuildDays">How many days the work takes.</param>
/// <param name="EstWeeklyProfitChangeMinor">
/// How much a week's profit would move at that level, in minor units, judged by the crowd or the fans and mood as they stand now and before
/// any result bonus. Negative when the upkeep outgrows the takings.
/// </param>
public sealed record StructureNextLevelResponse(
    int Level,
    long SpendMinor,
    long UpkeepWeeklyMinor,
    long CostMinor,
    int BuildDays,
    long EstWeeklyProfitChangeMinor);

/// <summary>An upgrade that is being built (`STRC-3`).</summary>
/// <param name="TargetLevel">The level the work will reach.</param>
/// <param name="DueAt">When the work finishes.</param>
public sealed record StructureBuildingResponse(int TargetLevel, DateTimeOffset DueAt);

/// <summary>One line of a bonus table: a result or a mood, and how much it lifts the takings (`STRC-5`, `STRC-7`).</summary>
/// <param name="Reason">A stable code: a result such as <c>underdog_win</c>, or a fan mood word such as <c>content</c>.</param>
/// <param name="Bp">The bonus, in basis points of the takings.</param>
/// <param name="Current">True for the line that applies now (the shop's mood) or applied to the last home match (the snack bar).</param>
public sealed record StructureBonusResponse(string Reason, int Bp, bool Current);

/// <summary>One week of a building's books (`STRC-8`).</summary>
/// <param name="WeekOf">The first day of the week the weekly finance run settled.</param>
/// <param name="TakingsMinor">What the building took that week, in minor units.</param>
/// <param name="UpkeepMinor">What it cost to keep that week, in minor units, as a positive amount.</param>
/// <param name="ProfitMinor">Takings less upkeep, in minor units.</param>
public sealed record StructureWeekResponse(DateOnly WeekOf, long TakingsMinor, long UpkeepMinor, long ProfitMinor);

/// <summary>One home match the snack bar served (`STRC-4`, `STRC-5`).</summary>
/// <param name="FixtureId">The match.</param>
/// <param name="Opponent">The visiting club's name.</param>
/// <param name="Spectators">The crowd.</param>
/// <param name="Result">The home side's result: <c>win</c>, <c>draw</c> or <c>loss</c>.</param>
/// <param name="GoalsFor">The home side's goals.</param>
/// <param name="GoalsAgainst">The visitors' goals.</param>
/// <param name="BonusBp">The result bonus applied, in basis points.</param>
/// <param name="BonusReason">Why: the result code, or <c>none</c>.</param>
/// <param name="TakingsMinor">What the snack bar took, in minor units.</param>
public sealed record StructureMatchResponse(
    Guid FixtureId,
    string Opponent,
    int Spectators,
    string Result,
    int GoalsFor,
    int GoalsAgainst,
    int BonusBp,
    string BonusReason,
    long TakingsMinor);

/// <summary>One of a club's buildings: its level, what it earns and costs, and its recent books (`STRC-1`…`STRC-9`).</summary>
/// <param name="Kind">The building, as a stable code: <c>snack_bar</c> or <c>merchandise</c>.</param>
/// <param name="Level">The level the building trades at now.</param>
/// <param name="MaxLevel">The highest level a building reaches.</param>
/// <param name="Version">The building's version, which is the strong entity tag an upgrade order is made against.</param>
/// <param name="SpendMinor">What one spectator (snack bar) or one fan per week (shop) spends at this level, in minor units.</param>
/// <param name="UpkeepWeeklyMinor">What the building costs to keep each week at this level, in minor units.</param>
/// <param name="Next">What the next level would change, or null at the top level.</param>
/// <param name="Building">The upgrade under way, or null.</param>
/// <param name="Bonuses">The bonus table, with the line that applies marked.</param>
/// <param name="RecentWeeks">The last eight weeks the building traded, newest first.</param>
/// <param name="RecentMatches">The snack bar's recent home matches, newest first. Empty for the shop.</param>
/// <param name="SeasonTakingsMinor">What the building has taken this season, in minor units.</param>
/// <param name="SeasonUpkeepMinor">What it has cost to keep this season, in minor units, as a positive amount.</param>
public sealed record StructureResponse(
    string Kind,
    int Level,
    int MaxLevel,
    long Version,
    long SpendMinor,
    long UpkeepWeeklyMinor,
    StructureNextLevelResponse? Next,
    StructureBuildingResponse? Building,
    IReadOnlyList<StructureBonusResponse> Bonuses,
    IReadOnlyList<StructureWeekResponse> RecentWeeks,
    IReadOnlyList<StructureMatchResponse> RecentMatches,
    long SeasonTakingsMinor,
    long SeasonUpkeepMinor);

/// <summary>A club's snack bar and merchandise shop (`STRC-1`…`STRC-9`).</summary>
/// <param name="AvailableCashMinor">What the club can spend on an upgrade, after reservations (`FIN-10`), in minor units.</param>
/// <param name="SnackBar">The snack bar.</param>
/// <param name="Merchandise">The merchandise shop.</param>
/// <param name="ServerTime">The server's current instant.</param>
public sealed record StructuresResponse(
    long AvailableCashMinor,
    StructureResponse SnackBar,
    StructureResponse Merchandise,
    DateTimeOffset ServerTime);
