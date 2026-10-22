namespace Tactical11Manager.Domain.Forum;

/// <summary>Domain rules, limits, and identifiers for the community forum.</summary>
public static class ForumConstants
{
    /// <summary>Minimum title length in characters.</summary>
    public const int TitleMinChars = 3;

    /// <summary>Maximum title length in characters.</summary>
    public const int TitleMaxChars = 150;

    /// <summary>Minimum post body length in characters for a new thread.</summary>
    public const int ThreadBodyMinChars = 3;

    /// <summary>Minimum reply body length in characters.</summary>
    public const int ReplyBodyMinChars = 1;

    /// <summary>Maximum body length in characters.</summary>
    public const int BodyMaxChars = 10_000;

    /// <summary>Slug for International section.</summary>
    public const string SectionInternational = "international";

    /// <summary>Slug for Country section.</summary>
    public const string SectionCountry = "country";

    /// <summary>Subforum slug: International General.</summary>
    public const string SubforumInternationalGeneral = "international-general";

    /// <summary>Subforum slug: International Development and Bugs.</summary>
    public const string SubforumInternationalDevBugs = "international-development-bugs";

    /// <summary>Subforum slug: International Announcements.</summary>
    public const string SubforumInternationalAnnouncements = "international-announcements";

    /// <summary>Subforum slug: Country General.</summary>
    public const string SubforumCountryGeneral = "country-general";

    /// <summary>Language policy specification for International Forum.</summary>
    public const string InternationalLanguageNotice = "All posts in the International Forum must be in English.";
}
