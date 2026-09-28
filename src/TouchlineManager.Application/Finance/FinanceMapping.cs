using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Contracts.Finance;
using TouchlineManager.Domain.Finance;
using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Application.Finance;

/// <summary>
/// Maps the finance module's read projections to their transport shape (master plan §10.7).
/// </summary>
/// <remarks>
/// One place, so the summary, the totals, and the ledger cannot be shaped differently by two screens. The
/// weekly credit and cost are derived here from the club's tier rather than stored on the summary, because
/// they are rule-set values and the rule set is where a balancing change happens (`RULE-1`).
/// </remarks>
public static class FinanceMapping
{
    /// <summary>Projects a club's money and season totals.</summary>
    /// <param name="snapshot">The read snapshot.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static FinanceSummaryResponse ToResponse(
        this FinanceSummarySnapshot snapshot,
        DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var tier = snapshot.TierNumber;

        return new FinanceSummaryResponse(
            snapshot.CashMinor,
            snapshot.ReservedMinor,
            snapshot.CashMinor - snapshot.ReservedMinor,
            snapshot.WeeklyWageMinor,
            snapshot.ContractedPlayers,
            WorldRuleSet.WeeklySponsorshipMinorForTier(tier),
            WorldRuleSet.WeeklyOperatingCostMinorForTier(tier),
            tier,
            snapshot.SeasonLabel,
            ClubWarnings.Calculate(snapshot),
            [.. snapshot.Totals.Select(total => new FinanceCategoryTotalResponse(total.Category.ToCode(), total.AmountMinor))],
            serverTime);
    }

    /// <summary>Projects one page of the ledger, rendering each line and naming the next cursor.</summary>
    /// <param name="page">The page.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static FinanceLedgerResponse ToResponse(this FinanceLedgerPage page, DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(page);

        var entries = page.Entries
            .Select(entry => new FinanceLedgerEntryResponse(
                entry.Id,
                entry.Sequence,
                entry.Category.ToCode(),
                entry.CashDeltaMinor,
                entry.ReservedDeltaMinor,
                entry.ResultingCashMinor,
                entry.ResultingReservedMinor,
                LedgerEntryText.Render(entry.DescriptionTemplate, entry.DescriptionParametersJson),
                entry.CreatedAt))
            .ToList();

        var next = page.HasMore && page.Entries.Count > 0
            ? FinanceLedgerCursor.Encode(page.Entries[^1].Sequence)
            : null;

        return new FinanceLedgerResponse(entries, next, serverTime);
    }
}
