using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.SimulationBenchmarks;

// The scripted managers (plan toasty-scott, Milestone 0): a few simple, repeatable ways of playing the money game, each on the median club of a
// tier. They use the same tools a manager has (listings, bids, the ground, the buildings, the squad) and the same fixed rules as every AI club. The
// two exceptions are marked: the spenders buy their stars straight from an AI club (so the economy is tested and not the market's supply), and the
// trader knows every player's hidden ceiling (a perfect scout).

internal abstract class EconManager
{
    public abstract string Name { get; }

    public virtual void OnSeasonStart(EconSim sim, SimClub club)
    {
    }

    /// <summary>How much stronger than its squad the club plays, in hundredths of a strength point: a stand-in for a manager who builds a great squad.</summary>
    public virtual int StrengthBonus => 0;

    /// <summary>The offer and the objective the manager signs in the first week: by default the balanced offer at the board's realistic objective.</summary>
    public virtual (SponsorOfferKind Kind, SeasonObjective Objective) ChooseSponsor(EconSim sim, SimClub club) =>
        (SponsorOfferKind.Balanced, EconomyRuleSet.RealisticObjectiveFor(club.TargetRank));

    /// <summary>Called three times a week, before the AI clubs' evaluation.</summary>
    public virtual void Act(EconSim sim, SimClub club, int week, int round)
    {
    }

    /// <summary>Called after the matchday and the Sunday finance run.</summary>
    public virtual void OnWeekEnd(EconSim sim, SimClub club, int week)
    {
    }

    public virtual void OnSeasonEnd(EconSim sim, SimClub club)
    {
    }

    public virtual void OnSigned(EconSim sim, SimClub club, SimPlayer player, long fee, SimListing? listing)
    {
    }
}

/// <summary>What a sensible manager does with a surplus: build where the crowd is turned away, and upgrade a building that repays quickly.</summary>
internal static class Growth
{
    public static void Apply(EconSim sim, SimClub club)
    {
        var reserve = AiFinancePolicy.ReserveMinorFor(club.WageBill);
        var surplus = club.Available - reserve;

        if (surplus <= 0)
        {
            return;
        }

        if (club.RecentFill.Count >= 5 && club.RecentFill.Average() >= 0.95 && club.Seats.Total < StadiumRuleSet.MaxCapacity)
        {
            var demand = StadiumRuleSet.MatchdayDemandFor(club.Fans.Fans, club.Fans.MoodBp);
            var wanted = Math.Max(demand - club.Seats.Total, club.Seats.Total / 20);
            var budget = surplus / 2;
            var cost = AveragePlaceCost(sim, club);
            var places = cost <= 0 ? wanted : (int)Math.Min(wanted, budget / cost);

            foreach (var stand in StadiumStands.All)
            {
                EconSim.BuildPlaces(club, stand, places * StadiumRuleSet.DemandShareBp(stand) / 10_000);
            }
        }

        UpgradeBuildings(sim, club, reserve, repaysWithinSettlements: EconSim.Weeks / 2);
    }

    /// <summary>What a place costs on average when they are built in the proportions the crowd asks for.</summary>
    public static long AveragePlaceCost(EconSim sim, SimClub club) =>
        StadiumStands.All.Sum(stand => (long)StadiumRuleSet.DemandShareBp(stand) * EconRules.BuildCostPerPlace(stand, club.Tier)) / 10_000;

    public static void UpgradeBuildings(EconSim sim, SimClub club, long reserve, int repaysWithinSettlements)
    {
        var spectators = EconRules.Attendance(club.Seats, club.Fans.Fans, club.Fans.MoodBp);
        var word = FanMoods.WordFor(club.Fans.MoodBp);

        if (club.SnackLevel < StructureRuleSet.MaxLevel)
        {
            var gain = ((EconRules.SnackTakings(club.SnackLevel + 1, spectators, 0, club.Tier) - EconRules.SnackTakings(club.SnackLevel, spectators, 0, club.Tier)) * (EconSim.Weeks / 2))
                - ((EconRules.Upkeep(club.SnackLevel + 1, club.Tier) - EconRules.Upkeep(club.SnackLevel, club.Tier)) * EconSim.Weeks);
            var cost = EconRules.UpgradeCost(club.SnackLevel + 1, club.Tier);

            if (gain > 0 && cost <= gain * repaysWithinSettlements / EconSim.Weeks && club.Available - cost >= reserve)
            {
                EconSim.UpgradeStructure(club, snackBar: true);
            }
        }

        if (club.MerchLevel < StructureRuleSet.MaxLevel)
        {
            var gain = ((EconRules.MerchTakings(club.MerchLevel + 1, club.Fans.Fans, word, club.Tier) - EconRules.MerchTakings(club.MerchLevel, club.Fans.Fans, word, club.Tier)) * EconSim.Weeks)
                - ((EconRules.Upkeep(club.MerchLevel + 1, club.Tier) - EconRules.Upkeep(club.MerchLevel, club.Tier)) * EconSim.Weeks);
            var cost = EconRules.UpgradeCost(club.MerchLevel + 1, club.Tier);

            if (gain > 0 && cost <= gain * repaysWithinSettlements / EconSim.Weeks && club.Available - cost >= reserve)
            {
                EconSim.UpgradeStructure(club, snackBar: false);
            }
        }
    }
}

/// <summary>Keeps wages inside 70% of the income forecast, signs the best affordable improvement, and spends a surplus on the ground and the buildings.</summary>
internal sealed class PrudentBuilder(string name) : EconManager
{
    public const int WageBudgetBp = 7_000;

    public override string Name => name;

    public override void Act(EconSim sim, SimClub club, int week, int round)
    {
        var bill = club.WageBill;

        // Trim a swollen squad: the weakest are put on the market.
        if (club.Squad.Count > 24 && !club.Squad.Any(player => player.Listed))
        {
            var surplus = club.Squad.Where(player => !player.IsGoalkeeper).OrderBy(player => player.Ability).ThenBy(player => player.Id).First();

            sim.ListPlayer(club, surplus, EconRules.Value(surplus.AbilityTenths, surplus.AgeIn(sim.GameYear)), 2);
        }

        if (club.Squad.Count >= 25 || sim.Listings.Count(listing => listing.LeadingClub == club) >= 2)
        {
            return;
        }

        var budget = EconomyRuleSet.WageBudgetMinorFor(club.Forecast);
        var reserve = bill / 4;
        SimListing? best = null;
        var bestGain = 0;
        var bestFee = 0L;

        foreach (var listing in sim.Listings.Where(listing => listing.Seller != club && listing.LeadingClub != club))
        {
            var player = listing.Player;
            var fee = EconSim.MinimumBid(listing);
            var weakest = club.Squad.Where(other => other.Family == player.Family).Select(other => other.Ability).DefaultIfEmpty(0).Min();
            var cap = WorldRuleSet.AiMarketFamilyCap(player.Family);
            var gain = club.Squad.Count(other => other.Family == player.Family) < cap ? player.Ability : player.Ability - weakest;

            if (gain < 2 || bill + listing.BuyerWage > budget || fee > club.Available - reserve)
            {
                continue;
            }

            if (best is null || gain > bestGain || (gain == bestGain && fee < bestFee))
            {
                best = listing;
                bestGain = gain;
                bestFee = fee;
            }
        }

        if (best is not null)
        {
            EconSim.PlaceBid(club, best, bestFee);
        }
    }

    public override void OnWeekEnd(EconSim sim, SimClub club, int week) => Growth.Apply(sim, club);
}

/// <summary>
/// A dominant club: the prudent builder's money habits with a squad that wins more than its players say. It stands in for a manager who has built a
/// great side, so the economy can be read where the plan expects the dominant clubs to be.
/// </summary>
internal sealed class Dynasty : EconManager
{
    private readonly PrudentBuilder _inner = new("Dynasty");

    public override string Name => "Dynasty";

    public override int StrengthBonus => 250;

    public override (SponsorOfferKind Kind, SeasonObjective Objective) ChooseSponsor(EconSim sim, SimClub club) =>
        (SponsorOfferKind.Performance, EconomyRuleSet.RealisticObjectiveFor(club.TargetRank));

    public override void Act(EconSim sim, SimClub club, int week, int round) => _inner.Act(sim, club, week, round);

    public override void OnWeekEnd(EconSim sim, SimClub club, int week) => _inner.OnWeekEnd(sim, club, week);
}

/// <summary>Buys young players whose ceiling is well above their skill, trains them, and sells them on at their peak. Knows every hidden ceiling.</summary>
internal sealed class Trader : EconManager
{
    private const int HeldMax = 3;

    public override string Name => "Trader";

    public override void Act(EconSim sim, SimClub club, int week, int round)
    {
        foreach (var player in club.Squad.Where(player => player.BoughtFee > 0 && !player.Listed).ToList())
        {
            var age = player.AgeIn(sim.GameYear);

            if (age >= 24 || player.Ability >= player.Potential - 1)
            {
                sim.ListPlayer(club, player, EconRules.Value(player.AbilityTenths, age) * 90 / 100, 2);
            }
        }

        var held = club.Squad.Count(player => player.BoughtFee > 0);

        if (held >= HeldMax || club.Squad.Count >= 26 || sim.Listings.Any(listing => listing.LeadingClub == club))
        {
            return;
        }

        var budget = EconomyRuleSet.WageBudgetMinorFor(club.Forecast);
        SimListing? best = null;
        var bestScore = 0.0;
        var bestFee = 0L;

        foreach (var listing in sim.Listings.Where(listing => listing.Seller != club))
        {
            var player = listing.Player;
            var fee = EconSim.MinimumBid(listing);
            var headroom = player.Potential - player.Ability;

            if (player.AgeIn(sim.GameYear) > 22 || headroom < 3 || player.Potential < 12 || fee > club.Available / 4 || club.WageBill + listing.BuyerWage > budget)
            {
                continue;
            }

            var score = headroom * 1_000_000.0 / (fee + listing.BuyerWage);

            if (score > bestScore)
            {
                best = listing;
                bestScore = score;
                bestFee = fee;
            }
        }

        if (best is not null)
        {
            EconSim.PlaceBid(club, best, bestFee);
        }
    }

    public override void OnSigned(EconSim sim, SimClub club, SimPlayer player, long fee, SimListing? listing)
    {
        player.BoughtFee = fee;
        player.BoughtFromPool = listing?.IsPool ?? false;
        player.BoughtSeason = sim.Season;
        player.BoughtAbility = player.Ability;
        player.BoughtPotential = player.Potential;
        player.WagesSinceBought = 0;
    }

    public override void OnWeekEnd(EconSim sim, SimClub club, int week) => Growth.Apply(sim, club);
}

/// <summary>Spends every penny at once: a bigger ground and bigger buildings than the crowd needs, and the best stars the cash will buy.</summary>
internal sealed class OverAmbitious : EconManager
{
    public override string Name => "Over-ambitious";

    public override (SponsorOfferKind Kind, SeasonObjective Objective) ChooseSponsor(EconSim sim, SimClub club) =>
        (SponsorOfferKind.Performance, SeasonObjective.WinLeague);

    public override void Act(EconSim sim, SimClub club, int week, int round)
    {
        if (round != 0 || week % 4 != 0)
        {
            return;
        }

        if (club.Squad.Count(player => player.Ability >= 15) < 3 && club.Squad.Count < 26)
        {
            sim.Poach(club, minAbility: 15, maxAge: 29, maxFee: club.Available);
        }
    }

    public override void OnWeekEnd(EconSim sim, SimClub club, int week)
    {
        // The ground first, to level 5, then the buildings, then the stars again.
        var spend = club.Available * 9 / 10;

        if (spend <= 0)
        {
            return;
        }

        if (club.Seats.Total < 24_900)
        {
            var cost = Growth.AveragePlaceCost(sim, club);
            var places = cost <= 0 ? 25_000 - club.Seats.Total : (int)Math.Min(25_000 - club.Seats.Total, spend / cost);

            foreach (var stand in StadiumStands.All)
            {
                EconSim.BuildPlaces(club, stand, places * StadiumRuleSet.DemandShareBp(stand) / 10_000);
            }

            return;
        }

        while (club.SnackLevel < 5 && EconSim.UpgradeStructure(club, snackBar: true))
        {
        }

        while (club.MerchLevel < 5 && EconSim.UpgradeStructure(club, snackBar: false))
        {
        }
    }
}

/// <summary>Keeps a number of stars (15 or better) in the squad whatever the wage bill, buying them straight from the AI clubs.</summary>
internal sealed class BigSpender(string name, int stars) : EconManager
{
    public override string Name => name;

    public int Stars => stars;

    public override void Act(EconSim sim, SimClub club, int week, int round)
    {
        if (round != 0 || week % 2 != 0)
        {
            return;
        }

        if (club.Squad.Count(player => player.Ability >= 15) < stars && club.Squad.Count < 26)
        {
            // The best the cash buys, within a fee that is not above a fifth of a star's worth more than the cash needs.
            // The fee is the scenario's to pay: the test is what the wages do to a club, not whether it can find the cash for a star.
            sim.Poach(club, minAbility: 15, maxAge: 29, maxFee: long.MaxValue, endow: true);
        }
    }

    public override void OnSigned(EconSim sim, SimClub club, SimPlayer player, long fee, SimListing? listing) =>
        sim.Note(club, $"signed {sim.Describe(player)} for {fee / 100:N0} on {player.Wage / 100:N0} a season");
}
