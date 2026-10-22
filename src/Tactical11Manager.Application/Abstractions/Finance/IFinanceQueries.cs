using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.Abstractions.Finance;

/// <summary>
/// One club's basis for gate revenue: the places in its ground, the tier it plays in, and its fans (`FIN-3`, `STAD-5`).
/// </summary>
/// <remarks>
/// The ground says how many people the club can seat, the fans and their mood say how many want to come, and
/// the tier says what they pay, so the three are read together: a second read that must agree with the first
/// is a second read that can disagree. The fans are read as they stand going into the round, because the
/// crowd came before the result moved them.
/// </remarks>
/// <param name="ClubId">The club.</param>
/// <param name="Seats">The places in the club's ground.</param>
/// <param name="TierNumber">The tier the club plays in, 1 and up.</param>
/// <param name="Fans">The club's fans. A club with no fan base yet reads as the tier's opening fans.</param>
/// <param name="MoodBp">The fans' mood, 0 to 10,000. A club with no fan base yet reads as calm.</param>
/// <param name="SnackBarLevel">The snack bar's level, which prices each spectator's spend (`STRC-4`). A club with no snack bar yet reads as level 1.</param>
public sealed record ClubRevenueBasis(
    Guid ClubId,
    StadiumSeats Seats,
    int TierNumber,
    int Fans,
    int MoodBp,
    int SnackBarLevel = StructureRuleSet.OpeningLevel);

/// <summary>
/// One club's obligations for a week's finance run: the tier it plays in, its ground and its buildings (`FIN-9`, `STRC-8`, `STAD-7`).
/// </summary>
/// <remarks>
/// The wages and the sponsor's payment are not here: they are paid on every matchday, with the league's television money, when the round is
/// published (`FIN-7`).
/// </remarks>
/// <param name="ClubId">The club.</param>
/// <param name="TierNumber">The tier the club plays in, which scales the operating cost.</param>
/// <param name="Seats">The places in the club's ground, which set its upkeep (`STAD-7`).</param>
/// <param name="Fans">The club's fans at the week's close, which the merchandise shop sells to (`STRC-6`). A club with no fan base yet reads as the tier's opening fans.</param>
/// <param name="MoodBp">The fans' mood, 0 to 10,000, which lifts the merchandise takings (`STRC-7`). A club with no fan base yet reads as calm.</param>
/// <param name="SnackBarLevel">The snack bar's level, which sets its upkeep (`STRC-8`).</param>
/// <param name="MerchandiseLevel">The merchandise shop's level, which sets its spend and its upkeep (`STRC-6`, `STRC-8`).</param>
public sealed record ClubWeeklyObligations(
    Guid ClubId,
    int TierNumber,
    StadiumSeats Seats,
    int Fans,
    int MoodBp,
    int SnackBarLevel,
    int MerchandiseLevel);

/// <summary>
/// What a club's money is planned from this season: its tier, ground, fans, buildings, sponsor deal and payroll (plan toasty-scott section 5).
/// </summary>
/// <param name="ClubId">The club.</param>
/// <param name="TierNumber">The tier the club plays in.</param>
/// <param name="Seats">The places in the club's ground.</param>
/// <param name="Fans">The club's fans. A club with no fan base yet reads as the tier's opening fans.</param>
/// <param name="MoodBp">The fans' mood, 0 to 10,000. A club with no fan base yet reads as calm.</param>
/// <param name="SnackBarLevel">The snack bar's level.</param>
/// <param name="MerchandiseLevel">The merchandise shop's level.</param>
/// <param name="SponsorFixedMatchdayMinor">What the club's sponsor deal pays on each matchday, 0 while no deal is signed.</param>
/// <param name="SeasonWageMinor">The active contracts' total wage for a season.</param>
/// <param name="ContractedPlayers">How many active contracts that total covers.</param>
public sealed record ClubFinanceBasis(
    Guid ClubId,
    int TierNumber,
    StadiumSeats Seats,
    int Fans,
    int MoodBp,
    int SnackBarLevel,
    int MerchandiseLevel,
    long SponsorFixedMatchdayMinor,
    long SeasonWageMinor,
    int ContractedPlayers)
{
    /// <summary>Gets the sponsor's fixed part for the whole season.</summary>
    public long SponsorFixedSeasonMinor => SponsorFixedMatchdayMinor * EconomyRuleSet.MatchdaysPerSeason;

    /// <summary>Gets the season income forecast at the club's present fans and ground (`BUD-1`).</summary>
    public long ForecastMinor => EconomyRuleSet.SeasonIncomeForecastMinor(
        TierNumber,
        SponsorFixedSeasonMinor,
        Seats,
        Fans,
        MoodBp,
        SnackBarLevel,
        MerchandiseLevel);

    /// <summary>Gets the wage bill the club should stay within (`BUD-2`).</summary>
    public long WageBudgetMinor => EconomyRuleSet.WageBudgetMinorFor(ForecastMinor);

    /// <summary>Gets how far into the red the club may go before it is in administration (`DEBT-1`).</summary>
    public long OverdraftLimitMinor => EconomyRuleSet.OverdraftLimitMinorFor(ForecastMinor);
}

/// <summary>One ledger category's net cash movement over a season.</summary>
/// <param name="Category">The category.</param>
/// <param name="AmountMinor">The signed net, in minor units.</param>
public sealed record FinanceCategoryTotal(LedgerCategory Category, long AmountMinor);

/// <summary>A club's money and its season so far, as the reporting read shapes it (`FIN-3`…`FIN-9`).</summary>
/// <param name="CashMinor">The club's cash.</param>
/// <param name="ReservedMinor">The part already committed to open bids.</param>
/// <param name="WeeklyWageMinor">The active contracts' weekly wage bill.</param>
/// <param name="ContractedPlayers">How many active contracts that covers.</param>
/// <param name="TierNumber">The tier the club plays in.</param>
/// <param name="SeasonLabel">The season the totals cover, or null before a season exists.</param>
/// <param name="Goalkeepers">How many of the contracted players are goalkeepers (`SQ-2`).</param>
/// <param name="ExpiringContracts">How many contracts end at the end of the current season (`CON-6`).</param>
/// <param name="Totals">The season's totals by category, in a stable order.</param>
public sealed record FinanceSummarySnapshot(
    long CashMinor,
    long ReservedMinor,
    long WeeklyWageMinor,
    int ContractedPlayers,
    int TierNumber,
    string? SeasonLabel,
    int Goalkeepers,
    int ExpiringContracts,
    IReadOnlyList<FinanceCategoryTotal> Totals);

/// <summary>One page of a club's ledger, newest first.</summary>
/// <param name="Entries">The page's entries, newest first.</param>
/// <param name="HasMore">Whether the ledger continues past this page.</param>
public sealed record FinanceLedgerPage(IReadOnlyList<LedgerEntry> Entries, bool HasMore);

/// <summary>
/// The finance module's read projections (master plan §6.8, §10.7).
/// </summary>
/// <remarks>
/// Separate from the write ports for the reason the module rules give (`MOD-3`): a screen or a workflow can
/// change without widening what a command can reach, and the reporting reads below are projections over the
/// ledger the write ports never need.
/// </remarks>
public interface IFinanceQueries
{
    /// <summary>
    /// Reads the gate-revenue basis of every club in the division a matchday belongs to (`FIN-3`).
    /// </summary>
    /// <param name="matchdayId">The matchday whose division is wanted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One row per club in the division, or empty when the matchday is unknown.</returns>
    Task<IReadOnlyList<ClubRevenueBasis>> GetMatchdayRevenueBasisAsync(
        Guid matchdayId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads every club's tier, ground and buildings for the running season's weekly run (`FIN-9`, `STRC-8`, `STAD-7`).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One row per club in the current season, or empty before a season exists.</returns>
    Task<IReadOnlyList<ClubWeeklyObligations>> GetWeeklyObligationsAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads what the given clubs' money is planned from this season: the forecast, the wage bill and the sponsor's payment.
    /// </summary>
    /// <param name="clubIds">The clubs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One row per club that plays in the current season, in no particular order.</returns>
    Task<IReadOnlyList<ClubFinanceBasis>> GetClubFinanceBasisAsync(
        IReadOnlyCollection<Guid> clubIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads a club's registered squad the way the board reads it when the club is in administration (`DEBT-4`).
    /// </summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AdministrationPlayer>> GetAdministrationSquadAsync(Guid clubId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads how many matchdays the world has settled so far, counted across seasons: the season's number minus one, times the season's
    /// matchdays, plus the latest round any division has published (`DEBT-2`).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ordinal, or 0 before a season exists.</returns>
    Task<int> GetSettledMatchdayOrdinalAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads a club's money and its season's totals by category for the finance screen (`FIN-3`…`FIN-9`).
    /// </summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The summary, or null when the club has no account.</returns>
    Task<FinanceSummarySnapshot?> GetFinanceSummaryAsync(Guid clubId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads one page of a club's ledger, newest first (`FIN-11`, `FIN-12`).
    /// </summary>
    /// <param name="clubId">The club.</param>
    /// <param name="beforeSequence">The exclusive upper bound, or null for the first page.</param>
    /// <param name="pageSize">How many entries to return at most.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<FinanceLedgerPage> GetFinanceLedgerAsync(
        Guid clubId,
        long? beforeSequence,
        int pageSize,
        CancellationToken cancellationToken);
}
