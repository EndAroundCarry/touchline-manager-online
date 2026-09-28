using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Application.Comms;
using TouchlineManager.Domain.Comms;

namespace TouchlineManager.Application.Market;

/// <summary>
/// Writes the messages a market event produces (`TRF-4`, `TRF-10`, `F-41`).
/// </summary>
/// <remarks>
/// An AI club has no manager, so its targets are skipped — the same "who is told" rule the matchday notifier
/// follows. The messages are staged, never saved, so they commit inside the resolution or bid transaction that
/// produced them.
/// </remarks>
public sealed class MarketNotifications
{
    private readonly IInboxRepository _inbox;
    private readonly INewsRepository _news;

    /// <summary>Initializes the composer.</summary>
    public MarketNotifications(IInboxRepository inbox, INewsRepository news)
    {
        _inbox = inbox;
        _news = news;
    }

    /// <summary>Tells a club its leading bid was beaten (`TRF-7`).</summary>
    /// <param name="clubId">The outbid club.</param>
    /// <param name="playerId">The player bid for, which resolves its name.</param>
    /// <param name="newAmountMinor">The amount that beat it, in minor units.</param>
    /// <param name="listingId">The listing.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task NotifyOutbidAsync(
        Guid clubId,
        Guid playerId,
        long newAmountMinor,
        Guid listingId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var managerId = await ManagerOfAsync(clubId, cancellationToken);

        if (managerId is null)
        {
            return;
        }

        var names = await _inbox.FindPlayerNamesAsync([playerId], cancellationToken);
        var playerName = names.TryGetValue(playerId, out var name) ? name : "a player";

        _inbox.Add(Message(
            managerId.Value,
            MarketInboxTemplates.OutbidBy(playerName, newAmountMinor, listingId),
            now));
    }

    /// <summary>Tells the buyer and the seller a transfer completed (`TRF-10`).</summary>
    /// <param name="worldId">The world, which scopes the transfer's news.</param>
    /// <param name="buyerClubId">The buying club.</param>
    /// <param name="sellerClubId">The selling club.</param>
    /// <param name="playerId">The player who moved.</param>
    /// <param name="feeMinor">The settled fee, in minor units.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task NotifyTransferAsync(
        Guid? worldId,
        Guid buyerClubId,
        Guid sellerClubId,
        Guid playerId,
        long feeMinor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var targets = (await _inbox.FindClubTargetsAsync([buyerClubId, sellerClubId], cancellationToken))
            .ToDictionary(target => target.ClubId);
        var names = await _inbox.FindPlayerNamesAsync([playerId], cancellationToken);
        var playerName = names.TryGetValue(playerId, out var name) ? name : "a player";

        var buyerName = targets.TryGetValue(buyerClubId, out var buyerTarget) ? buyerTarget.Name : "a club";
        var sellerName = targets.TryGetValue(sellerClubId, out var sellerTarget) ? sellerTarget.Name : "a club";

        if (Target(targets, buyerClubId) is { } buyer)
        {
            _inbox.Add(Message(
                buyer,
                MarketInboxTemplates.BidWonMessage(playerName, feeMinor, sellerName, playerId),
                now));
        }

        if (Target(targets, sellerClubId) is { } sellerManager)
        {
            _inbox.Add(Message(
                sellerManager,
                MarketInboxTemplates.PlayerSoldMessage(playerName, feeMinor, buyerName, playerId),
                now));
        }

        // The feed is public: a transfer is news whether or not either club has a human manager (COM-1).
        if (worldId is { } world)
        {
            var draft = NewsTemplates.Transfer(playerName, feeMinor, buyerName, sellerName);

            _news.Add(NewsItem.Publish(
                Guid.CreateVersion7(),
                world,
                draft.CountryId,
                draft.DivisionId,
                draft.Category,
                draft.TemplateKey,
                draft.ParametersJson,
                publishedAt: now,
                expiresAt: null,
                now));
        }
    }

    private static Guid? Target(Dictionary<Guid, ClubInboxTarget> targets, Guid clubId) =>
        targets.TryGetValue(clubId, out var target) && target.ManagerId is { } manager && manager != Guid.Empty
            ? manager
            : null;

    private async Task<Guid?> ManagerOfAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var targets = await _inbox.FindClubTargetsAsync([clubId], cancellationToken);
        var target = targets.FirstOrDefault(candidate => candidate.ClubId == clubId);

        return target?.ManagerId is { } manager && manager != Guid.Empty ? manager : null;
    }

    private static InboxMessage Message(Guid managerId, InboxDraft draft, DateTimeOffset now) =>
        InboxMessage.Record(
            Guid.CreateVersion7(),
            managerId,
            draft.Category,
            draft.TemplateKey,
            draft.ParametersJson,
            draft.RelatedEntityId,
            now);
}
