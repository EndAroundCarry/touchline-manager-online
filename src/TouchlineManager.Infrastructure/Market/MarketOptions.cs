namespace TouchlineManager.Infrastructure.Market;

/// <summary>
/// Switches and timing for the transfer-auction resolver (`TRF-2`, master plan §7.2).
/// </summary>
/// <remarks>
/// Gated by configuration rather than an <c>ops.feature_flags</c> row because the flag table does not exist
/// yet; disabled by default, so a deployment that has not opted in never settles a transfer.
/// </remarks>
public sealed class MarketOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Auctions";

    /// <summary>
    /// Gets or sets whether the worker materialises and runs auction resolutions. Off unless explicitly
    /// enabled, so a half-configured market cannot move a player by accident.
    /// </summary>
    public bool EnableAuctions { get; set; }

    /// <summary>Gets or sets how often the materialiser checks for due listings, in seconds.</summary>
    public int CheckIntervalSeconds { get; set; } = 600;
}
