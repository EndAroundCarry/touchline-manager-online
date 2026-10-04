namespace TouchlineManager.Contracts.Comms;

/// <summary>One item in the division news feed (`COM-1`, master plan §10.7).</summary>
/// <remarks>
/// The rendered English is carried beside the stable <paramref name="Category"/> and
/// <paramref name="TemplateKey"/> so a client shows a sentence today and can re-render the same item in
/// another language when the template tokens are localized (master plan §8.6). The raw parameters stay
/// server-side.
/// </remarks>
/// <param name="Id">The item.</param>
/// <param name="Category">The kind of event, a stable code.</param>
/// <param name="CountryId">The country the item is scoped to, or null for a world-wide item.</param>
/// <param name="DivisionId">The division the item is scoped to, or null.</param>
/// <param name="Title">The rendered headline.</param>
/// <param name="Body">The rendered detail.</param>
/// <param name="PublishedAt">When it was published.</param>
/// <param name="Spoiler">
/// What <paramref name="Title"/> and <paramref name="Body"/> hold back because it gives a match away — the score —
/// or null. The client shows it only when the manager asks, or has watched the match.
/// </param>
/// <param name="MatchId">
/// The match a result item reports, or null. A client remembers a shown result under it, so watching the match
/// shows the result here and showing it here shows it in the match viewer.
/// </param>
public sealed record NewsItemResponse(
    Guid Id,
    string Category,
    Guid? CountryId,
    Guid? DivisionId,
    string Title,
    string Body,
    DateTimeOffset PublishedAt,
    string? Spoiler = null,
    Guid? MatchId = null);

/// <summary>One page of the news feed (`COM-1`).</summary>
/// <param name="Items">The page, newest first.</param>
/// <param name="NextCursor">Where to continue, or null when this is the last page.</param>
/// <param name="ServerTime">The server's current instant (`TIME-5`).</param>
public sealed record NewsResponse(
    IReadOnlyList<NewsItemResponse> Items,
    string? NextCursor,
    DateTimeOffset ServerTime);
