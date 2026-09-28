using System.Globalization;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// The one job the AI transfer market is driven by, and the key that keeps it unique (`TRF-12`,
/// master plan §7.2, ADR-0003).
/// </summary>
/// <remarks>
/// A daily row keyed on the UTC day, the same shape the AI club evaluation uses: the row is the deadline, so
/// a worker that was down when the day opened runs the evaluation late rather than skipping it. The work
/// itself is idempotent — a club's listing and bid decisions carry deterministic daily keys that a replay
/// matches — so the at-least-once queue cannot create a second listing, bid, or reservation.
/// </remarks>
public static class AiMarketJobTypes
{
    /// <summary>Runs the AI market: surplus listings and bids for every club no human holds.</summary>
    public const string Evaluate = "market.evaluate-ai";

    /// <summary>Gets the business key of the day's evaluation job.</summary>
    /// <param name="day">The UTC day the evaluation belongs to.</param>
    public static string DayKey(DateOnly day) =>
        string.Create(CultureInfo.InvariantCulture, $"{Evaluate}:{day:yyyy-MM-dd}");
}
