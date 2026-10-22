using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.Forum;
using Tactical11Manager.Domain.Forum;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>Entity Framework Core implementation of <see cref="IForumRepository"/>.</summary>
public sealed class ForumRepository : IForumRepository
{
    private readonly Tactical11ManagerDbContext _dbContext;

    public ForumRepository(Tactical11ManagerDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<IReadOnlyList<ForumThread>> GetThreadsBySubforumAsync(
        string subforumId,
        Guid? countryId,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.ForumThreads.AsNoTracking()
            .Where(t => t.SubforumId == subforumId);

        if (countryId.HasValue)
        {
            query = query.Where(t => t.CountryId == countryId.Value);
        }

        return await query
            .OrderByDescending(t => t.IsPinned)
            .ThenByDescending(t => t.LastActivityAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ForumThread?> GetThreadByIdAsync(Guid threadId, CancellationToken cancellationToken)
    {
        return await _dbContext.ForumThreads
            .SingleOrDefaultAsync(t => t.Id == threadId, cancellationToken);
    }

    public async Task<IReadOnlyList<ForumPost>> GetPostsByThreadIdAsync(Guid threadId, CancellationToken cancellationToken)
    {
        return await _dbContext.ForumPosts.AsNoTracking()
            .Where(p => p.ThreadId == threadId)
            .OrderBy(p => p.PostNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetThreadCountBySubforumAsync(string subforumId, Guid? countryId, CancellationToken cancellationToken)
    {
        var query = _dbContext.ForumThreads.AsNoTracking()
            .Where(t => t.SubforumId == subforumId);

        if (countryId.HasValue)
        {
            query = query.Where(t => t.CountryId == countryId.Value);
        }

        return await query.CountAsync(cancellationToken);
    }

    public async Task<int> GetPostCountBySubforumAsync(string subforumId, Guid? countryId, CancellationToken cancellationToken)
    {
        var query = from t in _dbContext.ForumThreads.AsNoTracking()
                    where t.SubforumId == subforumId && (!countryId.HasValue || t.CountryId == countryId.Value)
                    join p in _dbContext.ForumPosts.AsNoTracking() on t.Id equals p.ThreadId
                    select p.Id;

        return await query.CountAsync(cancellationToken);
    }

    public async Task<DateTimeOffset?> GetLatestActivityBySubforumAsync(string subforumId, Guid? countryId, CancellationToken cancellationToken)
    {
        var query = _dbContext.ForumThreads.AsNoTracking()
            .Where(t => t.SubforumId == subforumId);

        if (countryId.HasValue)
        {
            query = query.Where(t => t.CountryId == countryId.Value);
        }

        return await query
            .OrderByDescending(t => t.LastActivityAt)
            .Select(t => (DateTimeOffset?)t.LastActivityAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<(string ManagerName, string? ClubName, Guid? CountryId)?> GetManagerContextAsync(
        Guid managerId,
        CancellationToken cancellationToken)
    {
        var managerInfo = await (
            from manager in _dbContext.Managers
            join user in _dbContext.Users on manager.UserId equals user.Id
            where manager.Id == managerId
            select new { user.DisplayName }).SingleOrDefaultAsync(cancellationToken);

        if (managerInfo == null)
        {
            return null;
        }

        var clubInfo = await (
            from tenure in _dbContext.ClubTenures
            join club in _dbContext.Clubs on tenure.ClubId equals club.Id
            where tenure.ManagerId == managerId && tenure.ControlStatus != ClubTenureControlStatus.Closed
            orderby tenure.StartedAt descending
            select new { club.Name, club.CountryId }).FirstOrDefaultAsync(cancellationToken);

        return (managerInfo.DisplayName, clubInfo?.Name, clubInfo?.CountryId);
    }

    public void AddThread(ForumThread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        _dbContext.ForumThreads.Add(thread);
    }

    public void AddPost(ForumPost post)
    {
        ArgumentNullException.ThrowIfNull(post);
        _dbContext.ForumPosts.Add(post);
    }
}
