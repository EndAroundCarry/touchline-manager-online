namespace Tactical11Manager.Application.Forum;

/// <summary>Outcomes for forum commands.</summary>
public enum ForumOutcome
{
    /// <summary>The operation succeeded.</summary>
    Success = 0,

    /// <summary>Title is missing or invalid length.</summary>
    InvalidTitle = 1,

    /// <summary>Body is missing or invalid length.</summary>
    InvalidBody = 2,

    /// <summary>Announcements can only be published by staff.</summary>
    AnnouncementsRestricted = 3,

    /// <summary>Subforum does not exist.</summary>
    SubforumNotFound = 4,

    /// <summary>Thread does not exist.</summary>
    ThreadNotFound = 5,

    /// <summary>Manager cannot access this country forum.</summary>
    CountryMismatch = 6,

    /// <summary>Manager profile was not found.</summary>
    ManagerNotFound = 7,
}
