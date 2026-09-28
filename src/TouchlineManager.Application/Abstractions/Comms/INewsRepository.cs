using TouchlineManager.Domain.Comms;

namespace TouchlineManager.Application.Abstractions.Comms;

/// <summary>Where a page of news starts, which is the last item the previous page returned.</summary>
/// <param name="PublishedAt">The instant the item was published.</param>
/// <param name="Id">The item's identity, which breaks a same-instant tie.</param>
public sealed record NewsCursorPosition(DateTimeOffset PublishedAt, Guid Id);

/// <summary>What a page of the news feed asks for.</summary>
/// <param name="DivisionId">Whether to restrict the feed to one division.</param>
/// <param name="CountryId">Whether to restrict the feed to one country.</param>
/// <param name="Before">Where to continue from, or null for the first page.</param>
/// <param name="Take">How many items to return.</param>
public sealed record NewsPageQuery(Guid? DivisionId, Guid? CountryId, NewsCursorPosition? Before, int Take);

/// <summary>
/// The news feed's persistence (`COM-1`, ADR-0029).
/// </summary>
/// <remarks>
/// It stages and never saves, so a news item commits with the event that produced it — a division activation,
/// a completed transfer, or a published round. The read is a keyset walk, because the feed grows from the top
/// the same way the inbox does.
/// </remarks>
public interface INewsRepository
{
    /// <summary>Stages a news item.</summary>
    /// <param name="item">The item.</param>
    void Add(NewsItem item);

    /// <summary>Reads one page of the feed, newest first.</summary>
    /// <param name="query">What the page asks for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<NewsItem>> LoadAsync(NewsPageQuery query, CancellationToken cancellationToken);
}
