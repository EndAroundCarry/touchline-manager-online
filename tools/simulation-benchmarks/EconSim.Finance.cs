using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.SimulationBenchmarks;

// The money rules of economy v2 that are not formulas: the sponsor and the objective, the state of a club's cash, the board's sales in administration
// and the computer clubs' building (plan toasty-scott sections 3, 6 and 7).
internal sealed partial class EconSim
{
    /// <summary>
    /// At the start of a season every club signs a sponsor and picks an objective. A manager does it by the rules of his script, a computer club by
    /// its temperament and the board's realistic objective (<c>ai-finance-v1</c>). The offers are made on the club's fans and standing now.
    /// </summary>
    private void SignSponsors()
    {
        foreach (var club in Clubs)
        {
            var (kind, objective) = club.Managed
                ? club.Manager!.ChooseSponsor(this, club)
                : (AiFinancePolicy.ChooseOffer(club.Temperament), AiFinancePolicy.ChooseObjective(club.TargetRank));
            var points = ClubStanding.PointsOf(club.StandingCentis);

            club.SponsorKind = kind;
            club.Objective = objective;
            club.SponsorFans = club.Fans.Fans;
            club.SponsorStanding = points;
            club.SponsorFixedSeason = EconomyRuleSet.SponsorFixedSeasonMinorFor(kind, club.Tier, club.SponsorFans, points);
            club.HasSponsor = true;
            club.Forecast = ForecastSeasonIncome(club);
        }
    }

    /// <summary>Reads a club's cash after the matchday's settlement and moves its state; in administration the board sells the highest earners.</summary>
    private void ReadFinancialState(SimClub club)
    {
        var books = club.Books;
        var limit = EconomyRuleSet.OverdraftLimitMinorFor(club.Forecast);
        var now = ((Season - 1) * Weeks) + Week + 1;
        var step = ClubFinancialState.Next(club.FinState, club.FinSince, now, club.Cash, limit);

        books.EnteredRed += step.Transition == FinancialTransition.EnteredRed ? 1 : 0;
        books.EnteredAdmin += step.Transition == FinancialTransition.EnteredAdministration ? 1 : 0;
        club.FinState = step.State;
        club.FinSince = step.SinceMatchday;

        if (step.State != FinancialStateKind.Healthy)
        {
            books.RedMatchdays++;
        }

        if (step.EmergencyGrantMinor > 0)
        {
            club.Cash += step.EmergencyGrantMinor;
            club.GrantsTotal += step.EmergencyGrantMinor;
            books.Grants += step.EmergencyGrantMinor;
        }

        if (step.State == FinancialStateKind.Administration)
        {
            books.AdminMatchdays++;
            BoardSells(club);

            if (step.Dismiss && club.Managed)
            {
                club.Dismissed = true;
                books.Dismissals++;
                Note(club, "manager dismissed after 17 matchdays in administration");
            }
        }
    }

    private void BoardSells(SimClub club)
    {
        var squad = club.Squad
            .Select(player => new AdministrationPlayer(player.Guid, player.Family, player.Wage, EconRules.Value(player.AbilityTenths, player.AgeIn(GameYear)), player.Listed))
            .ToList();

        foreach (var sale in ClubFinancialState.ChooseSales(squad, club.WageBill, EconomyRuleSet.WageBudgetMinorFor(club.Forecast)))
        {
            var player = club.Squad.First(candidate => candidate.Guid == sale.PlayerId);

            if (ListPlayer(club, player, sale.AskingMinor, 2) is not null)
            {
                player.BoardListed = true;
                club.Books.BoardSales++;
            }
        }
    }

    /// <summary>A computer club adds places when its ground sells out and upgrades a building that repays quickly (<c>ai-finance-v1</c>).</summary>
    private static void AiGrowth(SimClub club)
    {
        var input = new AiBuildingInput(
            club.Tier,
            club.Seats,
            club.Fans.Fans,
            club.Fans.MoodBp,
            [.. club.RecentFill.Select(fill => (int)(Math.Min(1.0, fill) * 10_000))],
            club.Available,
            club.WageBill,
            club.FinState != FinancialStateKind.Healthy,
            club.SnackLevel,
            club.MerchLevel);

        foreach (var order in AiFinancePolicy.ChooseStands(input))
        {
            BuildPlaces(club, order.Stand, order.Places);
        }

        foreach (var upgrade in AiFinancePolicy.ChooseUpgrades(input))
        {
            UpgradeStructure(club, snackBar: !upgrade.Shop);
        }
    }

    /// <summary>The money a scenario hands a club (a spender's fee): booked, so the books still explain the cash. Not a rule of the game.</summary>
    public static void Gift(SimClub club, long amount)
    {
        club.Cash += amount;
        club.Books.Gifts += amount;
    }
}
