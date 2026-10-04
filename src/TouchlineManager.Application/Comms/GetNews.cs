using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Contracts.Comms;
using TouchlineManager.Domain.Comms;

namespace TouchlineManager.Application.Comms;

/// <summary>The result of reading the news feed.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="News">The page, when the read succeeded.</param>
public sealed record GetNewsResult(CommsOutcome Outcome, NewsResponse? News);

/// <summary>
/// Reads one page of the division news feed (`COM-1`, master plan §10.7).
/// </summary>
/// <remarks>
/// The feed is public game data, so it needs no manager profile, but it does need a world. A page is
/// keyset-paginated because it grows from the top, and the filters are optional scope — a division's own
/// feed, a country's feed, or the whole world's.
/// </remarks>
public sealed class GetNews
{
    /// <summary>The number of items a page holds.</summary>
    public const int PageSize = 25;

    private readonly INewsRepository _news;
    private readonly IWorldRepository _world;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetNews(INewsRepository news, IWorldRepository world, IClock clock)
    {
        _news = news;
        _world = world;
        _clock = clock;
    }

    /// <summary>Reads a page of the feed.</summary>
    /// <param name="divisionId">Whether to restrict the feed to one division.</param>
    /// <param name="countryId">Whether to restrict the feed to one country.</param>
    /// <param name="cursor">Where to continue from, or null for the first page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetNewsResult> ExecuteAsync(
        Guid? divisionId,
        Guid? countryId,
        string? cursor,
        CancellationToken cancellationToken)
    {
        if (!NewsCursor.TryDecode(cursor, out var before))
        {
            return new GetNewsResult(CommsOutcome.InvalidCursor, null);
        }

        if (await _world.FindWorldAsync(cancellationToken) is null)
        {
            return new GetNewsResult(CommsOutcome.WorldNotSeeded, null);
        }

        var rows = await _news.LoadAsync(
            new NewsPageQuery(divisionId, countryId, before, PageSize + 1),
            cancellationToken);

        var hasMore = rows.Count > PageSize;
        var page = hasMore ? rows.Take(PageSize).ToList() : [.. rows];

        var next = hasMore
            ? NewsCursor.Encode(new NewsCursorPosition(page[^1].PublishedAt, page[^1].Id))
            : null;

        return new GetNewsResult(
            CommsOutcome.Ok,
            new NewsResponse(
                [.. page.Select(NewsMapping.ToResponse)],
                next,
                _clock.UtcNow));
    }
}

/// <summary>
/// Maps a stored news item to its transport projection (`COM-1`).
/// </summary>
/// <remarks>
/// The rendered text is derived here from the item's template and parameters rather than stored, which is what
/// keeps the wording a presentation concern instead of a fact baked into the row.
/// </remarks>
internal static class NewsMapping
{
    /// <summary>Projects one stored item, rendering its template.</summary>
    /// <param name="item">The stored item.</param>
    public static NewsItemResponse ToResponse(this NewsItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var text = NewsMessageText.Render(item.TemplateKey, item.ParametersJson);

        return new NewsItemResponse(
            item.Id,
            item.Category.ToCode(),
            item.CountryId,
            item.DivisionId,
            text.Title,
            text.Body,
            item.PublishedAt,
            text.Spoiler,
            text.MatchId);
    }
}
