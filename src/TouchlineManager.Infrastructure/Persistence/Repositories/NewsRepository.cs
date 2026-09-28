using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Domain.Comms;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The news feed's persistence (`COM-1`, master plan §6.9).
/// </summary>
/// <remarks>
/// A module of its own with a port of its own (`MOD-1`). It stages and never saves, so an item commits with
/// the event that produced it, and the read is a keyset walk against the same total order the page's cursor is
/// built from.
/// </remarks>
internal sealed class NewsRepository : INewsRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public NewsRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public void Add(NewsItem item) => _dbContext.NewsItems.Add(item);

    /// <inheritdoc />
    public async Task<IReadOnlyList<NewsItem>> LoadAsync(
        NewsPageQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var items = _dbContext.NewsItems.AsQueryable();

        if (query.DivisionId is { } divisionId)
        {
            items = items.Where(item => item.DivisionId == divisionId);
        }

        if (query.CountryId is { } countryId)
        {
            items = items.Where(item => item.CountryId == countryId);
        }

        if (query.Before is { } before)
        {
            // The identity breaks a same-instant tie, which a publication produces: every item a round writes
            // carries the one instant it ran at, so the instant alone is not a total order.
            items = items.Where(item =>
                item.PublishedAt < before.PublishedAt
                || (item.PublishedAt == before.PublishedAt && item.Id.CompareTo(before.Id) < 0));
        }

        return await items
            .OrderByDescending(item => item.PublishedAt)
            .ThenByDescending(item => item.Id)
            .Take(query.Take)
            .ToListAsync(cancellationToken);
    }
}
