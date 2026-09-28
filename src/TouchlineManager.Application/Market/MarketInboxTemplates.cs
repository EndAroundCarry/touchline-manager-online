using System.Text.Json;
using TouchlineManager.Application.Comms;
using TouchlineManager.Domain.Comms;

namespace TouchlineManager.Application.Market;

/// <summary>
/// The market's inbox templates: an outbid bidder, a completed signing, and a completed sale (`TRF-4`,
/// `TRF-10`, `F-41`).
/// </summary>
/// <remarks>
/// The same durable-token contract the rest of the inbox uses (`MAT-8`): a stable key and a parameter
/// document, rendered by <see cref="InboxMessageText"/>. They sit on the existing squad shelf rather than
/// adding a market shelf, because the MVP does not introduce a new category for them.
/// </remarks>
public static class MarketInboxTemplates
{
    /// <summary>A leading bid was beaten by a higher one (`TRF-7`).</summary>
    public const string Outbid = "inbox.market.outbid";

    /// <summary>The club's bid won a listing and the transfer completed (`TRF-10`).</summary>
    public const string BidWon = "inbox.market.bid_won";

    /// <summary>The club's listed player was sold (`TRF-10`).</summary>
    public const string PlayerSold = "inbox.market.player_sold";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Builds the message an outbid club receives.</summary>
    /// <param name="playerName">The player bid for.</param>
    /// <param name="newAmountMinor">The amount that beat the club's bid, in minor units.</param>
    /// <param name="listingId">The listing, which the message links to.</param>
    public static InboxDraft OutbidBy(string playerName, long newAmountMinor, Guid listingId) =>
        Write(Outbid, new OutbidParameters(playerName, newAmountMinor), listingId);

    /// <summary>Builds the message a winning bidder receives.</summary>
    /// <param name="playerName">The player signed.</param>
    /// <param name="feeMinor">The settled fee, in minor units.</param>
    /// <param name="sellerClubName">The club the player came from.</param>
    /// <param name="playerId">The player, which the message links to.</param>
    public static InboxDraft BidWonMessage(string playerName, long feeMinor, string sellerClubName, Guid playerId) =>
        Write(BidWon, new BidWonParameters(playerName, feeMinor, sellerClubName), playerId);

    /// <summary>Builds the message a selling club receives.</summary>
    /// <param name="playerName">The player sold.</param>
    /// <param name="feeMinor">The settled fee, in minor units.</param>
    /// <param name="buyerClubName">The club the player joined.</param>
    /// <param name="playerId">The player, which the message links to.</param>
    public static InboxDraft PlayerSoldMessage(string playerName, long feeMinor, string buyerClubName, Guid playerId) =>
        Write(PlayerSold, new PlayerSoldParameters(playerName, feeMinor, buyerClubName), playerId);

    /// <summary>Reads a stored parameter document back.</summary>
    internal static TParameters Read<TParameters>(string parametersJson) =>
        JsonSerializer.Deserialize<TParameters>(parametersJson, Options)
        ?? throw new InvalidOperationException("A stored inbox message carries no parameters.");

    internal sealed record OutbidParameters(string PlayerName, long NewAmountMinor);

    internal sealed record BidWonParameters(string PlayerName, long FeeMinor, string SellerClubName);

    internal sealed record PlayerSoldParameters(string PlayerName, long FeeMinor, string BuyerClubName);

    private static InboxDraft Write<TParameters>(string templateKey, TParameters parameters, Guid relatedEntityId) =>
        new(
            InboxCategory.Squad,
            templateKey,
            JsonSerializer.Serialize(parameters, Options),
            relatedEntityId);
}
