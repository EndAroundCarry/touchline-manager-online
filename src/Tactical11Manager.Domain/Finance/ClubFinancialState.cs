using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Domain.Finance;

/// <summary>Where a club's money stands (`DEBT-*`, plan toasty-scott section 6).</summary>
public enum FinancialStateKind
{
    /// <summary>Cash is at or above zero.</summary>
    Healthy = 0,

    /// <summary>Cash is below zero but within the overdraft limit: no bids, no building.</summary>
    InTheRed = 1,

    /// <summary>Cash is below the overdraft limit: the board lists the highest earners, and a manager who stays here is dismissed.</summary>
    Administration = 2,
}

/// <summary>What changed in a club's financial state at a settlement.</summary>
public enum FinancialTransition
{
    /// <summary>Nothing changed.</summary>
    None = 0,

    /// <summary>The club went from healthy into the red.</summary>
    EnteredRed = 1,

    /// <summary>The club came back to zero or above.</summary>
    LeftRed = 2,

    /// <summary>The club passed its overdraft limit.</summary>
    EnteredAdministration = 3,

    /// <summary>The club came back inside its overdraft limit and is only in the red.</summary>
    LeftAdministration = 4,
}

/// <summary>The result of one reading of a club's money.</summary>
/// <param name="State">The state now.</param>
/// <param name="SinceMatchday">The matchday ordinal the club has been in this state since, as the caller counts them.</param>
/// <param name="Transition">What changed.</param>
/// <param name="Dismiss">Whether the manager is dismissed: still in administration after <see cref="EconomyRuleSet.AdministrationDismissalMatchdays"/> matchdays.</param>
/// <param name="EmergencyGrantMinor">What the integrity net must grant to bring the cash back to its floor; 0 in the normal case.</param>
public sealed record FinancialStep(
    FinancialStateKind State,
    int SinceMatchday,
    FinancialTransition Transition,
    bool Dismiss,
    long EmergencyGrantMinor);

/// <summary>A player the board puts on the market while a club is in administration.</summary>
/// <param name="PlayerId">The player.</param>
/// <param name="AskingMinor">The price asked: 80% of his value.</param>
public sealed record AdministrationListing(Guid PlayerId, long AskingMinor);

/// <summary>One player of the squad as the board reads him.</summary>
/// <param name="PlayerId">The player.</param>
/// <param name="Family">His position family.</param>
/// <param name="SeasonWageMinor">His wage for a season.</param>
/// <param name="ValueMinor">His value.</param>
/// <param name="IsListed">Whether he is already on the market.</param>
public sealed record AdministrationPlayer(Guid PlayerId, PositionFamily Family, long SeasonWageMinor, long ValueMinor, bool IsListed);

/// <summary>
/// The transitions between healthy, in the red and administration, and what the board sells (`DEBT-*`).
/// </summary>
/// <remarks>
/// Cash may go below zero only through the recurring costs (wages, the operating cost, upkeep); a fee, a building and a release still need the
/// cash. The thresholds are the overdraft limit (a quarter of the season income forecast) and, as the integrity net, twice that
/// (`FIN-16`). There is no interest and no effect on the fans' mood.
/// </remarks>
public static class ClubFinancialState
{
    /// <summary>Reads a club's money and moves its state.</summary>
    /// <param name="current">The state the club was in.</param>
    /// <param name="sinceMatchday">The matchday ordinal the club has been in <paramref name="current"/> since.</param>
    /// <param name="nowMatchday">The matchday ordinal now.</param>
    /// <param name="cashMinor">The club's cash.</param>
    /// <param name="overdraftLimitMinor">The overdraft limit.</param>
    public static FinancialStep Next(FinancialStateKind current, int sinceMatchday, int nowMatchday, long cashMinor, long overdraftLimitMinor)
    {
        var state = cashMinor >= 0
            ? FinancialStateKind.Healthy
            : cashMinor < -overdraftLimitMinor
                ? FinancialStateKind.Administration
                : FinancialStateKind.InTheRed;

        var transition = (current, state) switch
        {
            (FinancialStateKind.Healthy, FinancialStateKind.InTheRed) => FinancialTransition.EnteredRed,
            (FinancialStateKind.Healthy, FinancialStateKind.Administration) => FinancialTransition.EnteredAdministration,
            (FinancialStateKind.InTheRed, FinancialStateKind.Administration) => FinancialTransition.EnteredAdministration,
            (FinancialStateKind.InTheRed, FinancialStateKind.Healthy) => FinancialTransition.LeftRed,
            (FinancialStateKind.Administration, FinancialStateKind.InTheRed) => FinancialTransition.LeftAdministration,
            (FinancialStateKind.Administration, FinancialStateKind.Healthy) => FinancialTransition.LeftRed,
            _ => FinancialTransition.None,
        };

        var since = state == current ? sinceMatchday : nowMatchday;
        var dismiss = state == FinancialStateKind.Administration
            && nowMatchday - since >= EconomyRuleSet.AdministrationDismissalMatchdays;
        var floor = EconomyRuleSet.SafetyNetFloorMinor(overdraftLimitMinor);

        return new FinancialStep(state, since, transition, dismiss, cashMinor < floor ? floor - cashMinor : 0);
    }

    /// <summary>
    /// The players the board lists to bring the wage bill back under the budget: the highest earners first, enough to get under, at most five,
    /// never leaving fewer than the legal squad or fewer than two goalkeepers, at 80% of their value. A player nobody buys is released and his agent
    /// pays the club 30% of his value (<see cref="EconomyRuleSet.AdministrationReleaseFeeMinor"/>).
    /// </summary>
    /// <param name="squad">The registered squad.</param>
    /// <param name="wageBillSeasonMinor">The season wage bill.</param>
    /// <param name="wageBudgetMinor">The budget the bill is held against.</param>
    public static IReadOnlyList<AdministrationListing> ChooseSales(
        IReadOnlyList<AdministrationPlayer> squad,
        long wageBillSeasonMinor,
        long wageBudgetMinor)
    {
        ArgumentNullException.ThrowIfNull(squad);

        var listings = new List<AdministrationListing>();
        var remaining = squad.Count;
        var goalkeepers = squad.Count(player => player.Family == PositionFamily.Goalkeeper);
        var bill = wageBillSeasonMinor;

        // Players already on the market leave the squad when they sell, so their wages come off the bill already counted.
        foreach (var player in squad.Where(player => player.IsListed))
        {
            bill -= player.SeasonWageMinor;
            remaining--;
            goalkeepers -= player.Family == PositionFamily.Goalkeeper ? 1 : 0;
        }

        foreach (var player in squad.Where(player => !player.IsListed).OrderByDescending(player => player.SeasonWageMinor).ThenBy(player => player.PlayerId))
        {
            if (bill <= wageBudgetMinor || listings.Count >= EconomyRuleSet.AdministrationMaxListings)
            {
                break;
            }

            var keeper = player.Family == PositionFamily.Goalkeeper;

            if (remaining - 1 < WorldRuleSet.SquadMinimumRegistered || (keeper && goalkeepers - 1 < WorldRuleSet.MinimumGoalkeepers))
            {
                continue;
            }

            listings.Add(new AdministrationListing(
                player.PlayerId,
                Math.Max(1, player.ValueMinor * EconomyRuleSet.AdministrationAskingBp / EconomyRuleSet.OneHundredPercentBp)));

            bill -= player.SeasonWageMinor;
            remaining--;
            goalkeepers -= keeper ? 1 : 0;
        }

        return listings;
    }
}
