using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World;
using TouchlineManager.MatchEngine.Highlights;

namespace TouchlineManager.Application.World;

/// <summary>
/// Maps a club's stadium to its transport shape (`STAD-1`…`STAD-6`).
/// </summary>
/// <remarks>
/// The prices, the build costs and the expected crowd are derived here from the club's tier rather than
/// stored on the ground, because they are rule-set values and the rule set is where a balancing change is
/// made (`RULE-1`). The crowd is quoted at mid-table form so the figure is steady from one match to the next;
/// the gate a fixture actually draws moves with the league position it is priced against (`FIN-3`).
/// </remarks>
public static class StadiumMapping
{
    /// <summary>The league position a quoted crowd and gate assume: the middle of an 18-club table.</summary>
    private const int QuotedFormRank = 9;

    /// <summary>Projects a ground for the stadium page.</summary>
    /// <param name="stadium">The ground.</param>
    /// <param name="context">The club's tier and money.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static StadiumResponse ToResponse(
        this ClubStadium stadium,
        ClubStadiumContext context,
        DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(stadium);
        ArgumentNullException.ThrowIfNull(context);

        var tier = context.TierNumber;
        var demand = StadiumRuleSet.MatchdayDemandFor(tier, QuotedFormRank);
        var capacity = stadium.Capacity;

        var stands = StadiumStands.All
            .Select(stand => new StadiumStandResponse(
                stand.ToCode(),
                stadium.Seats.Of(stand),
                StadiumRuleSet.TicketPriceMinorFor(stand, tier),
                StadiumRuleSet.BuildCostMinorFor(stand, tier),
                StadiumRuleSet.SoldFor(stand, stadium.Seats.Of(stand), demand)))
            .ToList();

        var level = stadium.Level;
        var colours = ClubPalette.Resolve(stadium.ClubId, context.PrimaryColour, context.SecondaryColour);

        return new StadiumResponse(
            stadium.ClubId,
            level,
            StadiumRuleSet.MaxLevel,
            capacity,
            StadiumRuleSet.MaxCapacity,
            StadiumRuleSet.SeatsPerLevel,
            level == StadiumRuleSet.MaxLevel ? 0 : (level * StadiumRuleSet.SeatsPerLevel) - capacity + 1,
            colours.Primary,
            colours.Secondary,
            stands,
            demand,
            StadiumRuleSet.FullHouseMinorFor(stadium.Seats, tier),
            StadiumRuleSet.GateRevenueMinorFor(stadium.Seats, tier, QuotedFormRank),
            context.CashMinor - context.ReservedMinor,
            stadium.Version,
            serverTime);
    }
}
