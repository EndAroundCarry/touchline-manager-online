using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Domain.Comms;

namespace TouchlineManager.Application.Comms;

/// <summary>
/// Writes division news items in the caller's transaction (`COM-1`, ADR-0029).
/// </summary>
/// <remarks>
/// One writer rather than a news row built at each call site, so an activation, a transfer, and a published
/// round all stamp the world, the scope, and the instant the same way. It stages and never saves: the news
/// commits with the event that produced it, which is what makes the feed a view of the world rather than a
/// second, driftable record of it.
/// </remarks>
public sealed class PostNews
{
    private readonly INewsRepository _news;
    private readonly IClock _clock;

    /// <summary>Initializes the writer.</summary>
    public PostNews(INewsRepository news, IClock clock)
    {
        _news = news;
        _clock = clock;
    }

    /// <summary>Posts the item a newly activated tier produces.</summary>
    public void DivisionActivated(
        Guid worldId,
        Guid countryId,
        Guid divisionId,
        string countryName,
        int tierNumber,
        string divisionName)
    {
        var now = _clock.UtcNow;

        _news.Add(NewsItem.Publish(
            Guid.CreateVersion7(),
            worldId,
            countryId,
            divisionId,
            NewsCategory.Division,
            NewsTemplates.DivisionProvisioned,
            NewsTemplates.DivisionActivated(countryName, tierNumber, divisionName, countryId, divisionId).ParametersJson,
            publishedAt: now,
            expiresAt: null,
            now));
    }

    /// <summary>Posts the item a completed transfer produces.</summary>
    public void TransferCompleted(
        Guid worldId,
        string playerName,
        long feeMinor,
        string buyerClubName,
        string sellerClubName)
    {
        var draft = NewsTemplates.Transfer(playerName, feeMinor, buyerClubName, sellerClubName);
        var now = _clock.UtcNow;

        _news.Add(NewsItem.Publish(
            Guid.CreateVersion7(),
            worldId,
            draft.CountryId,
            draft.DivisionId,
            draft.Category,
            draft.TemplateKey,
            draft.ParametersJson,
            publishedAt: now,
            expiresAt: null,
            now));
    }

    /// <summary>Posts the item a published round produces.</summary>
    public void RoundPublished(
        Guid worldId,
        Guid divisionId,
        int roundNumber,
        string homeClubName,
        int homeGoals,
        string awayClubName,
        int awayGoals)
    {
        var draft = NewsTemplates.Round(
            roundNumber,
            homeClubName,
            homeGoals,
            awayClubName,
            awayGoals,
            divisionId);
        var now = _clock.UtcNow;

        _news.Add(NewsItem.Publish(
            Guid.CreateVersion7(),
            worldId,
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
