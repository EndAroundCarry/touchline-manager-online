using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Contracts.World;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.World;

/// <summary>
/// Puts a club's two buildings, its purse and their books into the response the structures page reads
/// (`STRC-1`…`STRC-9`).
/// </summary>
/// <remarks>
/// <para>
/// The spend, the upkeep, the cost of the next level and the bonus tables are derived here from the rule set
/// rather than stored on the building, because they are rule-set values and the rule set is where a
/// retune is made (`RULE-1`, `STRC-9`).
/// </para>
/// <para>
/// The quote for the next level uses the crowd and the fans as they stand now: the snack bar the crowd the
/// ground would draw, the shop the fans and their mood. It leaves the result bonus out, because that depends
/// on a match not yet played, so it is a cautious estimate and never a promise.
/// </para>
/// </remarks>
public sealed class StructuresComposer
{
    /// <summary>How many weeks of books the page shows.</summary>
    public const int RecentWeeks = 8;

    /// <summary>How many home matches the snack bar's page shows.</summary>
    public const int RecentMatches = 8;

    private readonly IStructureRepository _structures;
    private readonly IStadiumRepository _stadiums;
    private readonly IStadiumQueries _context;
    private readonly IStructureQueries _history;
    private readonly IClock _clock;

    /// <summary>Initializes the composer.</summary>
    public StructuresComposer(
        IStructureRepository structures,
        IStadiumRepository stadiums,
        IStadiumQueries context,
        IStructureQueries history,
        IClock clock)
    {
        _structures = structures;
        _stadiums = stadiums;
        _context = context;
        _history = history;
        _clock = clock;
    }

    /// <summary>Composes the response for a club.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response, or null when the club has no ground, account or building (a defect in generation).</returns>
    public async Task<StructuresResponse?> ComposeAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var buildings = await _structures.ListByClubAsync(clubId, cancellationToken);
        var stadium = await _stadiums.FindByClubAsync(clubId, cancellationToken);
        var context = await _context.GetContextAsync(clubId, cancellationToken);

        var snackBar = buildings.FirstOrDefault(building => building.Kind == StructureKind.SnackBar);
        var merchandise = buildings.FirstOrDefault(building => building.Kind == StructureKind.Merchandise);

        if (stadium is null || context is null || snackBar is null || merchandise is null)
        {
            return null;
        }

        var snackHistory = await _history.GetHistoryAsync(
            clubId, StructureKind.SnackBar, RecentWeeks, RecentMatches, cancellationToken);
        var merchandiseHistory = await _history.GetHistoryAsync(
            clubId, StructureKind.Merchandise, RecentWeeks, RecentMatches, cancellationToken);

        var spectators = StadiumRuleSet.AttendanceFor(stadium.Seats, context.Fans, context.MoodBp);
        var mood = FanMoods.WordFor(context.MoodBp);

        return new StructuresResponse(
            context.CashMinor - context.ReservedMinor,
            Describe(
                snackBar,
                context.TierNumber,
                level => StructureRuleSet.SnackWeeklyEstimateMinorFor(level, spectators),
                SnackBonuses(snackHistory),
                snackHistory),
            Describe(
                merchandise,
                context.TierNumber,
                level => StructureRuleSet.MerchWeeklyEstimateMinorFor(level, context.Fans, mood),
                MerchandiseBonuses(mood),
                merchandiseHistory),
            _clock.UtcNow);
    }

    private static StructureResponse Describe(
        ClubStructure building,
        int tier,
        Func<int, long> weeklyTakingsAt,
        IReadOnlyList<StructureBonusResponse> bonuses,
        StructureHistory history)
    {
        var level = building.Level;
        var spend = building.Kind == StructureKind.SnackBar
            ? StructureRuleSet.SnackSpendMinor(level)
            : StructureRuleSet.MerchSpendMinor(level);
        var upkeep = StructureRuleSet.UpkeepMinorFor(level, tier);

        StructureNextLevelResponse? next = null;

        if (!building.IsAtMaxLevel)
        {
            var target = level + 1;
            var nextUpkeep = StructureRuleSet.UpkeepMinorFor(target, tier);
            var profitChange = weeklyTakingsAt(target) - weeklyTakingsAt(level) - (nextUpkeep - upkeep);

            next = new StructureNextLevelResponse(
                target,
                building.Kind == StructureKind.SnackBar
                    ? StructureRuleSet.SnackSpendMinor(target)
                    : StructureRuleSet.MerchSpendMinor(target),
                nextUpkeep,
                StructureRuleSet.UpgradeCostMinorFor(target, tier),
                StructureRuleSet.BuildDaysFor(target),
                profitChange);
        }

        var underway = building is { UpgradeTargetLevel: { } targetLevel, UpgradeDueAt: { } dueAt }
            ? new StructureBuildingResponse(targetLevel, dueAt)
            : null;

        return new StructureResponse(
            building.Kind.ToCode(),
            level,
            StructureRuleSet.MaxLevel,
            building.Version,
            spend,
            upkeep,
            next,
            underway,
            bonuses,
            history.Weeks
                .Select(week => new StructureWeekResponse(
                    week.WeekOf, week.TakingsMinor, week.UpkeepMinor, week.TakingsMinor - week.UpkeepMinor))
                .ToList(),
            history.Matches
                .Select(match => new StructureMatchResponse(
                    match.FixtureId,
                    match.Opponent,
                    match.Spectators,
                    match.GoalsFor > match.GoalsAgainst ? "win" : match.GoalsFor == match.GoalsAgainst ? "draw" : "loss",
                    match.GoalsFor,
                    match.GoalsAgainst,
                    match.BonusBp,
                    match.BonusReason,
                    match.TakingsMinor))
                .ToList(),
            history.SeasonTakingsMinor,
            history.SeasonUpkeepMinor);
    }

    // The snack bar's table is the results that earn a bonus; the line that applied to the last home match is
    // marked, and a last match that earned nothing marks none.
    private static IReadOnlyList<StructureBonusResponse> SnackBonuses(StructureHistory history)
    {
        var last = history.Matches.Count > 0 ? history.Matches[0].BonusReason : null;

        return
        [
            new(StructureRuleSet.UnderdogWinReason, StructureRuleSet.UnderdogWinBonusBp, last == StructureRuleSet.UnderdogWinReason),
            new(StructureRuleSet.EvenWinReason, StructureRuleSet.EvenWinBonusBp, last == StructureRuleSet.EvenWinReason),
            new(StructureRuleSet.FavouriteWinReason, StructureRuleSet.FavouriteWinBonusBp, last == StructureRuleSet.FavouriteWinReason),
            new(StructureRuleSet.UnderdogDrawReason, StructureRuleSet.UnderdogDrawBonusBp, last == StructureRuleSet.UnderdogDrawReason),
        ];
    }

    private static IReadOnlyList<StructureBonusResponse> MerchandiseBonuses(FanMoodWord current) =>
        [.. Enum.GetValues<FanMoodWord>()
            .Select(word => new StructureBonusResponse(
                FanMoods.Code(word), StructureRuleSet.MoodBonusBpFor(word), word == current))];
}
