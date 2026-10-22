namespace Tactical11Manager.Contracts.Forum;

/// <summary>Machine-readable error codes for the community forum.</summary>
public static class ForumErrorCodes
{
    /// <summary>The thread title is missing.</summary>
    public const string TitleRequired = "FORUM_TITLE_REQUIRED";

    /// <summary>The thread title violates length limits (3 to 150 characters).</summary>
    public const string TitleLength = "FORUM_TITLE_LENGTH";

    /// <summary>The post body is missing.</summary>
    public const string BodyRequired = "FORUM_BODY_REQUIRED";

    /// <summary>The post body violates length limits (3 to 10,000 characters for thread, 1 to 10,000 for reply).</summary>
    public const string BodyLength = "FORUM_BODY_LENGTH";

    /// <summary>Announcements can only be published by staff / operators.</summary>
    public const string AnnouncementsRestricted = "FORUM_ANNOUNCEMENTS_RESTRICTED";

    /// <summary>The specified subforum was not found.</summary>
    public const string SubforumNotFound = "FORUM_SUBFORUM_NOT_FOUND";

    /// <summary>The specified thread was not found.</summary>
    public const string ThreadNotFound = "FORUM_THREAD_NOT_FOUND";

    /// <summary>The manager cannot access another country's forum.</summary>
    public const string CountryMismatch = "FORUM_COUNTRY_MISMATCH";
}
