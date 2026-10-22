using Tactical11Manager.Domain.Forum;

namespace Tactical11Manager.Application.Abstractions.Forum;

/// <summary>Repository for forum threads and posts.</summary>
public interface IForumRepository
{
    /// <summary>Reads threads for a subforum, ordered by pinned first then latest activity descending.</summary>
    Task<IReadOnlyList<ForumThread>> GetThreadsBySubforumAsync(string subforumId, Guid? countryId, CancellationToken cancellationToken);

    /// <summary>Reads a single thread by identity.</summary>
    Task<ForumThread?> GetThreadByIdAsync(Guid threadId, CancellationToken cancellationToken);

    /// <summary>Reads all posts in a thread in chronological order.</summary>
    Task<IReadOnlyList<ForumPost>> GetPostsByThreadIdAsync(Guid threadId, CancellationToken cancellationToken);

    /// <summary>Counts threads in a subforum.</summary>
    Task<int> GetThreadCountBySubforumAsync(string subforumId, Guid? countryId, CancellationToken cancellationToken);

    /// <summary>Counts posts in a subforum.</summary>
    Task<int> GetPostCountBySubforumAsync(string subforumId, Guid? countryId, CancellationToken cancellationToken);

    /// <summary>Gets the latest activity timestamp in a subforum, if any.</summary>
    Task<DateTimeOffset?> GetLatestActivityBySubforumAsync(string subforumId, Guid? countryId, CancellationToken cancellationToken);

    /// <summary>Gets a manager's context (display name, held club name, and country ID) for forum authorship and authorization.</summary>
    Task<(string ManagerName, string? ClubName, Guid? CountryId)?> GetManagerContextAsync(Guid managerId, CancellationToken cancellationToken);

    /// <summary>Adds a new thread.</summary>
    void AddThread(ForumThread thread);

    /// <summary>Adds a new post.</summary>
    void AddPost(ForumPost post);
}
