using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Market;

/// <summary>
/// The arithmetic of a timed auction: the floor, the minimum raise, and the winning order (`TRF-1`, `TRF-5`,
/// `TRF-8`).
/// </summary>
/// <remarks>
/// Kept here rather than in the use case so the client, the bid use case, and the resolution job all reason
/// about a bid the same way, and so the increment is a rule the rule set owns (`RULE-1`).
/// </remarks>
public static class AuctionRules
{
    /// <summary>Gets the smallest amount that may be bid, given any current leading amount (`TRF-1`, `TRF-5`).</summary>
    /// <param name="minimumFeeMinor">The listing's minimum fee, in minor units.</param>
    /// <param name="currentLeadingMinor">The current leading amount, or null when there is none.</param>
    /// <returns>The smallest acceptable bid, in minor units.</returns>
    public static long MinimumAcceptableBid(long minimumFeeMinor, long? currentLeadingMinor) =>
        currentLeadingMinor is { } leading
            ? leading + WorldRuleSet.MinimumBidIncrementMinor
            : minimumFeeMinor;

    /// <summary>Reports whether an amount clears the minimum raise over the current leading amount (`TRF-5`).</summary>
    /// <param name="minimumFeeMinor">The listing's minimum fee, in minor units.</param>
    /// <param name="currentLeadingMinor">The current leading amount, or null when there is none.</param>
    /// <param name="amountMinor">The proposed amount.</param>
    public static bool IsAcceptableBid(long minimumFeeMinor, long? currentLeadingMinor, long amountMinor) =>
        amountMinor >= MinimumAcceptableBid(minimumFeeMinor, currentLeadingMinor);

    /// <summary>Chooses the winning bid under the tie-break order (`TRF-8`), or null when there are none.</summary>
    /// <param name="bids">The bids to order.</param>
    public static TransferBid? Winner(IEnumerable<TransferBid> bids)
    {
        ArgumentNullException.ThrowIfNull(bids);

        return bids.OrderBy(bid => bid.OrderKey).FirstOrDefault();
    }
}
