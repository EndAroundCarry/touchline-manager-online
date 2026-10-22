namespace Tactical11Manager.Domain.Forum;

/// <summary>
/// A single post or reply within a forum thread.
/// </summary>
public sealed class ForumPost
{
    private ForumPost()
    {
    }

    /// <summary>Gets the unique identity (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the thread this post belongs to.</summary>
    public Guid ThreadId { get; private set; }

    /// <summary>Gets the post number within the thread (1 = initial post, 2+ = replies).</summary>
    public int PostNumber { get; private set; }

    /// <summary>Gets the author's manager identity.</summary>
    public Guid AuthorManagerId { get; private set; }

    /// <summary>Gets the author's manager display name at post time.</summary>
    public string AuthorManagerName { get; private set; } = string.Empty;

    /// <summary>Gets the author's club name at post time, if held.</summary>
    public string? AuthorClubName { get; private set; }

    /// <summary>Gets whether the author posted with staff privileges.</summary>
    public bool IsStaff { get; private set; }

    /// <summary>Gets the body text.</summary>
    public string Body { get; private set; } = string.Empty;

    /// <summary>Gets when the post was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Creates a new forum post or reply.</summary>
    public static ForumPost Create(
        Guid id,
        Guid threadId,
        int postNumber,
        Guid authorManagerId,
        string authorManagerName,
        string? authorClubName,
        bool isStaff,
        string body,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorManagerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        var trimmedBody = body.Trim();
        var minChars = postNumber == 1 ? ForumConstants.ThreadBodyMinChars : ForumConstants.ReplyBodyMinChars;
        if (trimmedBody.Length < minChars || trimmedBody.Length > ForumConstants.BodyMaxChars)
        {
            throw new ArgumentException(
                $"Post body must be between {minChars} and {ForumConstants.BodyMaxChars} characters.",
                nameof(body));
        }

        if (postNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(postNumber), "Post number must be positive.");
        }

        return new ForumPost
        {
            Id = id,
            ThreadId = threadId,
            PostNumber = postNumber,
            AuthorManagerId = authorManagerId,
            AuthorManagerName = authorManagerName.Trim(),
            AuthorClubName = string.IsNullOrWhiteSpace(authorClubName) ? null : authorClubName.Trim(),
            IsStaff = isStaff,
            Body = trimmedBody,
            CreatedAt = now,
        };
    }
}
