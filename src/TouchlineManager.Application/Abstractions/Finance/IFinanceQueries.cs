using TouchlineManager.Domain.Finance;

namespace TouchlineManager.Application.Abstractions.Finance;

/// <summary>
/// One club's basis for gate revenue: its fixed stadium baseline and the tier it plays in (`FIN-3`).
/// </summary>
/// <remarks>
/// The baseline is the whole of "how big is the ground", and it is already scaled to the tier, so the tier
/// is carried beside it rather than derived from it: the safety and warning reads want the tier too, and a
/// second read that must agree with the first is a second read that can disagree.
/// </remarks>
/// <param name="ClubId">The club.</param>
/// <param name="StadiumBaseline">The club's fixed stadium baseline, in minor units.</param>
/// <param name="TierNumber">The tier the club plays in, 1 and up.</param>
public sealed record ClubRevenueBasis(Guid ClubId, long StadiumBaseline, int TierNumber);

/// <summary>
/// One club's obligations for a week's finance run: the tier it plays in and what its squad costs (`FIN-7`).
/// </summary>
/// <param name="ClubId">The club.</param>
/// <param name="TierNumber">The tier the club plays in, which scales the sponsorship and the operating cost.</param>
/// <param name="WeeklyWageMinor">The club's active contracts' total weekly wage, in minor units.</param>
/// <param name="ContractedPlayers">How many active contracts that total covers.</param>
public sealed record ClubWeeklyObligations(
    Guid ClubId,
    int TierNumber,
    long WeeklyWageMinor,
    int ContractedPlayers);

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
    /// Reads every club's tier and weekly wage bill for the running season (`FIN-4`, `FIN-7`, `FIN-9`).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One row per club in the current season, or empty before a season exists.</returns>
    Task<IReadOnlyList<ClubWeeklyObligations>> GetWeeklyObligationsAsync(
        CancellationToken cancellationToken);

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
