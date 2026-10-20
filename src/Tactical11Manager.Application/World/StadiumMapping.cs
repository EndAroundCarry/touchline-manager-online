using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Contracts.World;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;
using Tactical11Manager.MatchEngine.Highlights;

namespace Tactical11Manager.Application.World;

/// <summary>
/// Maps a club's stadium to its transport shape (`STAD-1`…`STAD-6`).
/// </summary>
/// <remarks>
/// The prices, the build costs and the expected crowd are derived here from the club's tier rather than
/// stored on the ground, because they are rule-set values and the rule set is where a balancing change is
/// made (`RULE-1`). The crowd is quoted from the club's fans and their mood as they stand now, which is the
/// crowd the next home match draws if nothing moves them before it (`FIN-3`, `STAD-5`).
/// </remarks>
public static class StadiumMapping
{
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
        var demand = StadiumRuleSet.MatchdayDemandFor(context.Fans, context.MoodBp);
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
            StadiumRuleSet.GateRevenueMinorFor(stadium.Seats, tier, context.Fans, context.MoodBp),
            context.CashMinor - context.ReservedMinor,
            stadium.Version,
            serverTime);
    }
}
