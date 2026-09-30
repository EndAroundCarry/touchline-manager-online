namespace TouchlineManager.Domain.Comms;

/// <summary>
/// The kind of thing a news item reports, which the feed groups by (master plan §6.9, Stage 11).
/// </summary>
/// <remarks>
/// A category is presentation vocabulary, like the inbox's: it says which shelf an item sits on rather than
/// what happened. What happened is the <see cref="NewsItem.TemplateKey"/>, and these values are stable codes
/// because a client branches on them.
/// </remarks>
public enum NewsCategory
{
    /// <summary>A division was created or changed shape, e.g. a newly provisioned tier (`PYR-4`).</summary>
    Division = 0,

    /// <summary>A transfer completed (`TRF-10`).</summary>
    Transfer = 1,

    /// <summary>A division's round was published (`MAT-7`).</summary>
    Result = 2,

    /// <summary>An operator posted a game notice to every manager (`F-46`, ADR-0045).</summary>
    Announcement = 3,
}

/// <summary>Stable codes and parsing for <see cref="NewsCategory"/>.</summary>
public static class NewsCategories
{
    /// <summary>The longest code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 12;

    /// <summary>Every category, in declaration order.</summary>
    public static readonly IReadOnlyList<NewsCategory> All = [.. Enum.GetValues<NewsCategory>()];

    /// <summary>Converts a category to its stable code.</summary>
    /// <param name="category">The category.</param>
    public static string ToCode(this NewsCategory category) => category switch
    {
        NewsCategory.Division => "division",
        NewsCategory.Transfer => "transfer",
        NewsCategory.Result => "result",
        NewsCategory.Announcement => "announcement",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown news category."),
    };

    /// <summary>Parses a stable code back to its category.</summary>
    /// <param name="code">The stable code.</param>
    public static NewsCategory FromCode(string code) => code switch
    {
        "division" => NewsCategory.Division,
        "transfer" => NewsCategory.Transfer,
        "result" => NewsCategory.Result,
        "announcement" => NewsCategory.Announcement,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown news category code."),
    };
}

/// <summary>
/// One entry in a division's or country's public news feed (`COM-*`, master plan §6.9).
/// </summary>
/// <remarks>
/// <para>
/// The feed is public game data, so it is the counterpart of the inbox rather than a copy of it: an inbox
/// message is addressed to one manager and outlives a tenure (`COM-1`), while a news item is scoped to a
/// world, a country, or a division and addressed to anyone who can see that scope. It carries a stable
/// template key and parameters rather than prose, for the same reason the inbox does (`COM-2`).
/// </para>
/// </remarks>
public sealed class NewsItem
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private NewsItem()
    {
    }

    /// <summary>Gets the identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owning world.</summary>
    public Guid WorldId { get; private set; }

    /// <summary>Gets the country the item is scoped to, or null when it is world-scoped.</summary>
    public Guid? CountryId { get; private set; }

    /// <summary>Gets the division the item is scoped to, or null when it is country- or world-scoped.</summary>
    public Guid? DivisionId { get; private set; }

    /// <summary>Gets the category the feed groups it under.</summary>
    public NewsCategory Category { get; private set; }

    /// <summary>Gets the stable template the item renders from.</summary>
    public string TemplateKey { get; private set; } = string.Empty;

    /// <summary>Gets the template's parameters, as the document that gets stored.</summary>
    public string ParametersJson { get; private set; } = string.Empty;

    /// <summary>Gets when the item became public.</summary>
    public DateTimeOffset PublishedAt { get; private set; }

    /// <summary>Gets when the item stops being shown, or null when it does not expire.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>Gets when the row was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Publishes a news item.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="worldId">The owning world.</param>
    /// <param name="countryId">The country, or null for a world-scoped item.</param>
    /// <param name="divisionId">The division, or null for a broader scope.</param>
    /// <param name="category">The shelf it sits on.</param>
    /// <param name="templateKey">The stable template key.</param>
    /// <param name="parametersJson">The template's parameters.</param>
    /// <param name="publishedAt">When it became public.</param>
    /// <param name="expiresAt">When it stops being shown, or null.</param>
    /// <param name="now">The current instant.</param>
    public static NewsItem Publish(
        Guid id,
        Guid worldId,
        Guid? countryId,
        Guid? divisionId,
        NewsCategory category,
        string templateKey,
        string parametersJson,
        DateTimeOffset publishedAt,
        DateTimeOffset? expiresAt,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(parametersJson);

        if (expiresAt is not null && expiresAt <= publishedAt)
        {
            throw new ArgumentException("A news item cannot expire before it is published.", nameof(expiresAt));
        }

        return new NewsItem
        {
            Id = id,
            WorldId = worldId,
            CountryId = countryId,
            DivisionId = divisionId,
            Category = category,
            TemplateKey = templateKey,
            ParametersJson = parametersJson,
            PublishedAt = publishedAt,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }
}
