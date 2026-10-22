namespace Tactical11Manager.Contracts.World;

/// <summary>Request to create the account's manager profile (master plan §10.2).</summary>
/// <remarks>
/// One profile per account. The manager's name is the account's display name, so it is deliberately not
/// repeated here: two names for one person would raise the question of which one a league table shows.
/// </remarks>
public sealed record CreateManagerProfileRequest
{
    /// <summary>Gets the preferred locale for formatting, e.g. <c>en-GB</c>.</summary>
    public required string Locale { get; init; }

    /// <summary>Gets the IANA time zone used to render deadlines in local time (`CAL-4`).</summary>
    public required string TimeZone { get; init; }
}

/// <summary>
/// Request to change the manager's formatting preferences (master plan §10.2, `VOI-4`).
/// </summary>
/// <remarks>
/// Conditional on the profile version, so a change made on one device cannot silently overwrite one made
/// on another. The manager's name is the account's display name and is changed through <c>/me</c>, so only
/// the locale and time zone are here.
/// </remarks>
public sealed record UpdateManagerProfileRequest
{
    /// <summary>Gets the preferred locale for formatting, e.g. <c>en-GB</c>.</summary>
    public required string Locale { get; init; }

    /// <summary>Gets the IANA time zone used to render deadlines in local time (`CAL-4`).</summary>
    public required string TimeZone { get; init; }
}

/// <summary>Request to choose the two colours and the crest the manager's club wears.</summary>
/// <remarks>
/// The club is never named in the body: it is the club the authenticated manager holds. Colours are written
/// as <c>#rrggbb</c>, which is what a colour picker produces and what the renderer draws. The crest is drawn
/// in those same two colours, which is why it is chosen in the same request. Its shape and its stripes are
/// chosen independently, so any shape can wear any stripes.
/// </remarks>
public sealed record ChangeClubColoursRequest
{
    /// <summary>Gets the primary kit colour, e.g. <c>#1f4e79</c>.</summary>
    public required string PrimaryColour { get; init; }

    /// <summary>Gets the secondary kit colour, e.g. <c>#d6e4f0</c>. It must differ from the primary.</summary>
    public required string SecondaryColour { get; init; }

    /// <summary>Gets the crest shape code, e.g. <c>shield</c>. Omit it to keep the shape as it is.</summary>
    public string? CrestShape { get; init; }

    /// <summary>Gets the crest stripes code, e.g. <c>hoops</c>. Omit it to keep the stripes as they are.</summary>
    public string? CrestPattern { get; init; }
}

/// <summary>Request to take over an AI-controlled club (master plan §7.6).</summary>
/// <remarks>
/// The command identifies the club only. Ownership is never taken from the body: the manager is derived
/// from the authenticated account, and the country, tier, and current control of the club are read from
/// the database (master plan §10.9).
/// </remarks>
public sealed record ClaimClubRequest
{
    /// <summary>Gets the club to take over.</summary>
    public required Guid ClubId { get; init; }

    /// <summary>
    /// Gets how the club is taken over: <c>keep</c> to inherit everything as it stands, or <c>clean_start</c> to release the old players and
    /// begin from the tier's opening squad, ground, buildings and cash with no debt. Absent means <c>keep</c>.
    /// </summary>
    public string? Start { get; init; }
}
