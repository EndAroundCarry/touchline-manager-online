namespace Tactical11Manager.Contracts.Forum;

/// <summary>One subforum inside a forum section.</summary>
/// <param name="Id">Slug/identity of the subforum (e.g. international-general).</param>
/// <param name="SectionId">The section it belongs to (e.g. international or country).</param>
/// <param name="Name">Subforum name (e.g. General, Development and Bugs, Announcements).</param>
/// <param name="Description">Short description explaining what belongs here.</param>
/// <param name="CanCreateThreads">Whether regular managers can start new threads.</param>
/// <param name="ThreadCount">How many threads exist in this subforum.</param>
/// <param name="PostCount">How many posts (original posts + replies) exist in this subforum.</param>
/// <param name="LatestActivityAt">Timestamp of the latest activity, or null if empty.</param>
public sealed record ForumSubforumResponse(
    string Id,
    string SectionId,
    string Name,
    string Description,
    bool CanCreateThreads,
    int ThreadCount,
    int PostCount,
    DateTimeOffset? LatestActivityAt);

/// <summary>A top-level category or section in the forum (International or Country).</summary>
/// <param name="Id">Section identity (e.g. international, country).</param>
/// <param name="Title">Section title (e.g. International Forum, England Forum).</param>
/// <param name="Description">Section subtitle or summary.</param>
/// <param name="LanguageNotice">Language guidance, e.g. requiring English in international.</param>
/// <param name="CountryId">Country identity if this is a country forum.</param>
/// <param name="CountryName">Country name if this is a country forum.</param>
/// <param name="Subforums">Subforums under this section.</param>
public sealed record ForumSectionResponse(
    string Id,
    string Title,
    string Description,
    string? LanguageNotice,
    Guid? CountryId,
    string? CountryName,
    IReadOnlyList<ForumSubforumResponse> Subforums);

/// <summary>Overview of all forum sections visible to the manager.</summary>
/// <param name="Sections">Sections available (International and the manager's Country).</param>
/// <param name="ServerTime">Server instant.</param>
public sealed record ForumOverviewResponse(
    IReadOnlyList<ForumSectionResponse> Sections,
    DateTimeOffset ServerTime);

/// <summary>Summary of a thread for list views.</summary>
/// <param name="Id">Thread identity.</param>
/// <param name="SubforumId">Subforum slug.</param>
/// <param name="Title">Thread title.</param>
/// <param name="AuthorManagerId">Creator manager identity.</param>
/// <param name="AuthorManagerName">Creator display name.</param>
/// <param name="AuthorClubName">Creator club name, if held.</param>
/// <param name="IsPinned">Whether pinned to top.</param>
/// <param name="IsAnnouncement">Whether this is an official announcement.</param>
/// <param name="ReplyCount">Number of replies (excluding original post).</param>
/// <param name="CreatedAt">When the thread was started.</param>
/// <param name="LastActivityAt">When the latest post/reply was made.</param>
/// <param name="LatestReplyAuthorName">Author name of the latest reply, or null.</param>
public sealed record ForumThreadSummaryResponse(
    Guid Id,
    string SubforumId,
    string Title,
    Guid AuthorManagerId,
    string AuthorManagerName,
    string? AuthorClubName,
    bool IsPinned,
    bool IsAnnouncement,
    int ReplyCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    string? LatestReplyAuthorName);

/// <summary>Thread list for a subforum.</summary>
/// <param name="SubforumId">Subforum slug.</param>
/// <param name="SubforumName">Subforum display name.</param>
/// <param name="SectionId">Section slug.</param>
/// <param name="SectionTitle">Section display name.</param>
/// <param name="LanguageNotice">Section language specification, if any.</param>
/// <param name="CanCreateThreads">Whether the current manager can create threads here.</param>
/// <param name="Threads">List of threads, pinned first then newest activity first.</param>
public sealed record ForumThreadListResponse(
    string SubforumId,
    string SubforumName,
    string SectionId,
    string SectionTitle,
    string? LanguageNotice,
    bool CanCreateThreads,
    IReadOnlyList<ForumThreadSummaryResponse> Threads);

/// <summary>One post or reply in a thread.</summary>
/// <param name="Id">Post identity.</param>
/// <param name="ThreadId">Thread identity.</param>
/// <param name="PostNumber">Sequential number in thread (#1 = opening post).</param>
/// <param name="AuthorManagerId">Author manager identity.</param>
/// <param name="AuthorManagerName">Author manager display name.</param>
/// <param name="AuthorClubName">Author club name, if held.</param>
/// <param name="IsStaff">Whether author has official staff/operator status.</param>
/// <param name="Body">Post body text.</param>
/// <param name="CreatedAt">When the post was created.</param>
public sealed record ForumPostResponse(
    Guid Id,
    Guid ThreadId,
    int PostNumber,
    Guid AuthorManagerId,
    string AuthorManagerName,
    string? AuthorClubName,
    bool IsStaff,
    string Body,
    DateTimeOffset CreatedAt);

/// <summary>Complete thread details with posts.</summary>
/// <param name="Id">Thread identity.</param>
/// <param name="SubforumId">Subforum slug.</param>
/// <param name="SubforumName">Subforum name.</param>
/// <param name="SectionId">Section slug.</param>
/// <param name="SectionTitle">Section title.</param>
/// <param name="LanguageNotice">Section language specification, if any.</param>
/// <param name="Title">Thread title.</param>
/// <param name="AuthorManagerId">Creator manager identity.</param>
/// <param name="AuthorManagerName">Creator display name.</param>
/// <param name="AuthorClubName">Creator club name.</param>
/// <param name="IsPinned">Whether pinned.</param>
/// <param name="IsAnnouncement">Whether an announcement.</param>
/// <param name="CanReply">Whether replies are accepted.</param>
/// <param name="CreatedAt">Thread start instant.</param>
/// <param name="Posts">All posts in chronological order.</param>
public sealed record ForumThreadDetailResponse(
    Guid Id,
    string SubforumId,
    string SubforumName,
    string SectionId,
    string SectionTitle,
    string? LanguageNotice,
    string Title,
    Guid AuthorManagerId,
    string AuthorManagerName,
    string? AuthorClubName,
    bool IsPinned,
    bool IsAnnouncement,
    bool CanReply,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ForumPostResponse> Posts);
