namespace Tactical11Manager.Domain.Forum;

/// <summary>
/// A forum thread inside an International or Country subforum.
/// </summary>
public sealed class ForumThread
{
    private ForumThread()
    {
    }

    /// <summary>Gets the unique identity (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the subforum slug.</summary>
    public string SubforumId { get; private set; } = string.Empty;

    /// <summary>Gets the country identity if this is a country forum thread, or null for international.</summary>
    public Guid? CountryId { get; private set; }

    /// <summary>Gets the thread title.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Gets the author's manager identity.</summary>
    public Guid AuthorManagerId { get; private set; }

    /// <summary>Gets the author's manager display name at creation time.</summary>
    public string AuthorManagerName { get; private set; } = string.Empty;

    /// <summary>Gets the author's club name at creation time, if held.</summary>
    public string? AuthorClubName { get; private set; }

    /// <summary>Gets whether the thread is pinned to the top of the subforum.</summary>
    public bool IsPinned { get; private set; }

    /// <summary>Gets whether the thread is an announcement.</summary>
    public bool IsAnnouncement { get; private set; }

    /// <summary>Gets how many replies have been posted (excluding opening post).</summary>
    public int ReplyCount { get; private set; }

    /// <summary>Gets when the thread was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the most recent activity (post or reply) occurred.</summary>
    public DateTimeOffset LastActivityAt { get; private set; }

    /// <summary>Gets the display name of the latest reply author, or null.</summary>
    public string? LatestReplyAuthorName { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Creates a new regular forum thread.</summary>
    public static ForumThread Create(
        Guid id,
        string subforumId,
        Guid? countryId,
        string title,
        Guid authorManagerId,
        string authorManagerName,
        string? authorClubName,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subforumId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorManagerName);

        var trimmedTitle = title.Trim();
        if (trimmedTitle.Length < ForumConstants.TitleMinChars || trimmedTitle.Length > ForumConstants.TitleMaxChars)
        {
            throw new ArgumentException(
                $"Title must be between {ForumConstants.TitleMinChars} and {ForumConstants.TitleMaxChars} characters.",
                nameof(title));
        }

        return new ForumThread
        {
            Id = id,
            SubforumId = subforumId.Trim().ToLowerInvariant(),
            CountryId = countryId,
            Title = trimmedTitle,
            AuthorManagerId = authorManagerId,
            AuthorManagerName = authorManagerName.Trim(),
            AuthorClubName = string.IsNullOrWhiteSpace(authorClubName) ? null : authorClubName.Trim(),
            IsPinned = false,
            IsAnnouncement = false,
            ReplyCount = 0,
            CreatedAt = now,
            LastActivityAt = now,
            LatestReplyAuthorName = null,
            Version = 1,
        };
    }

    /// <summary>Creates an official announcement or pinned thread.</summary>
    public static ForumThread CreateAnnouncement(
        Guid id,
        string subforumId,
        Guid? countryId,
        string title,
        Guid authorManagerId,
        string authorManagerName,
        string? authorClubName,
        bool isPinned,
        DateTimeOffset now)
    {
        var thread = Create(id, subforumId, countryId, title, authorManagerId, authorManagerName, authorClubName, now);
        thread.IsPinned = isPinned;
        thread.IsAnnouncement = true;
        return thread;
    }

    /// <summary>Records that a new reply was added to this thread.</summary>
    public void RecordReply(string replyAuthorName, DateTimeOffset replyAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replyAuthorName);

        ReplyCount++;
        LastActivityAt = replyAt;
        LatestReplyAuthorName = replyAuthorName.Trim();
        Version++;
    }
}
